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
    /// 为什么自写而不直接用社区现成传输层？
    /// 社区的 Steam 传输层（Facepunch transport / SteamNetworkingSockets transport）都停留在 NGO 1.x 时代，
    /// 与本项目 NGO 2.13.3 的兼容性无保证，且内置 2020 年的旧版 Steam 库有已知 bug（Steam ID 返回 0）。
    /// 本类直接对着 NGO 2.x 的 NetworkTransport 抽象接口实现，依赖新版 Facepunch.Steamworks 2.5.2。
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
        // ==================== 常量 ====================

        public const uint SteamAppId = 480;   // Spacewar 测试 AppID：Steam 官方供开发测试免费使用

        // 自定义消息类型（1 字节头）：Steam P2P 只传字节流，协议自己定义
        private const byte MsgNgo = 0;   // NGO 数据
        private const byte MsgBye = 1;   // 优雅断开
        private const byte MsgPing = 2;  // 心跳请求
        private const byte MsgPong = 3;  // 心跳应答

        private const int Channel = 0;            // 所有消息走同一信道（Steam P2P 信道 0 即可满足需求）
        private const int MaxPacket = 8192;       // NGO 消息层会自己分片（MTU 1300），8KB 足够
        private const float HeartbeatInterval = 1f;   // 客户端 PING 间隔（秒）
        private const float HeartbeatTimeout = 5f;    // 无 PONG 判定失败（秒）

        // ==================== 对外属性 ====================

        /// <summary>客户端模式：要连接的主机 SteamId（由大厅输入 / Steam 大厅的房主获得）。</summary>
        public SteamId TargetSteamId { get; set; }

        /// <summary>Steam 是否初始化成功（Steam 客户端未运行时为 false）。</summary>
        public bool IsSteamReady => SteamClient.IsValid;

        /// <summary>本机 SteamID64（大厅状态栏显示，供其他玩家直接输入加入）。</summary>
        public ulong LocalSteamId => SteamClient.IsValid ? SteamClient.SteamId.Value : 0;

        // ==================== 内部状态 ====================

        // 收包缓冲区：Steam 读包 API 需要预先分配的数组，重复使用避免每包分配 GC
        private readonly byte[] _recvBuffer = new byte[MaxPacket];
        private bool _started;       // 是否处于会话中（StartServer/StartClient 后为 true，Shutdown 后为 false）
        private bool _isServer;      // 当前角色（服务端/客户端），决定收发包的 clientId 映射方向
        private readonly HashSet<ulong> _clients = new HashSet<ulong>(); // 服务端：已连接的客户端 SteamId（判重连接事件）

        // 客户端心跳（主机消失检测）
        private bool _clientConnected;    // 是否已与主机完成连接（收到过主机数据包）
        private float _nextHeartbeat;     // 下次发 PING 的时间点
        private float _lastPongTime;      // 最近收到 PONG 的时间点
        private bool _disconnectFired;    // 超时断开事件是否已上报（防止连发）

        // ==================== NGO 接口实现 ====================

        /// <summary>NGO 的服务器 clientId 固定为 0（主机自己的本地消息由 NGO 内部路由）。</summary>
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

        /// <summary>初始化 Steam API（仅一次；失败说明 Steam 客户端未启动/未登录）。</summary>
        private void TryInitSteam()
        {
            if (SteamClient.IsValid)
            {
                return;   // 已初始化过（进程级单例），直接跳过
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
                // 订阅会话请求：对方发来 P2P 会话请求时自动接受（不收发数据的前提）
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

        /// <summary>
        /// 服务端启动：P2P 无监听端口概念，只需标记角色并清空客户端列表。
        /// 与局域网传输层的关键差异：没有「绑定端口」——Steam 服务器按 SteamId 寻址。
        /// </summary>
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

        /// <summary>客户端启动：记录目标主机并开启连接阶段心跳（错误目标 5 秒内超时）。</summary>
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

        /// <summary>
        /// 发送 NGO 数据：负载前拼 1 字节类型头（MsgNgo）。
        /// 可靠/不可靠映射：NGO 的 Reliable* 走 Steam Reliable（保证送达且有序），Unreliable* 走 Steam Unreliable。
        /// </summary>
        /// <param name="clientId">NGO 视角的收件人（客户端模式恒为 0 = 服务器）</param>
        /// <param name="payload">NGO 消息负载</param>
        /// <param name="networkDelivery">NGO 投递语义（可靠/不可靠）</param>
        public override void Send(ulong clientId, ArraySegment<byte> payload, NetworkDelivery networkDelivery)
        {
            if (!_started || !SteamClient.IsValid || payload.Count == 0)
            {
                return;
            }

            // 客户端模式下 NGO 用 ServerClientId(0) 表示「发给服务器」；服务端模式下 clientId 即目标 SteamId
            SteamId target = _isServer ? (SteamId)clientId : TargetSteamId;

            // 拼包：1 字节类型头 + 原负载（Buffer.BlockCopy 比逐字节复制快）
            byte[] packet = new byte[payload.Count + 1];
            packet[0] = MsgNgo;
            Buffer.BlockCopy(payload.Array, payload.Offset, packet, 1, payload.Count);

            P2PSend mode = networkDelivery == NetworkDelivery.Unreliable || networkDelivery == NetworkDelivery.UnreliableSequenced
                ? P2PSend.Unreliable
                : P2PSend.Reliable;
            SteamNetworking.SendP2PPacket(target, packet, packet.Length, Channel, mode);
        }

        /// <summary>
        /// 轮询接口（NGO 2.x 走事件模式，此方法仅兜底返回 Nothing）。
        /// 说明：NGO 2.x 优先订阅 OnTransportEvent（事件模式），事件在 Update 泵包时直接 Invoke；
        /// 本方法保留抽象接口实现，但实际不会被依赖。
        /// </summary>
        public override NetworkEvent PollEvent(out ulong clientId, out ArraySegment<byte> payload, out float receiveTime)
        {
            // NGO 2.x 走事件模式（OnTransportEvent），事件在 Update 泵包时直接 Invoke，这里只兜底
            clientId = 0;
            payload = default;
            receiveTime = 0;
            return NetworkEvent.Nothing;
        }

        /// <summary>
        /// 服务端主动断开某客户端（踢人 / 主机退出关服）：先发 BYE 告别包再关闭会话。
        /// NGO 关服时会为每个已连接客户端调用本方法——BYE 让客户端「秒断」而不是等 5 秒心跳超时。
        /// </summary>
        /// <param name="clientId">要断开的客户端 ID（= 客户端 SteamId）</param>
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

        /// <summary>
        /// 客户端主动断开：发 BYE 告知服务器（服务器据此清理该玩家对象）。
        /// NGO 客户端关会话时会调用本方法——服务器收 BYE 后立即销毁该玩家对象，不留僵尸。
        /// </summary>
        public override void DisconnectLocalClient()
        {
            if (_isServer)
            {
                return;   // 主机的本地客户端退出由 NGO 自己处理，不需要发网络告别
            }
            SendControlTo(TargetSteamId, MsgBye);
            SteamNetworking.CloseP2PSessionWithUser(TargetSteamId);
        }

        /// <summary>RTT 查询：Demo 不追踪（返回 0，NGO 不依赖该值做同步）。</summary>
        public override ulong GetCurrentRtt(ulong clientId)
        {
            return 0; // Demo 不追踪 RTT
        }

        /// <summary>会话结束：复位状态（Steam 本身保持初始化，Play 会话内可再次开房/加入）。</summary>
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

        // ==================== 每帧驱动 ====================

        /// <summary>每帧：泵 Steam 回调 → 取空收包队列 → 客户端心跳。</summary>
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

            // 把 Steam 收包队列清空（每个包转成 NGO 事件上报）
            PumpPackets();

            // 客户端独有：心跳收发与超时判定
            if (!_isServer)
            {
                ClientHeartbeatTick();
            }
        }

        // ==================== 收包 ====================

        /// <summary>
        /// 把 Steam 收包队列里的包全部取出处理（每次 ReadP2PPacket 取一个完整包）。
        /// Steam P2P 保持消息边界（一包即一条消息），无需自己拼包/分包。
        /// </summary>
        private void PumpPackets()
        {
            while (SteamNetworking.IsP2PPacketAvailable(Channel))
            {
                uint size = 0;
                SteamId from = default;
                // ref 参数：size 传回实际包长，from 传回发送者 SteamId；
                // 读失败或空包跳过（异常包不影响后续处理）
                if (!SteamNetworking.ReadP2PPacket(_recvBuffer, ref size, ref from, Channel) || size == 0)
                {
                    continue;
                }
                HandlePacket(from, (int)size);
            }
        }

        /// <summary>
        /// 按角色分派包处理（服务端/客户端的 clientId 映射方向相反）。
        /// 服务端：from=客户端 SteamId → clientId 用 from.Value；
        /// 客户端：任何包都来自服务器 → clientId 恒为 ServerClientId(0)。
        /// </summary>
        /// <param name="from">发送者 SteamId</param>
        /// <param name="size">包总长（含 1 字节类型头）</param>
        private void HandlePacket(SteamId from, int size)
        {
            byte type = _recvBuffer[0];   // 首字节 = 消息类型

            if (_isServer)
            {
                HandleServerPacket(from, type, size);
            }
            else
            {
                HandleClientPacket(from, type, size);
            }
        }

        /// <summary>服务端收包：首包触发 Connect；BYE 触发 Disconnect；PING 回 PONG。</summary>
        private void HandleServerPacket(SteamId from, byte type, int size)
        {
            ulong clientId = from.Value;   // 服务端视角：clientId = 客户端 SteamId

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
                    // 数据负载去掉 1 字节类型头，原样交给 NGO
                    InvokeOnTransportEvent(NetworkEvent.Data, clientId,
                        new ArraySegment<byte>(_recvBuffer, 1, size - 1), Time.realtimeSinceStartup);
                    break;
                }
                case MsgBye:
                    // 已在列表里才上报 Disconnect（重复 BYE 只处理一次）
                    if (_clients.Remove(clientId))
                    {
                        Debug.Log($"[SteamTransport] 服务端：客户端主动断开 SteamID={from}");
                        InvokeOnTransportEvent(NetworkEvent.Disconnect, clientId, default, Time.realtimeSinceStartup);
                    }
                    break;
                case MsgPing:
                    SendControlTo(from, MsgPong);   // 回心跳（无论该客户端是否已连接，保证连接阶段心跳可用）
                    break;
                case MsgPong:
                    break; // 服务端暂不追踪客户端存活（Demo 场景客户端掉线由 NGO 侧退出流程覆盖）
            }
        }

        /// <summary>
        /// 客户端收包：首包触发 Connect（上报 clientId=0）；BYE/PONG 按语义处理。
        /// 客户端的「连接」由服务器首个数据包（连接审批通过消息）触发——在此之前 NGO 处于连接中状态。
        /// </summary>
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
                    // 客户端视角：所有服务器消息统一按 ServerClientId(0) 上报
                    InvokeOnTransportEvent(NetworkEvent.Data, ServerClientId,
                        new ArraySegment<byte>(_recvBuffer, 1, size - 1), Time.realtimeSinceStartup);
                    break;
                }
                case MsgBye:
                    // 主机优雅退出（NGO 关服时给每个客户端发 BYE）→ 立即上报断开
                    Debug.Log("[SteamTransport] 客户端：收到主机告别，主机已退出");
                    _clientConnected = false;
                    InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, default, Time.realtimeSinceStartup);
                    break;
                case MsgPong:
                    _lastPongTime = Time.realtimeSinceStartup;   // 刷新心跳基准，超时判定顺延
                    break;
                case MsgPing:
                    SendControlTo(from, MsgPong);
                    break;
            }
        }

        // ==================== 心跳 ====================

        /// <summary>
        /// 客户端心跳：每 1 秒 PING 一次，5 秒无 PONG 判定失败——
        /// 连接阶段超时 = 目标错误/主机未开房；连接后超时 = 主机已消失（崩溃/断网等非优雅退出）。
        /// </summary>
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
                _disconnectFired = true;   // 只上报一次，NGO 随后会调 Shutdown 结束会话
                Debug.Log(_clientConnected
                    ? "[SteamTransport] 客户端：心跳超时，主机已消失"
                    : "[SteamTransport] 客户端：连接超时（5 秒未收到主机回应，请检查 SteamID）");
                _clientConnected = false;
                InvokeOnTransportEvent(NetworkEvent.Disconnect, ServerClientId, default, now);
            }
        }

        /// <summary>
        /// 发控制报文（BYE 走可靠通道保证送达；心跳类走不可靠通道省流量）。
        /// 心跳包丢了没关系（下一秒还会发）；BYE 必须可靠——它是优雅断开的唯一通知。
        /// </summary>
        /// <param name="target">收件人 SteamId</param>
        /// <param name="type">控制报文类型（BYE/PING/PONG）</param>
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

    // ==================== 设计笔记 ====================
    // 为什么能自写一个网络传输层？
    // NGO 2.x 的传输层接口（NetworkTransport 抽象类）非常小：Start/Stop、Send、事件上报、Shutdown。
    // 真正复杂的是「可靠的连接语义」——Steam P2P 不提供连接状态，我们补了三件套：
    // 1) 首包即 Connect（NGO 自己的握手消息负责后续审批）
    // 2) BYE 报文实现优雅断开（双方主动退出都能立刻通知对方）
    // 3) PING/PONG 心跳兜底异常断开（崩溃/断网，5 秒判定）
    //
    // clientId 映射的巧思：
    // NGO 把「客户端 ID」当不透明 ulong 使用——我们直接把客户端 SteamId 当 clientId（服务端视角），
    // 客户端视角则把一切服务器消息上报为 ServerClientId(0)。两端映射方向相反但互不冲突，
    // 主机自己（clientId=0）的消息由 NGO 内部路由，根本不会走到这个传输层。
    //
    // 已知限制（如实说明）：
    // - 服务端不追踪客户端心跳（Demo 无踢人需求；客户端掉线靠 BYE/退出流程清理）
    // - RTT 恒返回 0（不参与同步，仅影响网络统计面板）
    // - Steam 模式无法用 MPPM 测试（进程级单例共享同一 Steam 账号），需真实双账号
}

