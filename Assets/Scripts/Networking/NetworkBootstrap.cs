using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using SphereRoom.Player;

namespace SphereRoom.Networking
{
    /// <summary>联机方式：局域网（Unity Transport 直连）或 Steam P2P（自写传输层）。</summary>
    public enum NetMode
    {
        UTP,    // 局域网直连（Unity Transport，需 IP + 端口）
        Steam   // Steam P2P（自写 SteamTransport，需大厅 ID）
    }

    /// <summary>
    /// 【模块】网络引导器（由 GameBootstrap 运行时创建）：
    /// - 创建 NetworkManager + 双传输层（UnityTransport 局域网 / SteamTransport 自写 P2P），
    ///   注册玩家与物理球网络预制体（Resources/Player、Resources/Ball）
    /// - 主机权威：所有玩家对象由主机按「加入顺序」在出生点依次生成（绝不重叠）
    /// - 出生点：场景常驻 4 个可编辑标记，运行时按当前联机人数启用前 N 个，其余禁用
    ///
    /// 连接流程：
    ///   局域网：StartHost 开房（固定端口 12305）/ StartClient(ip, port) 加入
    ///   Steam：开房 = 创建 Steam 大厅 → StartHost；加入 = 按大厅 ID 入厅 → 取房主 SteamId → StartClient
    ///
    /// 为什么运行时创建 NetworkManager 而不是场景里放一个？
    /// 题目允许 AI 工具协作，整个项目采用「场景最小化」策略——场景里只有房间灰盒和出生点标记，
    /// 玩家/HUD/NetworkManager 全部运行时生成，避免序列化引用在多人协作/工具生成时互相打架。
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour
    {
        // ==================== 对外事件 ====================

        /// <summary>客户端连接断开（参数：是否主机退出、提示文案）。</summary>
        public event Action<bool, string> ConnectionStopped;

        /// <summary>连接状态变化（大厅状态栏显示）。</summary>
        public event Action<string> Status;

        /// <summary>接受 Steam 好友邀请自动加入时触发（GameBootstrap 借此把大厅 UI 切到 Steam 模式）。</summary>
        public event Action SteamModeRequested;

        // ==================== 对外属性 ====================

        /// <summary>当前是否在联机会话中。</summary>
        public bool IsListening => _networkManager != null && _networkManager.IsListening;

        /// <summary>当前 Steam 大厅 ID（Esc 菜单显示与复制用）。</summary>
        public ulong? CurrentLobbyId => _lobbyManager?.CurrentLobbyId;

        /// <summary>
        /// 本次会话使用的端口（局域网模式，固定 12305）。
        /// 历史备注：编辑器里 UnityTransport 退出 Play 后可能不释放端口（已知问题），
        /// 若提示端口被占用，重启编辑器即可释放；端口值统一由大厅输入（默认 12305）。
        /// </summary>
        private ushort _port;

        /// <summary>当前联机方式（由大厅「联机方式」按钮切换）。</summary>
        public NetMode Mode { get; set; } = NetMode.UTP;

        // ==================== 网络组件（全部运行时创建/加载，场景中不放置） ====================

        private NetworkManager _networkManager;      // NGO 核心单例（本项目的联机总入口）
        private UnityTransport _transport;           // 局域网传输层（Unity 官方）
        private SteamTransport _steamTransport;      // Steam 传输层（自写，见 SteamTransport.cs）
        private SteamLobbyManager _lobbyManager;     // Steam 大厅管理（创建/加入/邀请）
        private GameObject _playerPrefab;            // Resources/Player（网络预制体）
        private GameObject _ballPrefab;              // Resources/Ball（网络预制体）
        private bool _connectedOnce;                 // 本次连接尝试是否成功连接过（区分「超时」与「掉线」）
        private readonly List<PlayerSpawnPoint> _spawnPoints = new List<PlayerSpawnPoint>();
        private int _nextSpawnIndex;                 // 下一位玩家的出生点序号（按加入顺序递增）

        // ==================== 生命周期 ====================

        /// <summary>启动顺序：先缓存出生点（生成玩家时要用），再建网络核心。</summary>
        private void Awake()
        {
            CacheSpawnPoints();
            CreateNetworkManager();
        }

        /// <summary>销毁清理：关闭会话释放端口 + 退订 NGO 回调。</summary>
        private void OnDestroy()
        {
            // 退出 Play 时主动关闭网络，释放传输层端口（否则编辑器会一直占着端口，下次运行绑定失败）
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }

            // 清理 NGO 回调订阅（对象销毁后回调不能再触发）
            if (_networkManager != null)
            {
                _networkManager.OnClientConnectedCallback -= OnClientConnected;
                _networkManager.OnClientStopped -= OnClientStopped;
            }
        }

