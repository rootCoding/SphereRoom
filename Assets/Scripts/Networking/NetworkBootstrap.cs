using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using SphereRoom.Player;

namespace SphereRoom.Networking
{
    /// <summary>联机方式：局域网（Unity Transport 直连）或 Steam P2P（自写传输层）。</summary>
    public enum NetMode
    {
        UTP,
        Steam
    }

    /// <summary>
    /// 网络引导器（由 GameBootstrap 运行时创建）：
    /// - 创建 NetworkManager + UnityTransport，注册玩家网络预制体（Resources/Player）
    /// - 主机权威：所有玩家对象由主机按「加入顺序」在出生点依次生成（绝不重叠）
    /// - 出生点：场景常驻 4 个可编辑标记，运行时按当前联机人数启用前 N 个，其余禁用
    /// 连接流程：StartHost 开房（默认端口 7777）/ StartClient(ip) 加入。
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour
    {
        /// <summary>客户端连接断开（参数：是否主机退出、提示文案）。</summary>
        public event Action<bool, string> ConnectionStopped;

        /// <summary>连接状态变化（大厅状态栏显示）。</summary>
        public event Action<string> Status;

        /// <summary>接受 Steam 好友邀请自动加入时触发（GameBootstrap 借此把大厅 UI 切到 Steam 模式）。</summary>
        public event Action SteamModeRequested;

        /// <summary>当前是否在联机会话中。</summary>
        public bool IsListening => _networkManager != null && _networkManager.IsListening;

        /// <summary>当前 Steam 大厅 ID（Esc 菜单显示与复制用）。</summary>
        public ulong? CurrentLobbyId => _lobbyManager?.CurrentLobbyId;

        /// <summary>
        /// 本次会话使用的端口。
        /// 编辑器里 UnityTransport 退出 Play 后可能不释放端口（已知问题），
        /// 因此端口由大厅随机分配，避免与上次泄漏的端口冲突。
        /// </summary>
        private ushort _port;

        /// <summary>当前联机方式（由大厅「联机方式」按钮切换）。</summary>
        public NetMode Mode { get; set; } = NetMode.UTP;

        private NetworkManager _networkManager;
        private UnityTransport _transport;
        private SteamTransport _steamTransport;
        private SteamLobbyManager _lobbyManager;
        private GameObject _playerPrefab;
        private GameObject _ballPrefab;
        private bool _connectedOnce;   // 本次连接尝试是否成功连接过（区分「超时」与「掉线」）
        private readonly List<PlayerSpawnPoint> _spawnPoints = new List<PlayerSpawnPoint>();
        private int _nextSpawnIndex;

        private void Awake()
        {
            CacheSpawnPoints();
            CreateNetworkManager();
        }

        private void OnDestroy()
        {
            // 退出 Play 时主动关闭网络，释放传输层端口（否则编辑器会一直占着端口，下次运行绑定失败）
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
            }

            if (_networkManager != null)
            {
                _networkManager.OnClientConnectedCallback -= OnClientConnected;
                _networkManager.OnClientStopped -= OnClientStopped;
            }
        }

        /// <summary>创建房间（主机），监听指定端口。</summary>
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
                return;
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

