using UnityEngine;
using SphereRoom.Networking;
using SphereRoom.Player;
using SphereRoom.UI;

namespace SphereRoom.Game
{
    /// <summary>
    /// 【模块】游戏启动器：Play 时创建网络引导器（NetworkManager / 传输层 / 出生点分配）与大厅菜单。
    ///
    /// 玩家生成统一由 NetworkBootstrap 在主机侧执行（主机权威，按出生点依次生成）；
    /// 本地玩家生成就绪后隐藏大厅并构建 HUD；连接断开时回到大厅并提示。
    /// 整个游戏只有场景里的这一个启动器，网络与 UI 全部运行时动态创建（场景里不放预制体）。
    ///
    /// 职责：把「大厅 UI 事件」与「网络引导器」接起来（事件中枢），自身不含网络/UI 细节。
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        private NetworkBootstrap _network;
        private LobbyMenu _lobby;
        private PlayerHUD _hud;

        /// <summary>
        /// 启动接线（Play 第一帧）：建网络引导器 → 建大厅菜单 → 把所有事件接起来。
        /// 注意：此时 NetworkManager 尚未 StartHost/StartClient，玩家点按钮后才真正开房/加入。
        /// </summary>
        private void Awake()
        {
            // 网络引导器：创建 NetworkManager 与两种传输层（局域网 UTP / Steam 自写传输层）
            _network = gameObject.AddComponent<NetworkBootstrap>();
            // 大厅菜单：运行时构建（纯色 Image + TMP 文字）
            _lobby = LobbyMenuBuilder.Build();
            // 大厅按钮 → 网络动作的接线
            _lobby.OnHostClicked += _network.StartHost;
            _lobby.OnJoinClicked += (ip, port) => _network.StartClient(ip, port);
            // 「联机方式」切换按钮 → 切换传输层（局域网 ⇄ Steam）
            _lobby.OnModeChanged += steam => _network.Mode = steam ? NetMode.Steam : NetMode.UTP;
            // 主机断开 5 秒后强制回大厅（销毁 HUD）
            _lobby.ForcedExit += OnForcedExit;
            // 断线提示（主机断开弹窗 / 超时提示）
            _network.ConnectionStopped += OnConnectionStopped;
            // 网络状态文案 → 大厅状态栏
            _network.Status += _lobby.SetStatus;
            // 接受 Steam 好友邀请自动加入：把大厅 UI 切到 Steam 模式（程序化切换，不触发 OnModeChanged 循环）
            _network.SteamModeRequested += () => _lobby.SetSteamMode(true);
        }

        /// <summary>
        /// 由 LocalPlayerSetup 调用：本地玩家生成完成 → 隐藏大厅 + 构建（或重建）HUD。
        /// Steam 模式额外：注入大厅 ID 显示、接好「邀请好友」按钮的回退邀请流程。
        /// </summary>
        /// <param name="stamina">本地玩家的体力系统（HUD 体力条数据源）</param>
        public void NotifyLocalPlayerReady(StaminaSystem stamina)
        {
            if (_hud != null)
            {
                Destroy(_hud.gameObject);   // 重进房间时重建 HUD（旧 HUD 引用已失效）
            }
            _lobby.Hide();
            bool steamMode = _network.Mode == NetMode.Steam;
            _hud = HudBuilder.Build(stamina, steamMode);
            _hud.InviteClicked += () =>
            {
                // 邀请流程：优先 Steam 官方 Overlay 面板；Overlay 不可用（编辑器常见）→ 回退游戏内好友列表
                InviteResult result = _network.InviteFriends();
                switch (result)
                {
                    case InviteResult.OverlayDisabled:
                        // Overlay 注入失败（编辑器常见）→ 回退到游戏内好友列表直接邀请
                        _hud.ShowFriendList(_network.GetOnlineFriends(), steamId =>
                        {
                            string error = _network.InviteFriend(steamId);
                            if (!string.IsNullOrEmpty(error))
                            {
                                _hud.ShowNotice(error);
                            }
                        });
                        break;
                    case InviteResult.NoLobby:
                        _hud.ShowNotice("尚未创建 Steam 大厅");
                        break;
                    case InviteResult.SteamUnavailable:
                        _hud.ShowNotice("Steam 初始化失败：请先启动 Steam 客户端");
                        break;
                }
            };
            if (steamMode)
            {
                // Esc 菜单第一行显示大厅 ID（只读 + 一键复制）
                _hud.SetLobbyId(_network.CurrentLobbyId?.ToString() ?? string.Empty);
            }
        }

        /// <summary>
        /// 连接断开回调：主机退出 → 弹窗；其他原因 → 直接回大厅。
        /// </summary>
        /// <param name="byHost">是否主机退出（由 NetworkBootstrap 按连接史判定，比 NGO 原值可靠）</param>
        /// <param name="message">非主机原因时的提示文案（超时等）</param>
        private void OnConnectionStopped(bool byHost, string message)
        {
            if (byHost)
            {
                // 主机断开：弹出「主机玩家已断开连接」对话框（5 秒内点确认立即回大厅，超时自动回）
                _lobby.ShowHostDisconnected();
            }
            else
            {
                // 非主机原因掉线（连接超时等）：销毁 HUD 直接回大厅并显示原因
                if (_hud != null)
                {
                    Destroy(_hud.gameObject);
                    _hud = null;
                }
                _lobby.Show(message);
            }
        }

        /// <summary>
        /// 主机断开 5 秒后：强制退出房间，销毁游戏内 HUD。
        /// 此时玩家网络对象已被 NGO 销毁，HUD 里的体力事件源也已失效，必须整体销毁。
        /// </summary>
        private void OnForcedExit()
        {
            if (_hud != null)
            {
                Destroy(_hud.gameObject);
                _hud = null;
            }
        }
    }
}