        // ==================== 开房 / 加入（大厅按钮入口） ====================

        /// <summary>
        /// 创建房间（主机）。局域网模式监听指定端口；Steam 模式走大厅流程（端口参数忽略）。
        /// 前置检查：NetworkManager 存在、未在会话中、玩家预制体就绪。
        /// </summary>
        /// <param name="port">局域网监听端口（大厅输入）</param>
        public void StartHost(int port)
        {
            Debug.Log($"[NetworkBootstrap] 点击创建房间 port={port}");
            if (_networkManager == null)
            {
                Status?.Invoke("NetworkManager 未创建");
                return;
            }
            if (_networkManager.IsListening)
            {
                return;   // 已在会话中，忽略重复点击
            }
            if (_playerPrefab == null)
            {
                Status?.Invoke("玩家预制体缺失：请先运行 Tools/球体房间/生成玩家网络预制体");
                return;
            }
            if (Mode == NetMode.Steam)
            {
                StartHostSteam();
                return;
            }

            // 局域网模式三步走：绑定端口（钳到合法范围）→ 切 UTP 传输层 → 启动主机
            // 步骤 1：端口钳制（1024~65535，避开系统保留端口）
            _port = (ushort)Mathf.Clamp(port, 1024, 65535);
            // 步骤 2：主机监听所有网卡（局域网内其他机器可加入）；0.0.0.0 = 绑定本机全部网卡
            _transport.SetConnectionData("0.0.0.0", _port);
            // 步骤 3：切传输层并启动（_connectedOnce 复位，供断线判定用）
            _networkManager.NetworkConfig.NetworkTransport = _transport;
            _connectedOnce = false;
            _networkManager.StartHost();
        }