            _port = (ushort)Mathf.Clamp(port, 1024, 65535);
            // 主机监听所有网卡（局域网内其他机器可加入）
            _transport.SetConnectionData("0.0.0.0", _port);
            _networkManager.NetworkConfig.NetworkTransport = _transport;
            _connectedOnce = false;
            _networkManager.StartHost();
        }

        /// <summary>加入指定 IP 与端口的房间（客户端）。</summary>
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

            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = "127.0.0.1";
            }
            _port = (ushort)Mathf.Clamp(port, 1024, 65535);
            _transport.SetConnectionData(ip, _port);
            _networkManager.NetworkConfig.NetworkTransport = _transport;
            _connectedOnce = false;
            _networkManager.StartClient();
        }

        /// <summary>Steam 模式开房：先创建 Steam 大厅（4 人上限），成功后以 Steam 传输层启动主机。</summary>
        private void StartHostSteam()
        {
            if (!_steamTransport.EnsureSteamReady())
            {
                Status?.Invoke("Steam 初始化失败：请先启动 Steam 客户端");
                return;
            }
            Status?.Invoke("正在创建 Steam 大厅…");
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

        /// <summary>Steam 模式加入：输入框内容是大厅 ID → 加入大厅 → 自动取房主 SteamId 连接。</summary>
        private void StartClientSteam(string input)
        {
            if (!_steamTransport.EnsureSteamReady())
            {
                Status?.Invoke("Steam 初始化失败：请先启动 Steam 客户端");
                return;
            }
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
                _steamTransport.TargetSteamId = lobby.Value.Owner.Id;
                _networkManager.NetworkConfig.NetworkTransport = _steamTransport;
                _connectedOnce = false;
                _networkManager.StartClient();
            });
        }

        /// <summary>接受 Steam 好友邀请 → 自动切 Steam 模式并连接邀请人（房主）的房间。</summary>
        private void OnFriendInviteAccepted(ulong inviterSteamId)
        {
            Mode = NetMode.Steam;
            SteamModeRequested?.Invoke();
            _steamTransport.TargetSteamId = inviterSteamId;
            _networkManager.NetworkConfig.NetworkTransport = _steamTransport;
            _connectedOnce = false;
            _networkManager.StartClient();
        }

        /// <summary>房主：打开 Steam 好友邀请面板（Esc 菜单「邀请 Steam 好友」按钮）。</summary>
        public void InviteFriends()
        {
            _lobbyManager.InviteFriends();
        }

        /// <summary>缓存出生点（按层级顺序），初始全部禁用。</summary>
        private void CacheSpawnPoints()
        {
            _spawnPoints.AddRange(UnityEngine.Object.FindObjectsByType<PlayerSpawnPoint>(FindObjectsSortMode.None)
                .OrderBy(p => p.transform.GetSiblingIndex()));
            UpdateSpawnPointVisibility();
        }

        private void CreateNetworkManager()
        {
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

            _networkManager.AddNetworkPrefab(_playerPrefab);
            _networkManager.AddNetworkPrefab(_ballPrefab);
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
                SpawnSharedBall();
                StartCoroutine(BallSpawnLoop());
            };
            _networkManager.OnTransportFailure += () =>
            {
                Debug.LogError("[NetworkBootstrap] 传输层连接失败（5 秒超时）");
                Status?.Invoke("连接超时，请检查主机IP与端口是否正确");
            };
            _networkManager.OnClientConnectedCallback += OnClientConnected;
            _networkManager.OnClientStopped += OnClientStopped;
        }

        /// <summary>服务端：玩家加入（含主机自己）→ 按顺序分配出生点并生成该玩家的对象。</summary>
        private void OnClientConnected(ulong clientId)
        {
            if (!_networkManager.IsServer)
            {
                _connectedOnce = true;
                Debug.Log("[NetworkBootstrap] 已连接服务器，等待玩家生成…");
                Status?.Invoke("已连接，等待玩家生成…");
                return;
            }

            Debug.Log($"[NetworkBootstrap] 玩家加入 clientId={clientId}");
            int spawnIndex = _nextSpawnIndex++;
            SpawnPlayerFor(clientId, spawnIndex);
            UpdateSpawnPointVisibility();
        }

        /// <summary>主机在房间中央生成初始共享物理球。</summary>
        private void SpawnSharedBall()
        {
            SpawnBallAt(new Vector3(0f, 0.5f, 0f));
        }

        /// <summary>加分项：每 15 秒由主机在房间内随机位置生成一个新球（主机权威，自动同步）。</summary>
        private IEnumerator BallSpawnLoop()
        {
            while (_networkManager != null && _networkManager.IsListening)
            {
                yield return new WaitForSeconds(15f);
                if (!_networkManager.IsServer)
                {
                    continue;
                }
                SpawnBallAt(new Vector3(UnityEngine.Random.Range(-8f, 8f), 0.5f, UnityEngine.Random.Range(-8f, 8f)));
            }
        }

        /// <summary>生成共享物理球（主机权威物理，位置/速度自动同步给所有客户端）。</summary>
        private void SpawnBallAt(Vector3 position)
        {
            if (_ballPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] 球体预制体缺失，无法生成共享球。");
                return;
            }

            NetworkObject.InstantiateAndSpawn(_ballPrefab, _networkManager,
                ownerClientId: NetworkManager.ServerClientId,
                position: position,
                rotation: Quaternion.identity);
            Debug.Log($"[NetworkBootstrap] 共享物理球已生成，位置={position}");
        }

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

        /// <summary>按当前联机人数启用前 N 个出生点，其余禁用（出生点本身在游戏中不可见，仅保持层级整洁）。</summary>
        private void UpdateSpawnPointVisibility()
        {
            for (int i = 0; i < _spawnPoints.Count; i++)
            {
                _spawnPoints[i].gameObject.SetActive(i < _nextSpawnIndex);
            }
        }

        /// <summary>客户端连接断开 → 按「是否成功连接过」给出准确提示。</summary>
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
    }
}
