using System;
using System.Collections.Generic;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

namespace SphereRoom.Networking
{
    /// <summary>打开邀请面板的结果。</summary>
    public enum InviteResult
    {
        Ok,               // Overlay 邀请面板已打开
        OverlayDisabled,  // 游戏内界面未启用 → 回退到游戏内好友列表邀请
        NoLobby,          // 尚未创建大厅
        SteamUnavailable  // Steam 未初始化
    }

    /// <summary>
    /// 【模块】Steam 大厅管理（超级加分项「Steam 联机」的入口层）：
    /// 大厅只解决「怎么找到对方」——创建大厅 / 大厅 ID 加入 / 邀请好友 / 接受邀请自动加入；
    /// 连接数据仍然走自写 SteamTransport（Steam P2P），游戏内同步架构不受影响。
    ///
    /// 大厅 ID 是 19 位数字，房主创建后显示在状态栏，发给其他玩家填入即可加入（无需互加好友）。
    /// 术语说明：Lobby（大厅）= Steam 官方「房间」概念，本身不承载游戏数据，只用来交换成员信息。
    ///
    /// 异步约定：创建/加入大厅是 Steam 服务器网络往返，方法为 async void + 回调；
    /// _busy 防重入（异步期间禁止再次发起）。所有回调在 Unity 主线程执行（Facepunch 同步上下文）。
    /// </summary>
    public class SteamLobbyManager : MonoBehaviour
    {
        // ==================== 对外事件与属性 ====================

        /// <summary>接受好友邀请（邀请人在 Steam 聊天发起）时触发，参数：邀请人（房主）SteamId。</summary>
        public event Action<ulong> FriendInviteAccepted;

        /// <summary>操作结果提示（转发给大厅状态栏）。</summary>
        public event Action<string> Status;

        /// <summary>当前所属大厅（房主创建 / 客户端加入后持有）。</summary>
        public Lobby? CurrentLobby { get; private set; }

        /// <summary>当前大厅 ID（Esc 菜单显示与复制用；无大厅时为 null）。</summary>
        public ulong? CurrentLobbyId => CurrentLobby?.Id;

        // ==================== 内部状态 ====================

        private SteamTransport _transport;   // 传输层引用（大厅操作前用它确保 Steam 已初始化）
        private bool _busy;                  // 防重入：创建/加入大厅是异步操作，期间禁止再次发起

        // ==================== 生命周期 ====================

        /// <summary>注入传输层引用（大厅操作前要用它确保 Steam 已初始化）。</summary>
        /// <param name="transport">SteamTransport 实例（NetworkBootstrap 创建）</param>
        public void Initialize(SteamTransport transport)
        {
            _transport = transport;
        }

        private void OnEnable()
        {
            // Steam 好友在聊天里点「接受邀请」→ 触发此回调（前提：Steam API 已初始化且回调在泵送）
            SteamFriends.OnGameLobbyJoinRequested += OnGameLobbyJoinRequested;
        }

        private void OnDisable()
        {
            // 与 OnEnable 对称清理（组件销毁时不再接收 Steam 回调）
            SteamFriends.OnGameLobbyJoinRequested -= OnGameLobbyJoinRequested;
        }

        // ==================== 创建 / 加入大厅 ====================

        /// <summary>
        /// 创建大厅（最多 4 人，公开可搜），完成后回调（失败回调 null）。
        /// 异步：Steam 服务器创建大厅需要一次网络往返，不能同步返回结果。
        /// </summary>
        /// <param name="onDone">完成回调，参数为空表示创建失败</param>
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
                // 异步请求 Steam 服务器创建大厅（4 人上限 = 题目 1-4 人）
                Lobby? lobby = await SteamMatchmaking.CreateLobbyAsync(4);
                if (lobby.HasValue)
                {
                    // SetJoinable：允许其他人按 ID 加入（默认创建后即可加入，这里显式保证）
                    // SetData：给大厅打个标记（正式发布时用于搜索过滤，本 Demo 仅作标识）
                    lobby.Value.SetJoinable(true);
                    lobby.Value.SetData("game", "SphereRoom");
                    // 保存当前大厅（房主后续邀请/显示 ID 都靠它）
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
                _busy = false;   // 无论成败都释放防重入锁
            }
        }

