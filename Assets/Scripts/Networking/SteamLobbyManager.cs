using System;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace SphereRoom.Networking
{
    /// <summary>
    /// Steam 大厅管理（超级加分项「Steam 联机」的入口层）：
    /// 大厅只解决「怎么找到对方」——创建大厅 / 大厅 ID 加入 / 邀请好友 / 接受邀请自动加入；
    /// 连接数据仍然走自写 SteamTransport（Steam P2P），游戏内同步架构不受影响。
    /// 大厅 ID 是 19 位数字，房主创建后显示在状态栏，发给其他玩家填入即可加入（无需互加好友）。
    /// </summary>
    public class SteamLobbyManager : MonoBehaviour
    {
        /// <summary>接受好友邀请（邀请人在 Steam 聊天发起）时触发，参数：邀请人（房主）SteamId。</summary>
        public event Action<ulong> FriendInviteAccepted;

        /// <summary>操作结果提示（转发给大厅状态栏）。</summary>
        public event Action<string> Status;

        /// <summary>当前所属大厅（房主创建 / 客户端加入后持有）。</summary>
        public Lobby? CurrentLobby { get; private set; }

        public ulong? CurrentLobbyId => CurrentLobby?.Id;

        private SteamTransport _transport;
        private bool _busy;

        public void Initialize(SteamTransport transport)
        {
            _transport = transport;
        }

        private void OnEnable()
        {
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        }

        private void OnDisable()
        {
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
        }

        /// <summary>创建大厅（最多 4 人，公开可搜），完成后回调（失败回调 null）。</summary>
        public async void CreateLobby(Action<Lobby?> onDone)
        {
            if (_busy || !EnsureSteamReady())
            {
                onDone?.Invoke(null);
                return;
            }

            _busy = true;
            try
            {
                Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(4);
                if (lobby.HasValue)
                {
                    lobby.Value.SetJoinable(true);
                    lobby.Value.SetData("game", "SphereRoom");
                    CurrentLobby = lobby;
                    Debug.Log($"[SteamLobby] 大厅已创建，大厅 ID={lobby.Value.Id}，房主={SteamClient.Name}");
                }
                else
                {
                    Debug.LogError("[SteamLobby] 创建大厅失败（CreateLobbyAsync 返回空）");
                }
                onDone?.Invoke(lobby);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamLobby] 创建大厅异常：{e}");
                onDone?.Invoke(null);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>按大厅 ID 加入，完成后回调（找不到大厅回调 null）。</summary>
        public async void JoinLobby(ulong lobbyId, Action<Lobby?> onDone)
        {
            if (_busy || !EnsureSteamReady())
            {
                onDone?.Invoke(null);
                return;
            }

            _busy = true;
            try
            {
                Lobby? lobby = await SteamMatchmaking.JoinLobbyAsync((SteamId)lobbyId);
                if (lobby.HasValue)
                {
                    CurrentLobby = lobby;
                    Debug.Log($"[SteamLobby] 已加入大厅 ID={lobby.Value.Id}，房主={lobby.Value.Owner.Name}");
                }
                else
                {
                    Debug.LogWarning($"[SteamLobby] 加入大厅失败：未找到大厅 {lobbyId}");
                }
                onDone?.Invoke(lobby);
            }
            catch (Exception e)
            {
                Debug.LogError($"[SteamLobby] 加入大厅异常：{e}");
                onDone?.Invoke(null);
            }
            finally
            {
                _busy = false;
            }
        }

        /// <summary>
        /// 房主：打开 Steam 好友邀请面板（被邀人点「接受邀请」会自动进入本房间）。
        /// 返回 null 表示成功；否则返回错误文案（由 HUD 显示）。
        /// </summary>
        public string InviteFriends()
        {
            if (!EnsureSteamReady())
            {
                return "Steam 初始化失败：请先启动 Steam 客户端";
            }
            if (!CurrentLobby.HasValue)
            {
                return "尚未创建 Steam 大厅";
            }
            if (!SteamUtils.IsOverlayEnabled)
            {
                Debug.LogWarning("[SteamLobby] Steam 游戏内界面（Overlay）未启用，无法打开好友邀请面板");
                return "无法打开邀请面板\n请在 Steam 设置 →「游戏中」里\n勾选「在游戏中启用 Steam 界面」";
            }
            SteamFriends.OpenGameInviteOverlay(CurrentLobby.Value.Id);
            Debug.Log($"[SteamLobby] 已请求打开好友邀请面板，大厅 ID={CurrentLobby.Value.Id}");
            return null;
        }

        /// <summary>Steam 聊天里接受好友邀请 → 上报房主 SteamId（由 NetworkBootstrap 自动连接加入）。</summary>
        private void OnGameLobbyJoinRequested(Lobby lobby, SteamId inviter)
        {
            Debug.Log($"[SteamLobby] 收到 Steam 好友邀请：大厅={lobby.Id}，邀请人={inviter}");
            if (NetworkManagerSingletonIsListening())
            {
                Debug.Log("[SteamLobby] 已在游戏内，忽略该邀请");
                return;
            }
            // 补一下大厅成员身份（不影响连接，但保持 Steam 侧状态正确）
            TryJoinSilently(lobby);
            FriendInviteAccepted?.Invoke(inviter.Value);
        }

        private async void TryJoinSilently(Lobby lobby)
        {
            try
            {
                await lobby.Join();
                CurrentLobby = lobby;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SteamLobby] 补加入大厅失败（不影响连接）：{e.Message}");
            }
        }

        private static bool NetworkManagerSingletonIsListening()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm != null && nm.IsListening;
        }

        private bool EnsureSteamReady()
        {
            if (_transport == null || !_transport.EnsureSteamReady())
            {
                Status?.Invoke("Steam 初始化失败：请先启动 Steam 客户端");
                return false;
            }
            return true;
        }
    }
}
