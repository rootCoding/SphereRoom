using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using SphereRoom.Player;

namespace SphereRoom.Networking
{
    /// <summary>
    /// 网络引导器（由 GameBootstrap 运行时创建）：
    /// - 创建 NetworkManager + UnityTransport，注册玩家网络预制体（Resources/Player）
    /// - 主机权威：所有玩家对象由主机按「加入顺序」在出生点依次生成（绝不重叠）
    /// - 出生点：场景常驻 4 个可编辑标记，运行时按当前联机人数启用前 N 个，其余禁用
    /// 连接流程：StartHost 开房（默认端口 7777）/ StartClient(ip) 加入。
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour
    {
        /// <summary>客户端连接断开（参数：给用户看的提示文案）。</summary>
        public event Action<string> ConnectionStopped;

        /// <summary>连接状态变化（大厅状态栏显示）。</summary>
        public event Action<string> Status;

        /// <summary>
        /// 本次会话使用的端口。
        /// 编辑器里 UnityTransport 退出 Play 后可能不释放端口（已知问题），
        /// 因此端口由大厅随机分配，避免与上次泄漏的端口冲突。
        /// </summary>
        private ushort _port;

        private NetworkManager _networkManager;
        private UnityTransport _transport;
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
            _port = (ushort)Mathf.Clamp(port, 1024, 65535);
            _connectedOnce = false;
            // 主机监听所有网卡（局域网内其他机器可加入）
            _transport.SetConnectionData("0.0.0.0", _port);
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
            if (string.IsNullOrWhiteSpace(ip))
            {
                ip = "127.0.0.1";
            }
            _port = (ushort)Mathf.Clamp(port, 1024, 65535);
            _connectedOnce = false;
            _transport.SetConnectionData(ip, _port);
            _networkManager.StartClient();
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
            _networkManager.NetworkConfig.NetworkTransport = _transport;

            // 加入房间超时控制：5 秒（1 秒 × 5 次重试）连不上即失败。
            // 默认 60 次重试要等 60 秒，用户会以为卡死。
            _transport.ConnectTimeoutMS = 1000;
            _transport.MaxConnectAttempts = 5;

            _networkManager.AddNetworkPrefab(_playerPrefab);
            _networkManager.AddNetworkPrefab(_ballPrefab);
            _networkManager.OnServerStarted += () =>
            {
                Debug.Log($"[NetworkBootstrap] 服务器已启动，端口={_port}");
                Status?.Invoke($"房间已创建（端口 {_port}）");
                SpawnSharedBall();
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

        /// <summary>主机在房间中央生成共享物理球（主机权威物理，位置/速度自动同步给所有客户端）。</summary>
        private void SpawnSharedBall()
        {
            if (_ballPrefab == null)
            {
                Debug.LogError("[NetworkBootstrap] 球体预制体缺失，无法生成共享球。");
                return;
            }

            NetworkObject.InstantiateAndSpawn(_ballPrefab, _networkManager,
                ownerClientId: NetworkManager.ServerClientId,
                position: new Vector3(0f, 0.5f, 0f),
                rotation: Quaternion.identity);
            Debug.Log("[NetworkBootstrap] 共享物理球已生成（房间中央）");
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
            if (_networkManager == null || _networkManager.IsServer)
            {
                return;
            }

            string message;
            if (!_connectedOnce)
            {
                // 从未连接成功（重试 5 秒耗尽）= 连接超时 / 房间不存在
                message = "连接超时，请检查主机IP与端口是否正确";
            }
            else if (byHost)
            {
                message = "主机已退出，连接断开";
            }
            else
            {
                message = "连接已断开";
            }
            ConnectionStopped?.Invoke(message);
        }
    }
}