        /// <summary>
        /// 按大厅 ID 加入，完成后回调（找不到大厅回调 null）。
        /// 加入成功后通过 lobby.Value.Owner 拿到房主 SteamId，交给传输层连接。
        /// </summary>
        /// <param name="lobbyId">19 位大厅 ID（Esc 菜单可复制的那个数字）</param>
        /// <param name="onDone">完成回调，参数为空表示大厅不存在/加入失败</param>
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
                // 按 ID 找大厅（SteamId 结构可直接由 ulong 隐式转换）
                Lobby? lobby = await SteamMatchmaking.JoinLobbyAsync((SteamId)lobbyId);
                if (lobby.HasValue)
                {
                    // 保存大厅；上层（NetworkBootstrap）随后取 Owner.Id 作为连接目标
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

        // ==================== 邀请好友 ====================

        /// <summary>
        /// 房主：尝试打开 Steam 好友邀请面板（Overlay）。
        /// 编辑器里 Steam Overlay 常常注入失败（IsOverlayEnabled=false）——
        /// 此时返回 OverlayDisabled，由上层回退到「游戏内好友列表」直接邀请（不依赖 Overlay）。
        /// </summary>
        /// <returns>结果枚举（Ok / OverlayDisabled / NoLobby / SteamUnavailable）</returns>
        public InviteResult TryOpenInviteOverlay()
        {
            if (!EnsureSteamReady())
            {
                return InviteResult.SteamUnavailable;
            }
            if (!CurrentLobby.HasValue)
            {
                return InviteResult.NoLobby;
            }
            if (!SteamUtils.IsOverlayEnabled)
            {
                Debug.LogWarning("[SteamLobby] Steam 游戏内界面（Overlay）未启用，回退到游戏内好友列表邀请");
                return InviteResult.OverlayDisabled;
            }
            // Overlay 邀请面板：Steam 官方「邀请好友加入本游戏」对话框（好友接受后经 OnGameLobbyJoinRequested 进入）
            SteamFriends.OpenGameInviteOverlay(CurrentLobby.Value.Id);
            Debug.Log($"[SteamLobby] 已请求打开好友邀请面板，大厅 ID={CurrentLobby.Value.Id}");
            return InviteResult.Ok;
        }

        /// <summary>
        /// 直接邀请指定 Steam 好友（好友在 Steam 聊天收到邀请，接受后自动进入本房间）。返回 null=成功。
        /// </summary>
        /// <param name="steamId">好友的 SteamID64</param>
        /// <returns>null=邀请已发送；否则错误文案</returns>
        public string InviteFriend(ulong steamId)
        {
            if (!CurrentLobby.HasValue)
            {
                return "尚未创建 Steam 大厅";
            }
            // Lobby.InviteFriend：以大厅成员身份邀请好友（对方收到的邀请会带上大厅信息）
            bool ok = CurrentLobby.Value.InviteFriend((SteamId)steamId);
            Debug.Log($"[SteamLobby] 邀请好友 SteamID={steamId}：{(ok ? "邀请已发送" : "发送失败")}");
            return ok ? null : "邀请发送失败，请重试";
        }

        /// <summary>
        /// 获取在线 Steam 好友（名字 + SteamId，按名字排序），用于游戏内好友列表邀请。
        /// </summary>
        /// <returns>在线好友列表（空列表 = 无在线好友或 Steam 未就绪）</returns>
        public List<(string Name, ulong SteamId)> GetOnlineFriends()
        {
            var result = new List<(string Name, ulong SteamId)>();
            if (!SteamClient.IsValid)
            {
                return result;
            }
            // SteamFriends.GetFriends：拉取本账号全部好友（含离线），这里只保留在线的好友
            foreach (Friend friend in SteamFriends.GetFriends())
            {
                // IsOnline = 当前在线（不等同于在玩本游戏，游戏内列表只做邀请用途）
                if (friend.IsOnline)
                {
                    result.Add((friend.Name, friend.Id.Value));
                }
            }
            // 按名字排序让列表展示稳定（Steam 返回顺序不保证，不排序的话每次打开列表顺序会跳）
            result.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return result;
        }

        // ==================== 接受邀请自动加入 ====================

        /// <summary>
        /// Steam 聊天里接受好友邀请 → 上报房主 SteamId（由 NetworkBootstrap 自动连接加入）。
        /// 已在游戏内时忽略新邀请（避免打断当前房间）。
        /// </summary>
        /// <param name="lobby">邀请关联的大厅对象</param>
        /// <param name="inviter">邀请人（即房主）的 SteamId</param>
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

        /// <summary>静默补加入大厅（接受邀请后走正常成员流程；失败也不阻塞连接，仅记日志）。</summary>
        /// <param name="lobby">邀请关联的大厅</param>
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

        // ==================== 工具方法 ====================

        /// <summary>是否已在联机会话中（已在游戏内时忽略新邀请）。</summary>
        private static bool NetworkManagerSingletonIsListening()
        {
            var nm = Unity.Netcode.NetworkManager.Singleton;
            return nm != null && nm.IsListening;
        }

        /// <summary>
        /// 大厅操作前置检查：确保 Steam 已初始化，失败时给出状态栏提示。
        /// </summary>
        /// <returns>true=Steam 就绪，可继续大厅操作</returns>
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

    // ==================== 设计笔记 ====================
    // 为什么「大厅 + 传输层」要分层？
    // 大厅（本类）只负责「找到对方」（成员信息交换），连接数据走 SteamTransport（P2P）——
    // 两件事在 Steam API 里本来就是两套接口（Matchmaking 与 Networking），
    // 分层后：换平台（如 EOS）只需换大厅层，传输层不动；反过来亦然。
    //
    // 邀请的双通道策略（TryOpenInviteOverlay + InviteFriend）：
    // - 优先 Steam Overlay 官方邀请面板（体验最好，被邀人点接受即触发 OnGameLobbyJoinRequested）
    // - Overlay 注入失败（编辑器常见）→ 回退游戏内好友列表直接 InviteFriend，
    //   好友在 Steam 聊天收到邀请提示，接受后同样触发自动加入——两条通道殊途同归。
}

