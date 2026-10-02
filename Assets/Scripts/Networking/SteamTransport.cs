using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Steamworks;

namespace SphereRoom.Networking
{
    /// <summary>
    /// 自写 NGO 传输层（超级加分项「Steam 联机」）：
    /// 基于 Facepunch.Steamworks（Steam 官方 SDK 的 C# 封装）的 P2P 网络接口实现 Unity.Netcode.NetworkTransport。
    ///
    /// 设计要点：
    /// - clientId 映射：服务端把「客户端 SteamId」当作 clientId；客户端收包统一按 ServerClientId(0) 上报
    ///   （主机自己（clientId=0）的本地消息由 NGO 内部路由，不走传输层）
    /// - 连接语义：Steam P2P 没有连接建立/断开回调，约定「收到对方首个数据包」即视为 Connect，
    ///   后续握手（连接请求/审批）由 NGO 自己的消息体系完成
    /// - 消息格式：1 字节类型头 + 负载（NGO 数据 / BYE 告别 / PING / PONG 心跳）
    /// - 断开检测：优雅退出时发送 BYE；主机突然消失由客户端 1 秒心跳 + 5 秒超时兜底
    ///   （Steam 的 OnP2PConnectionFailed 触发太慢，不能依赖）
    /// - 事件上报：NGO 2.x 订阅 OnTransportEvent（事件模式），Update 里泵包后直接 Invoke
    ///
    /// 测试 AppID 使用 480（Spacewar，Steam 官方指定的免费测试 ID），无需注册 Steamworks 开发者账号。
    /// 注意：SteamClient 是进程级单例，Steam 模式不可用 MPPM（两个虚拟玩家共享同一 Steam 账号）测试。
    /// </summary>
    public class SteamTransport : NetworkTransport
    {
        public const uint SteamAppId = 480;

        private const byte MsgNgo = 0;   // NGO 数据
        private const byte MsgBye = 1;   // 优雅断开
        private const byte MsgPing = 2;  // 心跳请求
        private const byte MsgPong = 3;  // 心跳应答

        private const int Channel = 0;            // 所有消息走同一信道
        private const int MaxPacket = 8192;       // NGO 消息层会自己分片（MTU 1300），8KB 足够
        private const float HeartbeatInterval = 1f;
        private const float HeartbeatTimeout = 5f;

        /// <summary>客户端模式：要连接的主机 SteamId（由大厅输入 / Steam 大厅的房主获得）。</summary>
        public SteamId TargetSteamId { get; set; }

        /// <summary>Steam 是否初始化成功（Steam 客户端未运行时为 false）。</summary>
        public bool IsSteamReady => SteamClient.IsValid;

        /// <summary>本机 SteamID64（大厅状态栏显示，供其他玩家直接输入加入）。</summary>
        public ulong LocalSteamId => SteamClient.IsValid ? SteamClient.SteamId.Value : 0;

        private readonly byte[] _recvBuffer = new byte[MaxPacket];
        private bool _started;
        private bool _isServer;
        private readonly HashSet<ulong> _clients = new HashSet<ulong>(); // 服务端：已连接的客户端 SteamId

        // 客户端心跳（主机消失检测）
        private bool _clientConnected;
        private float _nextHeartbeat;
        private float _lastPongTime;
        private bool _disconnectFired;

        public override ulong ServerClientId => 0;

        /// <summary>NGO 在 StartHost/StartClient 时调用（早于 StartServer/StartClient）。首次初始化 Steam。</summary>
        public override void Initialize(NetworkManager networkManager = null)
        {
            TryInitSteam();
        }

        /// <summary>
        /// 确保 Steam 已初始化（幂等）。
        /// NetworkBootstrap 在开房/加入前必须主动调用——NGO 的 Initialize 时机在 StartHost 之后，
        /// 光靠它会在检查 IsSteamReady 时还没初始化，永远报「Steam 未启动」。
        /// </summary>
        public bool EnsureSteamReady()
        {
            TryInitSteam();
            return SteamClient.IsValid;
        }

        private void TryInitSteam()
        {
            if (SteamClient.IsValid)
            {
                return;
            }

            try
            {
                SteamClient.Init(SteamAppId);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamTransport] Steam 初始化异常：{e}");
            }

