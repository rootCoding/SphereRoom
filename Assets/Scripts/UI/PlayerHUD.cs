using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;
using SphereRoom.Player;

namespace SphereRoom.UI
{
    /// <summary>
    /// 【模块】玩家 HUD：体力槽（长度 + 数值 + 平滑插值）、准星、左下角操作说明、Esc 菜单。
    /// UI 结构与引用由 HudBuilder 在运行时构建，并通过 Initialize 注入（场景中无 UI 结构）。
    /// Esc：解锁鼠标并弹出菜单（再按 Esc 或点「继续游戏」关闭并重新锁定鼠标）。
    /// 附加面板（Steam 模式）：大厅 ID 行（一键复制）、邀请好友按钮、提示对话框、好友列表面板。
    ///
    /// 职责边界：
    /// - 本类只负责「已有 UI 的行为」（输入处理、状态刷新、面板开关），不创建 UI
    /// - 布局与样式全部在 HudBuilder；网络业务（邀请/复制数据）在 GameBootstrap 接线
    /// - 局域网模式下 Steam 附加 UI 的引用为 null，所有相关代码自动跳过
    /// </summary>
    public class PlayerHUD : MonoBehaviour
    {
        // ==================== 对外事件 ====================

        /// <summary>
        /// 点击「邀请 Steam 好友」（由 GameBootstrap 接到 NetworkBootstrap 打开 Steam 邀请面板）。
        /// </summary>
        public event Action InviteClicked;

        // ==================== 常量 ====================

        // 体力三阶段颜色：绿（充沛）→ 黄（过半）→ 红（见底）
        private static readonly Color StaminaGreen = new Color(0.3f, 0.85f, 0.4f);
        private static readonly Color StaminaYellow = new Color(0.9f, 0.8f, 0.2f);
        private static readonly Color StaminaRed = new Color(0.9f, 0.25f, 0.2f);

        // 好友列表布局常量：最多 8 行，每行 44 高，第一行从 y=124 开始（对应 HudBuilder 的面板尺寸）
        private const int MaxFriendRows = 8;
        private const float FriendRowHeight = 44f;
        private const float FriendRowTopY = 124f;

        // ==================== 核心 UI 引用（HudBuilder 构建后注入） ====================

        private Image _staminaFill;              // 体力槽填充条（通过水平缩放表现长短）
        private TextMeshProUGUI _staminaText;    // 体力数值文本（0~100）
        private StaminaSystem _stamina;          // 体力数据源（订阅 Changed 事件）
        private GameObject _crosshair;           // 准星
        private GameObject _helpPanel;           // 左下角操作说明
        private GameObject _menuPanel;           // Esc 菜单
        private Toggle _crosshairToggle;         // 菜单里「准星」开关
        private Toggle _helpToggle;              // 菜单里「操作说明」开关
        private Button _resumeButton;            // 「继续游戏」
        private Button _quitButton;              // 「退出游戏」

        // ---- Steam 模式附加 UI（局域网模式下为 null，代码判空跳过）----
        private Button _inviteButton;
        // 「邀请 Steam 好友」按钮（点击 → InviteClicked 事件）
        private TextMeshProUGUI _lobbyIdText;
        // 大厅 ID 纯数字文本（复制只取它，不带「大厅ID」前缀）
        private Button _copyButton;
        // 「复制」按钮（点击 → 剪贴板）
        private TextMeshProUGUI _copyLabel;
        // 复制按钮上的文字（复制/已复制 切换）
        private GameObject _noticeDialog;
        // 居中提示对话框（ShowNotice 弹出）
        private TextMeshProUGUI _noticeText;
        // 提示对话框文本
        private Button _noticeConfirm;
        // 提示对话框「确认」（点击关闭）
        private GameObject _friendListPanel;
        // 游戏内好友列表面板（Overlay 回退方案）
        private Transform _friendRows;
        // 好友行容器（行由代码动态生成）
        private Button _friendCloseButton;
        // 好友列表「关闭」按钮

        private TextMeshProUGUI _tappedText;
        // 碰球「Tapped」提示（0.8 秒自动隐藏）
        private Coroutine _tappedCoroutine;
        // Tapped 自动隐藏协程（连续碰球时重启计时）

        // ==================== 运行状态 ====================

        private bool _initialized;
        // Initialize 是否已调用（防过早使用：Start/Update 先判空）
        private bool _menuOpen;
        // Esc 菜单开关状态（SetMenuOpen 统一维护）
        private float _targetStamina = 1f;
        // 实际体力（事件驱动，StaminaSystem.Changed 更新）
        private float _displayedStamina = 1f;
        // 显示体力（逐帧插值逼近目标值，UI 丝滑的关键）

