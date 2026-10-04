using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SphereRoom.UI
{
    /// <summary>
    /// 【模块】大厅菜单：创建房间（主机）/ 加入房间（IP 直连或 Steam 大厅 ID）+ 状态提示。
    ///
    /// 主机断开时弹出对话框（文本「主机玩家已断开连接」+「确认」按钮）：
    ///   点击「确认」→ 立即回到大厅主页面；
    ///   不点击 → 5 秒后自动回到大厅主页面。
    /// 强制退出时触发 ForcedExit 事件，由 GameBootstrap 销毁游戏内 HUD。
    /// 联机方式按钮切换：局域网直连 ⇄ Steam 联机（切换时刷新输入框/按钮文案并通知外部切传输层）。
    /// UI 结构与引用由 LobbyMenuBuilder 运行时构建并通过 Initialize 注入。
    ///
    /// 事件约定（外部接线在 GameBootstrap.Awake）：
    /// - OnHostClicked / OnJoinClicked → NetworkBootstrap 开房/加入
    /// - OnModeChanged → NetworkBootstrap.Mode 切换传输层
    /// - ForcedExit → GameBootstrap 销毁 HUD
    /// </summary>
    public class LobbyMenu : MonoBehaviour
    {
        // ==================== 对外事件 ====================

        /// <summary>主机断开后回到大厅主页面时触发（GameBootstrap 借此销毁 HUD）。</summary>
        public event Action ForcedExit;

        /// <summary>点击「创建房间」（参数：端口；Steam 模式下端口被忽略）。</summary>
        public event Action<int> OnHostClicked;

        /// <summary>点击「加入房间」（参数：输入的 IP 与端口；Steam 模式下第一参数是大厅 ID）。</summary>
        public event Action<string, int> OnJoinClicked;

        /// <summary>切换联机方式（参数：是否 Steam 模式），由外部切换网络传输层。</summary>
        public event Action<bool> OnModeChanged;

        // ---- 结构引用 ----
        private GameObject _root;              // 整个大厅 Canvas
        private GameObject _panel;             // 主面板（标题/输入框/按钮）
        private GameObject _disconnectDialog;  // 主机断开对话框

        // ---- 交互控件 ----
        private Button _hostButton;            // 「创建房间」
        private Button _joinButton;            // 「加入房间」
        private Button _confirmButton;         // 断开对话框「确认」
        private TMP_InputField _ipField;       // 输入框（局域网=主机 IP，Steam=大厅 ID）
        private TMP_InputField _portField;     // 端口输入（仅局域网模式显示）
        private TextMeshProUGUI _status;       // 底部状态栏

        // ---- 联机方式切换相关 ----
        private Button _modeButton;                 // 「联机方式」按钮
        private TextMeshProUGUI _modeButtonLabel;   // 按钮文字（局域网直连/Steam 联机）
        private TextMeshProUGUI _ipLabel;           // 输入框左侧标签（主机 IP/大厅 ID）
        private TextMeshProUGUI _ipPlaceholder;     // 输入框占位文字
        private GameObject _portLabelGo;            // 「端口」标签物体
        private TextMeshProUGUI _hostButtonLabel;   // 创建按钮文字（含 Steam 后缀）
        private TextMeshProUGUI _joinButtonLabel;   // 加入按钮文字
        private bool _steamMode;                    // 当前是否 Steam 模式
        private string _utpIpText = "127.0.0.1";    // 切到 Steam 模式前暂存的 IP 文本（切回时还原）

        private Coroutine _forcedExitCoroutine;    // 主机断开后 5 秒自动回大厅的倒计时
        private bool _initialized;                 // Initialize 是否已调用

        // ==================== 初始化 ====================

        /// <summary>
        /// 注入 Builder 构建好的全部引用并接线（不在此创建任何 UI）。
        /// 接线内容：创建/加入/确认/切换模式四个按钮的点击处理。
        /// </summary>
        public void Initialize(GameObject root, GameObject panel, GameObject disconnectDialog,
            Button hostButton, Button joinButton, Button confirmButton,
            TMP_InputField ipField, TMP_InputField portField, TextMeshProUGUI status,
            Button modeButton, TextMeshProUGUI modeButtonLabel,
            TextMeshProUGUI ipLabel, TextMeshProUGUI ipPlaceholder, GameObject portLabelGo,
            TextMeshProUGUI hostButtonLabel, TextMeshProUGUI joinButtonLabel)
        {
            // 1) 保存引用
            _root = root;
            _panel = panel;
            _disconnectDialog = disconnectDialog;
            _hostButton = hostButton;
            _joinButton = joinButton;
            _confirmButton = confirmButton;
            _ipField = ipField;
            _portField = portField;
            _status = status;
            _modeButton = modeButton;
            _modeButtonLabel = modeButtonLabel;
            _ipLabel = ipLabel;
            _ipPlaceholder = ipPlaceholder;
            _portLabelGo = portLabelGo;
            _hostButtonLabel = hostButtonLabel;
            _joinButtonLabel = joinButtonLabel;
            _initialized = true;

            // 2) 创建房间：状态栏提示 → 隐藏旧对话框 → 上报端口（Steam 模式忽略端口）
            _hostButton.onClick.AddListener(() =>
            {
                Debug.Log("[LobbyMenu] 创建房间按钮被点击");
                SetStatus("正在创建房间…");
                HideDisconnectDialog();
                OnHostClicked?.Invoke(ReadPort());
            });
            // 3) 加入房间：IP 输入框在两种模式下分别承载「主机 IP」/「大厅 ID」
            _joinButton.onClick.AddListener(() =>
            {
                Debug.Log("[LobbyMenu] 加入房间按钮被点击");
                SetStatus("正在加入房间…");
                HideDisconnectDialog();
                OnJoinClicked?.Invoke(_ipField.text, ReadPort());
            });
            // 4) 主机断开对话框的「确认」：立即回主页面
            _confirmButton.onClick.AddListener(() =>
            {
                Debug.Log("[LobbyMenu] 确认按钮被点击，立即回到主页面");
                ForceExitToLobby();
            });
            // 5) 联机方式切换按钮：翻转模式 → 刷新 UI → 通知外部切换传输层
            _modeButton.onClick.AddListener(() =>
            {
                Debug.Log($"[LobbyMenu] 联机方式切换：{(_steamMode ? "Steam → 局域网" : "局域网 → Steam")}");
                _steamMode = !_steamMode;
                ApplyModeUI();
                OnModeChanged?.Invoke(_steamMode);
            });

            // 初始按默认模式（局域网）刷一遍 UI，然后显示大厅
            ApplyModeUI();
            Show();
        }

        // ==================== 联机方式切换 ====================

        /// <summary>
        /// 按当前联机方式刷新大厅 UI：
        /// Steam 模式——隐藏端口、输入框变「大厅 ID」并加宽居中（放得下 19 位数字与占位文案）；
        /// 局域网模式——还原 IP 输入与端口行。
        /// </summary>
        private void ApplyModeUI()
        {
            // 第一步：按钮与标签文案
            _modeButtonLabel.text = _steamMode ? "联机方式：Steam 联机" : "联机方式：局域网直连";
            _ipLabel.text = _steamMode ? "大厅 ID" : "主机 IP";
            _portLabelGo.SetActive(!_steamMode);
            _portField.gameObject.SetActive(!_steamMode);
            _hostButtonLabel.text = _steamMode ? "创建房间（Steam）" : "创建房间";
            _joinButtonLabel.text = _steamMode ? "加入房间（Steam）" : "加入房间";

            // 第二步：输入框/标签的尺寸与位置
            RectTransform inputRt = _ipField.GetComponent<RectTransform>();
            RectTransform labelRt = _ipLabel.rectTransform;
            if (_steamMode)
            {
                // 暂存 IP 文本，切回局域网时还原（用户输过的 IP 不丢失）
                _utpIpText = _ipField.text;
                _ipField.text = string.Empty;
                _ipPlaceholder.text = "输入大厅 ID（19 位数字）";
                // 输入框加宽（放得下占位文案）并整体居中，标签跟随左移
                inputRt.sizeDelta = new Vector2(300f, 44f);
                inputRt.anchoredPosition = new Vector2(54f, 40f);
                labelRt.anchoredPosition = new Vector2(-154f, 40f);
                // 占位文字与输入正文居中显示
                _ipPlaceholder.alignment = TextAlignmentOptions.Midline;
                _ipField.textComponent.alignment = TextAlignmentOptions.Midline;
            }
            else
            {
                // 还原 IP 文本与局域网布局（位置/尺寸/左对齐全部还原）
                _ipField.text = string.IsNullOrWhiteSpace(_utpIpText) ? "127.0.0.1" : _utpIpText;
                _ipPlaceholder.text = "例如 127.0.0.1";
                inputRt.sizeDelta = new Vector2(220f, 44f);
                inputRt.anchoredPosition = new Vector2(56f, 40f);
                labelRt.anchoredPosition = new Vector2(-116f, 40f);
                _ipPlaceholder.alignment = TextAlignmentOptions.MidlineLeft;
                _ipField.textComponent.alignment = TextAlignmentOptions.MidlineLeft;
            }
        }

        /// <summary>
        /// 程序化切换联机方式（接受 Steam 好友邀请时由 GameBootstrap 调用），不触发 OnModeChanged。
        /// 与按钮切换的区别：只刷 UI 不上报事件（模式切换已由邀请流程自行处理）。
        /// </summary>
        /// <param name="steam">目标模式（true=Steam）</param>
        public void SetSteamMode(bool steam)
        {
            if (_steamMode == steam)
            {
                return;   // 已是目标模式，直接返回
            }
            _steamMode = steam;
            ApplyModeUI();
        }

        // ==================== 主机断开流程 ====================

        /// <summary>
        /// 主机断开：主面板隐藏、弹出对话框（解锁鼠标）；
        /// 点「确认」立即回主页面，不点 5 秒后自动回。
        /// </summary>
        public void ShowHostDisconnected()
        {
            if (!_initialized)
            {
                return;
            }

            // 三步：面板隐藏 → 对话框显示 → 解锁鼠标
            _root.SetActive(true);
            _panel.SetActive(false);
            _disconnectDialog.SetActive(true);
            // 断线时鼠标必然处于锁定状态（游戏内），解锁让玩家能点「确认」
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // 重启 5 秒倒计时（重复触发时先停旧的，保证只跑一个倒计时）
            if (_forcedExitCoroutine != null)
            {
                StopCoroutine(_forcedExitCoroutine);
            }
            _forcedExitCoroutine = StartCoroutine(ForcedExitRoutine());
        }

        /// <summary>5 秒倒计时：未点击「确认」时自动回到大厅主页面。</summary>
        private IEnumerator ForcedExitRoutine()
        {
            yield return new WaitForSeconds(5f);
            _forcedExitCoroutine = null;
            ForceExitToLobby();
        }

        /// <summary>
        /// 强制退出房间，回到大厅主页面（点确认或 5 秒超时都会走这里）。
        /// 停止倒计时 → 恢复主面板 → 提示「主机已断开连接」→ 通知 GameBootstrap 销毁 HUD。
        /// </summary>
        private void ForceExitToLobby()
        {
            // 停止倒计时（点了确认就不需要再自动执行）
            if (_forcedExitCoroutine != null)
            {
                StopCoroutine(_forcedExitCoroutine);
                _forcedExitCoroutine = null;
            }

            // 恢复主面板 + 状态提示
            _disconnectDialog.SetActive(false);
            _panel.SetActive(true);
            SetStatus("主机已断开连接");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            // 通知 GameBootstrap 销毁游戏内 HUD（玩家对象已随主机断开销毁）
            ForcedExit?.Invoke();
        }

        // ==================== 显隐与状态 ====================

        /// <summary>
        /// 显示大厅并解锁鼠标（可带状态文案，如断线原因）。
        /// </summary>
        /// <param name="status">可选的状态栏文案</param>
        public void Show(string status = null)
        {
            if (!_initialized)
            {
                return;
            }
            // 可选的断线原因文案（先设置再显示，避免状态栏闪旧文案）
            if (status != null)
            {
                SetStatus(status);
            }
            // 显示主面板 + 隐藏对话框 + 解锁鼠标（大厅界面鼠标必然可用）
            _root.SetActive(true);
            _panel.SetActive(true);
            _disconnectDialog.SetActive(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// 隐藏大厅（本地玩家就绪后由 GameBootstrap 调用，进入游戏）。
        /// </summary>
        public void Hide()
        {
            if (!_initialized)
            {
                return;
            }
            _root.SetActive(false);
        }

        /// <summary>状态栏文案（连接状态/提示信息）。</summary>
        /// <param name="text">文案内容</param>
        public void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

        // ==================== 工具方法 ====================

        /// <summary>读取端口输入（非法输入回退默认端口 12305）。</summary>
        private int ReadPort()
        {
            return int.TryParse(_portField.text, out int port) ? port : 12305;
        }

        /// <summary>隐藏主机断开对话框（点创建/加入房间时清掉上次的遗留状态）。</summary>
        private void HideDisconnectDialog()
        {
            if (_disconnectDialog != null)
            {
                _disconnectDialog.SetActive(false);
            }
        }
    }

    // ==================== 设计笔记 ====================
    // 一个输入框两种语义（ApplyModeUI）：
    // 局域网模式填「主机 IP」、Steam 模式填「大厅 ID」——用同一个 _ipField 切换文案/尺寸/对齐，
    // 并在切换时暂存/还原 IP 文本（_utpIpText），避免切模式丢失用户输入。
    //
    // 主机断开流程的两条路径（都会走到 ForceExitToLobby）：
    // 1) 玩家点「确认」→ 立即回主页面
    // 2) 5 秒倒计时（ForcedExitRoutine）→ 自动回主页面
    // 两条路径互斥：点确认会停掉倒计时，倒计时触发时玩家已无法再点确认（面板已切换）。
    //
    // ForcedExit 事件的意义：断开后玩家网络对象已被 NGO 销毁，游戏内 HUD 的体力事件源失效，
    // 必须由 GameBootstrap 统一销毁 HUD，防止残留空引用。
}