            if (SteamClient.IsValid)
            {
                Debug.Log($"[SteamTransport] Steam 初始化成功，本机 SteamID={SteamClient.SteamId}（名字：{SteamClient.Name}，在线：{SteamClient.IsLoggedOn}）");
                SteamNetworking.OnP2PSessionRequest += AcceptSession;
            }
            else
            {
                Debug.LogWarning("[SteamTransport] Steam 初始化失败：请确认 Steam 客户端已启动并登录（测试用 AppID 480 Spacewar）。");
            }
        }

        /// <summary>
        /// Play 启动即初始化 Steam（而非等到开房时）：
        /// 好友邀请回调（OnGameLobbyJoinRequested）只在 Steam API 已初始化时才会收到，
        /// 局域网模式的玩家也必须能「接受邀请」自动加入 Steam 房间。
        /// </summary>
        private void Awake()
        {
            TryInitSteam();
        }

        /// <summary>接受对方发起的 P2P 会话请求（Steam P2P 收发数据的前提）。</summary>
        private void AcceptSession(SteamId id)
        {
            SteamNetworking.AcceptP2PSessionWithUser(id);
        }

        public override bool StartServer()
        {
            if (!SteamClient.IsValid)
            {
                return false;
            }
            _started = true;
            _isServer = true;
            _clients.Clear();
            Debug.Log("[SteamTransport] Steam 服务器已就绪（P2P 无需监听端口）");
            return true;
        }

        public override bool StartClient()
        {
            if (!SteamClient.IsValid || TargetSteamId.Value == 0)
            {
                Debug.LogWarning($"[SteamTransport] 无法启动 Steam 客户端连接：SteamReady={SteamClient.IsValid}，目标={TargetSteamId}");
                return false;
            }
            _started = true;
            _isServer = false;
            _clientConnected = false;
            _disconnectFired = false;
            // 心跳从连接尝试阶段就开始：目标 SteamID 填错/主机未开房时 5 秒内报超时（不等 NGO 默认 30 秒）
            _lastPongTime = Time.realtimeSinceStartup;
            _nextHeartbeat = Time.realtimeSinceStartup + 0.5f;
            Debug.Log($"[SteamTransport] Steam 客户端连接目标主机={TargetSteamId}");
            return true;
        }

        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            if (!_started || !SteamClient.IsValid || payload.Count == 0)
            {
                return;
            }

            // 客户端模式下 NGO 用 ServerClientId(0) 表示「发给服务器」；服务端模式下 clientId 即目标 SteamId
            SteamId target = _isServer ? (SteamId)clientId : TargetSteamId;

            byte[] packet = new byte[payload.Count + 1];
            packet[0] = MsgNgo;
            Buffer.BlockCopy(payload.Array, payload.Offset, packet, 1, payload.Count);