        // ==================== 初始化 ====================

        /// <summary>
        /// 由 HudBuilder 在运行时调用，注入所有 UI 引用与体力系统。
        /// 局域网模式不传 Steam 附加 UI（inviteButton/lobbyIdText/copyButton 等保持 null），相关代码自动跳过。
        /// </summary>
        public void Initialize(Image staminaFill, TextMeshProUGUI staminaText, StaminaSystem stamina,
            GameObject crosshair, GameObject helpPanel, GameObject menuPanel,
            Toggle crosshairToggle, Toggle helpToggle, Button resumeButton, TextMeshProUGUI tappedText,
            Button quitButton, Button inviteButton = null, TextMeshProUGUI lobbyIdText = null,
            Button copyButton = null, GameObject noticeDialog = null, TextMeshProUGUI noticeText = null,
            Button noticeConfirm = null, GameObject friendListPanel = null, Transform friendRows = null,
            Button friendCloseButton = null)
        {
            // 步骤 1：保存全部引用（Steam 附加 UI 可空）
            _staminaFill = staminaFill;
            _staminaText = staminaText;
            _stamina = stamina;
            _crosshair = crosshair;
            _helpPanel = helpPanel;
            _menuPanel = menuPanel;
            _crosshairToggle = crosshairToggle;
            _helpToggle = helpToggle;
            _resumeButton = resumeButton;
            _tappedText = tappedText;
            _quitButton = quitButton;
            _inviteButton = inviteButton;
            _lobbyIdText = lobbyIdText;
            _copyButton = copyButton;
            _noticeDialog = noticeDialog;
            _noticeText = noticeText;
            _noticeConfirm = noticeConfirm;
            _friendListPanel = friendListPanel;
            _friendRows = friendRows;
            _friendCloseButton = friendCloseButton;
            if (_copyButton != null)
            {
                // 复制按钮的文案（「复制」/「已复制」）挂在它的 Label 子物体上
                _copyLabel = _copyButton.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            }
            _initialized = true;

            // 步骤 2：订阅体力事件并立即同步一次初始值（防止事件订阅前已经变化过）
            if (_stamina != null)
            {
                _stamina.Changed += OnStaminaChanged;
                OnStaminaChanged(_stamina.Normalized);
            }

            // 步骤 3：开关初始状态跟随实际可见性，避免「开关显示开、实际隐藏」的不一致
            _crosshairToggle.isOn = _crosshair.activeSelf;
            _crosshairToggle.onValueChanged.AddListener(ShowCrosshair);
            _helpToggle.isOn = _helpPanel.activeSelf;
            _helpToggle.onValueChanged.AddListener(ShowHelp);
            // 步骤 4：按钮接线（继续/退出必接；Steam 附加按钮判空接）
            _resumeButton.onClick.AddListener(Resume);
            _quitButton.onClick.AddListener(QuitGame);
            if (_inviteButton != null)
            {
                _inviteButton.onClick.AddListener(() => InviteClicked?.Invoke());
            }
            if (_copyButton != null)
            {
                _copyButton.onClick.AddListener(CopyLobbyId);
            }
            if (_noticeConfirm != null)
            {
                _noticeConfirm.onClick.AddListener(CloseNoticeDialog);
            }
            if (_friendCloseButton != null)
            {
                _friendCloseButton.onClick.AddListener(CloseFriendList);
            }
        }

        // ==================== Steam 大厅 ID 显示与复制 ====================

        /// <summary>
        /// 设置 Esc 菜单显示的 Steam 大厅 ID 纯数字文本（GameBootstrap 在本地玩家就绪时调用）。
        /// 「大厅ID」描述是左侧独立静态文本；复制时只取这里的纯数字。
        /// </summary>
        /// <param name="lobbyId">19 位大厅 ID 数字串</param>
        public void SetLobbyId(string lobbyId)
        {
            if (_lobbyIdText != null)
            {
                _lobbyIdText.text = lobbyId;
            }
        }

        /// <summary>
        /// 复制大厅 ID 到剪贴板，按钮变为「已复制」（下次打开菜单时恢复「复制」）。
        /// </summary>
        private void CopyLobbyId()
        {
            string id = _lobbyIdText != null ? _lobbyIdText.text : string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                return;   // 还没拿到大厅 ID（异常时序）时忽略
            }
            // GUIUtility.systemCopyBuffer：Unity 的系统剪贴板写入接口
            GUIUtility.systemCopyBuffer = id;
            Debug.Log($"[PlayerHUD] 大厅 ID 已复制：{id}");
            if (_copyLabel != null)
            {
                _copyLabel.text = "已复制";
            }
        }