        /// <summary>
        /// 加入房间（客户端）。局域网模式按 IP+端口；Steam 模式第一参数是大厅 ID。
        /// </summary>
        /// <param name="ip">局域网：主机 IP；Steam：大厅 ID 文本</param>
        /// <param name="port">局域网端口（Steam 模式忽略）</param>
        public void StartClient(string ip, int port)
        {
            Debug.Log($"[NetworkBootstrap] 点击加入房间 ip={ip} port={port}");
            if (_networkManager == null)
            {
                Status?.Invoke("NetworkManager 未创建");
                return;
            }
            if (_networkManager.IsListening)
            {
                return;
            }
            if (Mode == NetMode.Steam)
            {
                StartClientSteam(ip);
                return;
            }

            // 局域网模式三步走：空 IP 回退本机（单机自测）→ 设连接地址 → 启动客户端
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = "127.0.0.1";
            }
            // 端口钳制 + 传输层连接地址（客户端只连目标主机，不绑定本机端口）
            _port = (ushort)Mathf.Clamp(port, 1024, 65535);
            _transport.SetConnectionData(ip, _port);
            _networkManager.NetworkConfig.NetworkTransport = _transport;
            _connectedOnce = false;
            _networkManager.StartClient();
        }

        // ==================== Steam 模式的开房/加入 ====================

        /// <summary>
        /// Steam 模式开房：先创建 Steam 大厅（4 人上限）——大厅创建是异步的（Steam 服务器往返），
        /// 成功后切 Steam 传输层并启动主机。失败则在状态栏提示原因。
        /// </summary>
        private void StartHostSteam()
        {
            if (!_steamTransport.EnsureSteamReady())
            {
                Status?.Invoke("Steam 初始化失败：请先启动 Steam 客户端");
                return;
            }
            Status?.Invoke("正在创建 Steam 大厅…");
            // 异步回调里启动主机（回调运行在 Unity 主线程，可以安全操作 NetworkManager）
            _lobbyManager.CreateLobby(lobby =>
            {
                if (!lobby.HasValue)
                {
                    Status?.Invoke("Steam 大厅创建失败，请重试");
                    return;
                }
                _networkManager.NetworkConfig.NetworkTransport = _steamTransport;
                _connectedOnce = false;
                _networkManager.StartHost();
            });
        }

        /// <summary>
        /// Steam 模式加入：输入框内容是大厅 ID → 加入大厅 → 自动取房主 SteamId 连接（玩家不用知道对方 SteamID）。
        /// </summary>
        /// <param name="input">大厅 ID 文本（19 位数字）</param>
        private void StartClientSteam(string input)
        {
            if (!_steamTransport.EnsureSteamReady())
            {
                Status?.Invoke("Steam 初始化失败：请先启动 Steam 客户端");
                return;
            }
            // 大厅 ID 是 19 位数字（ulong），格式校验先行，避免无效请求
            if (!ulong.TryParse(input, out ulong lobbyId) || lobbyId == 0)
            {
                Status?.Invoke("大厅 ID 格式不正确");
                return;
            }
            Status?.Invoke("正在加入 Steam 大厅…");
            _lobbyManager.JoinLobby(lobbyId, lobby =>
            {
                if (!lobby.HasValue)
                {
                    Status?.Invoke("未找到大厅，请检查大厅 ID");
                    return;
                }
                // 大厅的 Owner（房主）就是连接目标
                _steamTransport.TargetSteamId = lobby.Value.Owner.Id;
                _networkManager.NetworkConfig.NetworkTransport = _steamTransport;
                _connectedOnce = false;
                _networkManager.StartClient();
            });
        }

        /// <summary>接受 Steam 好友邀请 → 自动切 Steam 模式并连接邀请人（房主）的房间（全程无需玩家输入）。</summary>
        /// <param name="inviterSteamId">邀请人（房主）的 SteamId</param>
        private void OnFriendInviteAccepted(ulong inviterSteamId)
        {
            // 三个动作：切模式（供大厅 UI 刷新）→ 设连接目标 → 启动客户端
            Mode = NetMode.Steam;
            SteamModeRequested?.Invoke();
            _steamTransport.TargetSteamId = inviterSteamId;
            _networkManager.NetworkConfig.NetworkTransport = _steamTransport;
            _connectedOnce = false;
            _networkManager.StartClient();
        }

        // ==================== Steam 邀请对外接口（Esc 菜单 / 好友列表调用） ====================

        /// <summary>房主：尝试打开 Steam 好友邀请面板（Esc 菜单「邀请 Steam 好友」按钮）。Overlay 不可用时上层回退好友列表。</summary>
        /// <returns>邀请面板打开结果</returns>
        public InviteResult InviteFriends()
        {
            return _lobbyManager.TryOpenInviteOverlay();
        }

        /// <summary>直接邀请指定 Steam 好友（游戏内好友列表路径）。返回 null=成功，否则错误文案。</summary>
        /// <param name="steamId">好友 SteamID64</param>
        public string InviteFriend(ulong steamId)
        {
            return _lobbyManager.InviteFriend(steamId);
        }

        /// <summary>在线 Steam 好友列表（游戏内好友列表路径）。</summary>
        public List<(string Name, ulong SteamId)> GetOnlineFriends()
        {
            return _lobbyManager.GetOnlineFriends();
        }

        // ==================== 出生点 ====================

        /// <summary>缓存出生点（按层级顺序），初始全部禁用。</summary>
        private void CacheSpawnPoints()
        {
            // 按 Hierarchy 中的兄弟序号排序 = 场景里 SpawnPoint_1..N 的摆放顺序
            _spawnPoints.AddRange(UnityEngine.Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None)
                .OrderBy(p => p.transform.GetSiblingIndex()));
            UpdateSpawnPointVisibility();
        }

        /// <summary>按当前联机人数启用前 N 个出生点，其余禁用（出生点本身在游戏中不可见，仅保持层级整洁）。</summary>
        private void UpdateSpawnPointVisibility()
        {
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                _spawnPoints[i].gameObject.SetActive(i < _nextSpawnIndex);
            }
        }

        // ==================== NetworkManager 创建与 NGO 回调 ====================

        /// <summary>
        /// 创建 NetworkManager 与两种传输层，注册预制体并接线 NGO 回调（运行时创建的三件套配置）。
        /// </summary>
        private void CreateNetworkManager()
        {
            // 预制体从 Resources 加载（Editor 工具生成，路径固定）
            _playerPrefab = Resources.Load<GameObject>("Player");
            if (_playerPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] 未找到 Resources/Player.prefab，请先运行 Tools/球体房间/生成玩家网络预制体。");
            }

            _ballPrefab = Resources.Load<GameObject>("Ball");
            if (_ballPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] 未找到 Resources/Ball.prefab，请先运行 Tools/球体房间/生成物理球预制体。");
            }

            // NGO 要求 NetworkManager 位于场景根（不能嵌套在其他物体下）
            GameObject networkGo = new GameObject("NetworkManager");
            networkGo.transform.SetParent(null);

            // 运行时创建必须显式赋配置与传输层（Inspector 添加时由序列化自动生成）
            _networkManager = networkGo.AddComponent<NetworkManager>();
            _networkManager.NetworkConfig = new NetworkConfig();
            _transport = networkGo.AddComponent<UnityTransport>();
            _steamTransport = networkGo.AddComponent<SteamTransport>();
            _lobbyManager = networkGo.AddComponent<SteamLobbyManager>();
            _lobbyManager.Initialize(_steamTransport);
            // 大厅状态与邀请回调转发到本组件
            _lobbyManager.Status += message => Status?.Invoke(message);
            _lobbyManager.FriendInviteAccepted += OnFriendInviteAccepted;
            // 两种传输层常驻同一 NetworkManager，按 Mode 在 StartHost/StartClient 前切换
            _networkManager.NetworkConfig.NetworkTransport = _transport;

            // 加入房间超时控制：5 秒（1 秒 × 5 次重试）连不上即失败。
            // 默认 60 次重试要等 60 秒，用户会以为卡死。
            _transport.ConnectTimeoutMS = 1000;
            _transport.MaxConnectAttempts = 5;
            // 掉线检测：主机突然消失时默认要等 30 秒（心跳超时），缩短到 5 秒
            _transport.DisconnectTimeoutMS = 5000;

            // 注册网络预制体（NGO 按 GlobalObjectIdHash 识别，注册后才能网络生成）
            _networkManager.AddNetworkPrefab(_playerPrefab);
            _networkManager.AddNetworkPrefab(_ballPrefab);
            // 服务器启动回调三件事：
            // 1) 状态栏显示房间信息（两种模式文案不同）
            // 2) 生成初始球
            // 3) 开启 15 秒生成循环（都是主机权威，只在本回调里启动一次）
            _networkManager.OnServerStarted += () =>
            {
                if (Mode == NetMode.Steam)
                {
                    Debug.Log($"[NetworkBootstrap] Steam 服务器已启动，大厅 ID={_lobbyManager.CurrentLobbyId}");
                    Status?.Invoke($"房间已创建（Steam 大厅 ID：{_lobbyManager.CurrentLobbyId}）");
                }
                else
                {
                    Debug.Log($"[NetworkBootstrap] 服务器已启动，端口={_port}");
                    Status?.Invoke($"房间已创建（端口 {_port}）");
                }
                // 初始球 + 定时生成循环（都是主机权威，只在本回调里启动一次）
                SpawnSharedBall();
                StartCoroutine(BallSpawnLoop());
            };
            // 传输层连接失败（UTP 5 秒重试耗尽）
            _networkManager.OnTransportFailure += () =>
            {
                Debug.LogError("[NetworkBootstrap] 传输层连接失败（5 秒超时）");
                Status?.Invoke("连接超时，请检查主机IP与端口是否正确");
            };
            _networkManager.OnClientConnectedCallback += OnClientConnected;
            _networkManager.OnClientStopped += OnClientStopped;
        }

        /// <summary>
        /// 玩家加入（含主机自己）：服务端按顺序分配出生点并生成该玩家的对象；客户端只记状态。
        /// </summary>
        /// <param name="clientId">加入的客户端 ID（主机自己为 0）</param>
        private void OnClientConnected(ulong clientId)
        {
            if (!_networkManager.IsServer)
            {
                // 客户端视角：连接成功（等待服务器生成自己的玩家对象）
                _connectedOnce = true;
                Debug.Log("[NetworkBootstrap] 已连接服务器，等待玩家生成…");
                Status?.Invoke("已连接，等待玩家生成…");
                return;
            }

            // 服务端视角：按加入顺序取下一个出生点生成玩家（isPlayerObject=true 由 NGO 管理生命周期）
            Debug.Log($"[NetworkBootstrap] 玩家加入 clientId={clientId}");
            int spawnIndex = _nextSpawnIndex++;
            SpawnPlayerFor(clientId, spawnIndex);
            UpdateSpawnPointVisibility();
        }

        /// <summary>
        /// 客户端连接断开 → 按「是否成功连接过」给出准确提示（超时 vs 主机退出）。
        /// </summary>
        /// <param name="byHost">NGO 上报的是否主机退出（不可靠，仅参考）</param>
        private void OnClientStopped(bool byHost)
        {
            // 本机主动退出（点「退出游戏」关闭会话时 IsServer 已为 false、ShutdownInProgress 为 true）
            // 不显示任何提示——主机不需要看到自己的断开对话框
            if (_networkManager == null || _networkManager.IsServer || _networkManager.ShutdownInProgress)
            {
                return;
            }

            if (!_connectedOnce)
            {
                // 从未连接成功（重试 5 秒耗尽）= 连接超时 / 房间不存在
                ConnectionStopped?.Invoke(false, Mode == NetMode.Steam
                    ? "连接超时，请检查大厅 ID 是否正确"
                    : "连接超时，请检查主机IP与端口是否正确");
            }
            else
            {
                // 连上后断开：Demo 中客户端掉线的唯一场景是主机退出
                //（byHost 标志在主机彻底关闭时并不可靠，按连接史判定）
                ConnectionStopped?.Invoke(true, "主机已断开连接");
            }
        }

        // ==================== 网络对象生成（主机权威） ====================

        /// <summary>
        /// 在指定出生点生成指定客户端的玩家对象（isPlayerObject=true 断线时由 NGO 自动清理）。
        /// </summary>
        /// <param name="clientId">玩家归属的客户端 ID</param>
        /// <param name="spawnIndex">出生点序号（0 起）</param>
        private void SpawnPlayerFor(ulong clientId, int spawnIndex)
        {
            if (_playerPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] 玩家预制体为 null，无法生成");
                return;
            }

            // 出生点不够时回到 1 号（题目限制 1-4 人，正常不会发生）
            PlayerSpawnPoint point = spawnIndex < _spawnPoints.Count
                ? _spawnPoints[spawnIndex]
                : _spawnPoints[0];
            Debug.Log($"[NetworkBootstrap] 在出生点 {spawnIndex + 1} 生成玩家 clientId={clientId}，位置={point.transform.position}");

            // 网络生成玩家对象：
            // - ownerClientId = 该玩家：他的客户端拥有 NetworkTransform 权威（本地操控本地计算）
            // - isPlayerObject = true：断线时 NGO 自动销毁该对象（不用手动清理）
            NetworkObject spawned = NetworkObject.InstantiateAndSpawn(_playerPrefab, _networkManager,
                ownerClientId: clientId,
                isPlayerObject: true,
                position: point.transform.position,
                rotation: point.transform.rotation);

            if (spawned == null)
            {
                Debug.LogError("[NetworkBootstrap] InstantiateAndSpawn 返回 null，生成失败（查看上方 NGO 错误）");
            }
            else
            {
                Debug.Log($"[NetworkBootstrap] 玩家对象已生成：{spawned.name}，实际位置={spawned.transform.position}");
            }
        }

        /// <summary>主机在房间中央生成初始共享物理球（开局就有一颗球可推）。</summary>
        private void SpawnSharedBall()
        {
            SpawnBallAt(new Vector3(0f, 0.5f, 0f));
        }

        /// <summary>
        /// 加分项：每 15 秒由主机在房间内随机位置生成一个新球（主机权威，自动同步）。
        /// 场上球数达到上限 10 个后停止生成（场上球数 = NetworkRigidbody 数量，只有球挂了这个组件）。
        /// </summary>
        private IEnumerator BallSpawnLoop()
        {
            const int MaxBalls = 10;
            // 循环条件：只要会话还在（主机或客户端）就继续等待；
            // 客户端虽然不生成球，但协程要活着——主机中途退出会重开房间，客户端无需重启协程
            while (_networkManager != null && _networkManager.IsListening)
            {
                // 先等 15 秒再检查（开局已有初始球，不需要立刻再生成）
                yield return new WaitForSeconds(15f);
                if (!_networkManager.IsServer)
                {
                    continue;   // 只有主机生成球（主机权威），客户端等待同步即可
                }

                // 数场上球数：所有网络生成的球都有 NetworkRigidbody，玩家（CharacterController）没有
                int ballCount = UnityEngine.Object.FindObjectsByType<NetworkRigidbody>(FindObjectsSortMode.None).Length;
                if (ballCount >= MaxBalls)
                {
                    Debug.Log($"[NetworkBootstrap] 场上已有 {ballCount} 个球（上限 {MaxBalls}），不再生成新球");
                    continue;
                }
                // 房间 20×20，生成范围 ±8 保证球落在房间内（球半径 0.5，贴地高度 0.5）
                SpawnBallAt(new Vector3(UnityEngine.Random.Range(-8f, 8f), 0.5f, UnityEngine.Random.Range(-8f, 8f)));
            }
        }

        /// <summary>
        /// 生成共享物理球（主机权威物理，NetworkRigidbody 自动把位置/速度同步给所有客户端）。
        /// </summary>
        /// <param name="position">生成位置（世界坐标）</param>
        private void SpawnBallAt(Vector3 position)
        {
            if (_ballPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] 球体预制体缺失，无法生成共享球。");
                return;
            }

            // ownerClientId = ServerClientId：球的归属是服务器 = 只有主机模拟它的物理
            NetworkObject.InstantiateAndSpawn(_ballPrefab, _networkManager,
                ownerClientId: NetworkManager.ServerClientId,
                position: position,
                rotation: Quaternion.identity);
            Debug.Log($"[NetworkBootstrap] 共享物理球已生成，位置={position}");
        }
    }

    // ==================== 设计笔记 ====================
    // 双传输层并存的设计：
    // UnityTransport 与 SteamTransport 常驻同一 NetworkManager，开房/加入前按 Mode 二选一赋给
    // NetworkConfig.NetworkTransport。NGO 每次 Start 都会对「当前传输层」重新 Initialize/订阅事件，
    // 所以切换是安全的（上一次会话的订阅已在 Shutdown 时退订）。
    //
    // 主机权威的生成时序：
    // OnServerStarted → 初始球 + 15 秒循环（球）；OnClientConnected → 出生点分配 + 玩家生成。
    // 玩家生成在「连接回调」里做，天然保证按加入顺序分配出生点、绝不重叠。
    //
    // 断线提示的三层判定（OnClientStopped）：
    // 1) 本机主动退出（ShutdownInProgress）→ 静默
    // 2) 从未连接成功 → 「连接超时」提示
    // 3) 连接后断开 → 判定主机退出（Demo 中客户端掉线的唯一场景），弹「主机已断开连接」对话框
}