            P2PSend mode = networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced
                ? P2PSend.Unreliable
                : P2PSend.Reliable;
            SteamNetworking.SendP2PPacket(target, packet, packet.Length, Channel, mode);
        }

        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            // NGO 2.x 走事件模式（OnTransportEvent），事件在 Update 泵包时直接 Invoke，这里只兜底
            clientId = 0;
            payload = default;
            receiveTime = 0;
            return NetworkEvent.Nothing;
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            if (!_isServer)
            {
                return;
            }
            // 服务端主动断开（踢人 / 主机退出关服）→ 先发告别包让客户端立刻知道，再关闭会话
            SendControlTo((SteamId)clientId, MsgBye);
            SteamNetworking.CloseP2PSessionWithUser((SteamId)clientId);
            _clients.Remove(clientId);
        }

        public override void DisconnectLocalClient()
        {
            if (_isServer)
            {
                return;
            }
            SendControlTo(TargetSteamId, MsgBye);
            SteamNetworking.CloseP2PSessionWithUser(TargetSteamId);
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            return 0; // Demo 不追踪 RTT
        }

        public override void Shutdown()
        {
            _started = false;
            _clients.Clear();
            _clientConnected = false;
        }

        private void OnDestroy()
        {
            SteamNetworking.OnP2PSessionRequest -= AcceptSession;
            // 注意：不调用 SteamClient.Shutdown()——Steam 是进程级单例，Play 会话内可能再次开房/加入；
            // 编辑器退出时 Steam 会自动结束「游戏中」状态。
        }

        private void Update()
        {
            if (!SteamClient.IsValid)
            {
                return;
            }

            // 回调泵常开：大厅阶段（未开联机）也需要处理 Steam 大厅/好友事件
            SteamClient.RunCallbacks();
            if (!_started)
            {
                return;
            }

            PumpPackets();

            if (!_isServer)
            {
                ClientHeartbeatTick();
            }
        }

        /// <summary>把 Steam 收包队列里的包全部取出处理。</summary>
        private void PumpPackets()
        {
            while (SteamNetworking.IsP2PPacketAvailable(Channel))
            {
                uint size = 0;
                SteamId from = default;
                if (!SteamNetworking.ReadP2PPacket(_recvBuffer, ref size, ref from, Channel) || size == 0)
                {
                    continue;
                }
                HandlePacket(from, (int)size);
            }
        }

        private void HandlePacket(SteamId from, int size)
        {
            byte type = _recvBuffer[0];

            if (_isServer)
            {
                HandleServerPacket(from, type, size);
            }
            else
            {
                HandleClientPacket(from, type, size);
            }
        }

        private void HandleServerPacket(SteamId from, byte type, int size)
        {
            ulong clientId = from.Value;

            switch (type)
            {
                case MsgNgo:
                {
                    bool first = !_clients.Contains(clientId);
                    if (first)
                    {
                        // 新客户端首个数据包 → 先 Connect 再 Data（NGO 随后完成连接审批握手）
                        _clients.Add(clientId);
                        Debug.Log($"[SteamTransport] 服务端：客户端已连接 SteamID={from}");
                        InvokeOnTransportEvent(NetworkEvent.Connect, clientId, default, Time.realtimeSinceStartup);
                    }
                    InvokeOnTransportEvent(NetworkEvent.Data, clientId,
                        new ArraySegment<byte>(_recvBuffer, 1, size - 1), Time.realtimeSinceStartup);
                    break;
                }
                case MsgBye:
                    if (_clients.Remove(clientId))
                    {
                        Debug.Log($"[SteamTransport] 服务端：客户端主动断开 SteamID={from}");
                        InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, default, Time.realtimeSinceStartup);
                    }
                    break;
                case MsgPing:
                    SendControlTo(from, MsgPong);
                    break;
                case MsgPong:
                    break; // 服务端暂不追踪客户端存活（Demo 场景客户端掉线由 NGO 侧退出流程覆盖）
            }
        }

        private void HandleClientPacket(SteamId from, byte type, int size)
        {
            switch (type)
            {
                case MsgNgo:
                {
                    if (!_clientConnected)
                    {
                        // 收到服务器首个数据包（连接审批通过）→ 先 Connect 再 Data
                        _clientConnected = true;
                        Debug.Log("[SteamTransport] 客户端：已连接主机");
                        InvokeOnTransportEvent(NetworkEvent.Connect, ServerClientId, default, Time.realtimeSinceStartup);
                    }
                    InvokeOnTransportEvent(NetworkEvent.Data, ServerClientId,
                        new ArraySegment<byte>(_recvBuffer, 1, size - 1), Time.realtimeSinceStartup);
                    break;
                }
                case MsgBye:
                    Debug.Log("[SteamTransport] 客户端：收到主机告别，主机已退出");
                    _clientConnected = false;
                    InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, default, Time.realtimeSinceStartup);
                    break;
                case MsgPong:
                    _lastPongTime = Time.realtimeSinceStartup;
                    break;
                case MsgPing:
                    SendControlTo(from, MsgPong);
                    break;
            }
        }

        /// <summary>客户端心跳：每 1 秒 PING 一次，5 秒无 PONG 判定失败——连接阶段=超时，连接后=主机已消失。</summary>
        private void ClientHeartbeatTick()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= _nextHeartbeat)
            {
                _nextHeartbeat = now + HeartbeatInterval;
                SendControlTo(TargetSteamId, MsgPing);
            }

            if (!_disconnectFired && now - _lastPongTime > HeartbeatTimeout)
            {
                _disconnectFired = true;
                Debug.Log(_clientConnected
                    ? "[SteamTransport] 客户端：心跳超时，主机已消失"
                    : "[SteamTransport] 客户端：连接超时（5 秒未收到主机回应，请检查 SteamID）");
                _clientConnected = false;
                InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, default, now);
            }
        }

        private void SendControlTo(SteamId target, byte type)
        {
            if (!SteamClient.IsValid || target.Value == 0)
            {
                return;
            }
            byte[] packet = { type };
            SteamNetworking.SendP2PPacket(target, packet, 1, Channel,
                type == MsgBye ? P2PSend.Reliable : P2PSend.Unreliable);
        }
    }
}
