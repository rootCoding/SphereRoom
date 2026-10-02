using UnityEngine;
using SphereRoom.Networking;
using SphereRoom.Player;
using SphereRoom.UI;

namespace SphereRoom.Game
{
    /// <summary>
    /// 游戏启动器：Play 时创建网络引导器（NetworkManager / 传输层 / 出生点分配）与大厅菜单。
    /// 玩家生成统一由 NetworkBootstrap 在主机侧执行（主机权威，按出生点依次生成）；
    /// 本地玩家生成就绪后隐藏大厅并构建 HUD；连接断开时回到大厅并提示。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private NetworkBootstrap _network;
        private LobbyMenu _lobby;
        private PlayerHUD _hud;

        private void Awake()
        {
            _network = gameObject.AddComponent<NetworkBootstrap>();
            _lobby = LobbyMenuBuilder.Build();
            _lobby.OnHostClicked += _network.StartHost;
            _lobby.OnJoinClicked += (ip, port) => _network.StartClient(ip, port);
            _network.ConnectionStopped += OnConnectionStopped;
            _network.Status += _lobby.SetStatus;
        }

        /// <summary>由 LocalPlayerSetup 调用：本地玩家生成完成 → 隐藏大厅 + 构建（或重建）HUD。</summary>
        public void NotifyLocalPlayerReady(StaminaSystem stamina)
        {
            if (_hud != null)
            {
                Destroy(_hud.gameObject);
            }
            _lobby.Hide();
            _hud = HudBuilder.Build(stamina);
        }

        private void OnConnectionStopped(string message)
        {
            if (_hud != null)
            {
                Destroy(_hud.gameObject);
                _hud = null;
            }
            _lobby.Show(message);
        }
    }
}