        // ==================== 生命周期与每帧刷新 ====================

        /// <summary>开局初始化：默认收起菜单（锁定鼠标开始游戏）。</summary>
        private void Start()
        {
            if (!_initialized)
            {
                return;
            }
            // 开局默认收起菜单（锁定鼠标开始游戏）
            SetMenuOpen(false);
        }

        /// <summary>
        /// 每帧两件事：Esc 开关菜单 + 体力显示插值刷新。
        /// 体力插值独立于事件——实际值由事件更新目标，显示值每帧追目标，两者解耦。
        /// </summary>
        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            // Esc 开/关菜单（wasPressedThisFrame 保证一次按键只触发一次，不会开完立刻关）
            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetMenuOpen(!_menuOpen);
            }

            // 体力显示插值：指数平滑逼近目标值，画面帧率波动时长度变化依然丝滑
            // （1 - exp(-10·dt)：帧率越高单步越小，任何帧率下都以相同视觉速度收敛）
            _displayedStamina = Mathf.Lerp(_displayedStamina, _targetStamina,
                1f - Mathf.Exp(-10f * Time.deltaTime));
            // 用插值后的显示值刷新 UI（长度/颜色/数值三要素）
            RefreshStaminaView();
        }

        // ==================== 体力显示 ====================

        /// <summary>
        /// 体力实际值变化（事件驱动），记录为目标值由插值逐帧追赶。
        /// </summary>
        /// <param name="normalized">新的体力值（0~1）</param>
        private void OnStaminaChanged(float normalized)
        {
            _targetStamina = normalized;
        }

        /// <summary>
        /// 刷新体力槽三要素：
        /// 长度（水平缩放，左锚点固定）、颜色（三阶段变色）、数值文本（0~100 取整）。
        /// </summary>
        private void RefreshStaminaView()
        {
            // 长度：左锚点 + 水平缩放实现长短变化（无精灵 Image 的 Filled 模式不生效）
            _staminaFill.rectTransform.localScale = new Vector3(_displayedStamina, 1f, 1f);
            // 颜色：三阶段变色阈值 >66% 绿 / >33% 黄 / 其余红（与体力语义对应：充沛/过半/见底）
            _staminaFill.color = _displayedStamina > 0.66f ? StaminaGreen
                : _displayedStamina > 0.33f ? StaminaYellow
                : StaminaRed;
            // 数值：0~1 归一化值 × 100 取整显示（与长度/颜色同源，三者永不矛盾）
            _staminaText.text = Mathf.RoundToInt(_displayedStamina * 100f).ToString();
        }

        // ==================== 碰球提示 ====================

        /// <summary>
        /// 碰球提示：显示「Tapped」，约 0.8 秒后自动消失。
        /// 连续碰球时重启计时（StopCoroutine 旧协程再开新的），提示保持可见而不是闪断。
        /// </summary>
        public void ShowTapped()
        {
            if (_tappedText == null)
            {
                return;
            }
            if (_tappedCoroutine != null)
            {
                StopCoroutine(_tappedCoroutine);
            }
            _tappedCoroutine = StartCoroutine(TappedRoutine());
        }

        /// <summary>显示 → 等 0.8 秒 → 隐藏（协程可被下一次 ShowTapped 打断重启）。</summary>
        private IEnumerator TappedRoutine()
        {
            _tappedText.gameObject.SetActive(true);
            yield return new WaitForSeconds(0.8f);
            _tappedText.gameObject.SetActive(false);
            _tappedCoroutine = null;
        }

        // ==================== 提示对话框 ====================

        /// <summary>
        /// 弹出居中提示对话框（内容文本 + 「确认」按钮，点击确认关闭）。
        /// 用途：邀请面板打不开等原因的系统提示。
        /// </summary>
        /// <param name="text">提示文案（支持 \n 换行）</param>
        public void ShowNotice(string text)
        {
            if (_noticeDialog == null || _noticeText == null)
            {
                return;
            }
            _noticeText.text = text;
            _noticeDialog.SetActive(true);
        }

        /// <summary>提示对话框「确认」：关闭对话框。</summary>
        private void CloseNoticeDialog()
        {
            if (_noticeDialog != null)
            {
                _noticeDialog.SetActive(false);
            }
        }

        // ==================== 好友列表面板（Overlay 回退邀请方案） ====================

        /// <summary>
        /// 弹出游戏内好友列表面板（Overlay 不可用时的邀请回退方案）：
        /// 每个在线好友一行「名字 + 邀请按钮」，点击邀请后按钮变「已邀请」并禁用。
        /// 面板在打开前会清空上次的行（行是动态生成的，反复打开不积累）。
        /// </summary>
        /// <param name="friends">在线好友列表（名字 + SteamId）</param>
        /// <param name="onInvite">点击某行「邀请」时回调（参数为该好友 SteamId）</param>
        public void ShowFriendList(List<(string Name, ulong SteamId)> friends, Action<ulong> onInvite)
        {
            if (_friendListPanel == null || _friendRows == null)
            {
                return;
            }

            // 先清空旧行，再按当前列表重建（面板多次打开不残留）
            ClearFriendRows();
            if (friends == null || friends.Count == 0)
            {
                // 没有在线好友：面板里放一行说明文字，避免弹出空面板
                TextMeshProUGUI empty = UiFactory.CreateText(_friendRows, "Empty", "没有在线的好友", 20,
                    new Vector2(360f, FriendRowHeight), new Vector2(0f, FriendRowTopY));
                empty.color = new Color(0.7f, 0.75f, 0.8f);
            }
            else
            {
                // 最多显示 8 行（面板高度限制），多余的在线好友不展示
                int count = Mathf.Min(friends.Count, MaxFriendRows);
                for (int i = 0; i < count; i++)
                {
                    CreateFriendRow(friends[i], onInvite, i);
                }
            }
            _friendListPanel.SetActive(true);
        }

        /// <summary>
        /// 创建一行好友：左侧名字 + 右侧「邀请」按钮（点后变「已邀请」并禁用防重复邀请）。
        /// </summary>
        /// <param name="friend">好友（名字 + SteamId）</param>
        /// <param name="onInvite">点击邀请的回调</param>
        /// <param name="index">行号（0 起，决定垂直位置）</param>
        private void CreateFriendRow((string Name, ulong SteamId) friend, Action<ulong> onInvite, int index)
        {
            // 行位置从上往下排列：第一行 y=124，每行下移 44
            float y = FriendRowTopY - index * FriendRowHeight;

            // 名字（左对齐，亮色）
            TextMeshProUGUI nameText = UiFactory.CreateText(_friendRows, $"Name{index}", friend.Name, 20,
                new Vector2(240f, FriendRowHeight), new Vector2(-75f, y));
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
            nameText.color = new Color(0.9f, 0.93f, 0.96f);

            // 邀请按钮（绿色系，与 HudBuilder 的「邀请 Steam 好友」按钮同色）
            GameObject btnGo = new GameObject($"Invite{index}", typeof(RectTransform));
            btnGo.transform.SetParent(_friendRows, false);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.sizeDelta = new Vector2(96f, 34f);
            btnRt.anchoredPosition = new Vector2(150f, y);
            Image btnBg = btnGo.AddComponent<Image>();
            btnBg.color = new Color(0.25f, 0.5f, 0.35f);
            Button inviteBtn = btnGo.AddComponent<Button>();
            inviteBtn.targetGraphic = btnBg;
            // ColorBlock：悬停变亮 / 按下变暗 / 选中同悬停（纯色按钮的标准配色方式）
            ColorBlock colors = inviteBtn.colors;
            colors.normalColor = new Color(0.25f, 0.5f, 0.35f);
            colors.highlightedColor = new Color(0.36f, 0.64f, 0.47f);
            colors.pressedColor = new Color(0.16f, 0.35f, 0.24f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            inviteBtn.colors = colors;
            TextMeshProUGUI label = UiFactory.CreateText(btnRt, "Label", "邀请", 18, new Vector2(96f, 34f), Vector2.zero);

            // 闭包捕获当前行的 steamId（循环变量陷阱：必须在循环外复制到局部变量）
            ulong steamId = friend.SteamId;
            inviteBtn.onClick.AddListener(() =>
            {
                onInvite?.Invoke(steamId);
                label.text = "已邀请";           // 反馈已发
                inviteBtn.interactable = false;  // 禁用防重复邀请
            });
        }

        /// <summary>清空好友行容器（面板重开时调用，旧行全部销毁重建）。</summary>
        private void ClearFriendRows()
        {
            if (_friendRows == null)
            {
                return;
            }
            // 倒序遍历销毁：Destroy 是延迟到帧末执行的，正序遍历下标会乱
            for (int i = _friendRows.childCount - 1; i >= 0; i--)
            {
                Destroy(_friendRows.GetChild(i).gameObject);
            }
        }

        /// <summary>好友列表「关闭」：隐藏面板。</summary>
        private void CloseFriendList()
        {
            if (_friendListPanel != null)
            {
                _friendListPanel.SetActive(false);
            }
        }

        // ==================== Esc 菜单 ====================

        /// <summary>
        /// 开关 Esc 菜单：同步鼠标锁定状态（菜单开=解锁可见，菜单关=锁定隐藏）。
        /// 菜单打开时顺带把「已复制」按钮文案恢复为「复制」。
        /// 全项目鼠标锁定的唯一入口：第一人称控制器只读 Cursor 状态，从不直接改。
        /// </summary>
        /// <param name="open">目标状态（true=打开菜单）</param>
        private void SetMenuOpen(bool open)
        {
            _menuOpen = open;
            _menuPanel.SetActive(open);
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;

            // 再次打开菜单时，「已复制」按钮文案恢复为「复制」（用户约定的交互）
            if (open && _copyLabel != null)
            {
                _copyLabel.text = "复制";
            }
        }

        /// <summary>菜单开关：显示/隐藏准星。</summary>
        /// <param name="show">是否显示</param>
        private void ShowCrosshair(bool show)
        {
            _crosshair.SetActive(show);
        }

        /// <summary>菜单开关：显示/隐藏左下角操作说明。</summary>
        /// <param name="show">是否显示</param>
        private void ShowHelp(bool show)
        {
            _helpPanel.SetActive(show);
        }

        /// <summary>「继续游戏」：收起菜单、重新锁定鼠标。</summary>
        private void Resume()
        {
            SetMenuOpen(false);
        }

        // ==================== 退出 ====================

        /// <summary>
        /// 退出游戏：先关闭网络会话（主机关闭会通知所有客户端断开；
        /// 编辑器/MPPM 里 Application.Quit 可能不生效，关会话能保证主机真的退出），再尝试退出应用。
        /// </summary>
        private void QuitGame()
        {
            Debug.Log("[PlayerHUD] 退出游戏");
            NetworkManager networkManager = NetworkManager.Singleton;
            // 关会话先于退出应用：NGO 关服会通知所有客户端主机退出，
            // 且编辑器/MPPM 里 Application.Quit 可能不生效——关会话保证「主机退出」语义一定成立
            if (networkManager != null && networkManager.IsListening)
            {
                // 关会话 = 通知所有客户端主机退出（客户端弹「主机玩家已断开连接」）
                networkManager.Shutdown();
            }
            Application.Quit();
        }

        /// <summary>销毁时清理所有事件订阅（UI 是运行时动态重建的，不清理会泄漏监听/重复回调）。</summary>
        private void OnDestroy()
        {
            if (_stamina != null)
            {
                _stamina.Changed -= OnStaminaChanged;
            }
            if (_crosshairToggle != null)
            {
                _crosshairToggle.onValueChanged.RemoveListener(ShowCrosshair);
            }
            if (_helpToggle != null)
            {
                _helpToggle.onValueChanged.RemoveListener(ShowHelp);
            }
            if (_resumeButton != null)
            {
                _resumeButton.onClick.RemoveListener(Resume);
            }
            if (_quitButton != null)
            {
                _quitButton.onClick.RemoveListener(QuitGame);
            }
            if (_inviteButton != null)
            {
                _inviteButton.onClick.RemoveAllListeners();
            }
            if (_copyButton != null)
            {
                _copyButton.onClick.RemoveAllListeners();
            }
            if (_noticeConfirm != null)
            {
                _noticeConfirm.onClick.RemoveAllListeners();
            }
        }
    }

    // ==================== 设计笔记 ====================
    // HUD 的「运行时动态构建 + Initialize 注入」模式说明：
    // - 玩家可能反复进出房间（重进时 HUD 销毁重建），Prefab 实例化会累积序列化引用，注入模式无此问题
    // - Steam 附加 UI 按需传入：局域网模式引用为 null，逻辑自动跳过（防御式判空）
    //
    // 体力显示的「目标值 + 插值」两层设计：
    // 实际体力由 StaminaSystem 事件驱动（离散变化），显示值每帧指数平滑逼近——
    // 好处是帧率波动时长条依然顺滑，且 UI 不反向驱动逻辑数据（单向数据流）。
    //
    // 鼠标锁定的单一管理者：全部锁定/解锁都走 SetMenuOpen（开局收菜单、Esc 开关、继续游戏），
    // 避免多处改 Cursor 状态互相打架（第一人称控制器只读状态，从不改）。
}

