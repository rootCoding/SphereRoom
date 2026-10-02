using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SphereRoom.UI
{
    /// <summary>
    /// 大厅菜单：创建房间（主机）/ 加入房间（IP 直连）+ 状态提示。
    /// 主机断开时弹出对话框（文本「主机玩家已断开连接」+「确认」按钮）：
    ///   点击「确认」→ 立即回到大厅主页面；
    ///   不点击 → 5 秒后自动回到大厅主页面。
    /// 强制退出时触发 ForcedExit 事件，由 GameBootstrap 销毁游戏内 HUD。
    /// UI 结构与引用由 LobbyMenuBuilder 运行时构建并通过 Initialize 注入。
    /// </summary>
    public class LobbyMenu : MonoBehaviour
    {
        /// <summary>主机断开后回到大厅主页面时触发（GameBootstrap 借此销毁 HUD）。</summary>
        public event Action ForcedExit;

        /// <summary>点击「创建房间」（参数：端口）。</summary>
        public event Action<int> OnHostClicked;

        /// <summary>点击「加入房间」（参数：输入的 IP 与端口）。</summary>
        public event Action<string, int> OnJoinClicked;

        /// <summary>切换联机方式（参数：是否 Steam 模式）。</summary>
        public event Action<bool> OnModeChanged;

        private GameObject _root;
        private GameObject _panel;
        private GameObject _disconnectDialog;
        private Button _hostButton;
        private Button _joinButton;
        private Button _confirmButton;
        private TMP_InputField _ipField;
        private TMP_InputField _portField;
        private TextMeshProUGUI _status;

        // 联机方式切换相关
        private Button _modeButton;
        private TextMeshProUGUI _modeButtonLabel;
        private TextMeshProUGUI _ipLabel;
        private TextMeshProUGUI _ipPlaceholder;
        private GameObject _portLabelGo;
        private TextMeshProUGUI _hostButtonLabel;
        private TextMeshProUGUI _joinButtonLabel;
        private bool _steamMode;
        private string _utpIpText = "127.0.0.1";

        private Coroutine _forcedExitCoroutine;
        private bool _initialized;

        public void Initialize(GameObject root, GameObject panel, GameObject disconnectDialog,
            Button hostButton, Button joinButton, Button confirmButton,
            TMP_InputField ipField, TMP_InputField portField, TextMeshProUGUI status,
            Button modeButton, TextMeshProUGUI modeButtonLabel,
            TextMeshProUGUI ipLabel, TextMeshProUGUI ipPlaceholder, GameObject portLabelGo,
            TextMeshProUGUI hostButtonLabel, TextMeshProUGUI joinButtonLabel)
        {
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

            _hostButton.onClick.AddListener(() =>
            {
                Debug.Log("[LobbyMenu] 创建房间按钮被点击");
                SetStatus("正在创建房间…");
                HideDisconnectDialog();
                OnHostClicked?.Invoke(ReadPort());
            });
            _joinButton.onClick.AddListener(() =>
            {
                Debug.Log("[LobbyMenu] 加入房间按钮被点击");
                SetStatus("正在加入房间…");
                HideDisconnectDialog();
                OnJoinClicked?.Invoke(_ipField.text, ReadPort());
            });
            _confirmButton.onClick.AddListener(() =>
            {
                Debug.Log("[LobbyMenu] 确认按钮被点击，立即回到主页面");
                ForceExitToLobby();
            });
            _modeButton.onClick.AddListener(() =>
            {
                Debug.Log($"[LobbyMenu] 联机方式切换：{(_steamMode ? "Steam → 局域网" : "局域网 → Steam")}");
                _steamMode = !_steamMode;
                ApplyModeUI();
                OnModeChanged?.Invoke(_steamMode);
            });

            ApplyModeUI();
            Show();
        }

        /// <summary>按当前联机方式刷新大厅 UI（Steam 模式隐藏端口、输入框变大厅 ID）。</summary>
        private void ApplyModeUI()
        {
            _modeButtonLabel.text = _steamMode ? "联机方式：Steam 联机" : "联机方式：局域网直连";
            _ipLabel.text = _steamMode ? "大厅 ID" : "主机 IP";
            _portLabelGo.SetActive(!_steamMode);
            _portField.gameObject.SetActive(!_steamMode);
            _hostButtonLabel.text = _steamMode ? "创建房间（Steam）" : "创建房间";
            _joinButtonLabel.text = _steamMode ? "加入房间（Steam）" : "加入房间";
            if (_steamMode)
            {
                // 暂存 IP 文本，切回局域网时还原
                _utpIpText = _ipField.text;
                _ipField.text = string.Empty;
                _ipPlaceholder.text = "输入大厅 ID（19 位数字）";
            }
            else
            {
                _ipField.text = string.IsNullOrWhiteSpace(_utpIpText) ? "127.0.0.1" : _utpIpText;
                _ipPlaceholder.text = "例如 127.0.0.1";
            }
        }

        /// <summary>程序化切换联机方式（接受 Steam 好友邀请时由 GameBootstrap 调用），不触发 OnModeChanged。</summary>
        public void SetSteamMode(bool steam)
        {
            if (_steamMode == steam)
            {
                return;
            }
            _steamMode = steam;
            ApplyModeUI();
        }

        /// <summary>主机断开：弹出对话框；点「确认」立即回主页面，不点 5 秒后自动回。</summary>
        public void ShowHostDisconnected()
        {
            if (!_initialized)
            {
                return;
            }

            _root.SetActive(true);
            _panel.SetActive(false);
            _disconnectDialog.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

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

        /// <summary>强制退出房间，回到大厅主页面（点确认或 5 秒超时都会走这里）。</summary>
        private void ForceExitToLobby()
        {
            if (_forcedExitCoroutine != null)
            {
                StopCoroutine(_forcedExitCoroutine);
                _forcedExitCoroutine = null;
            }

            _disconnectDialog.SetActive(false);
            _panel.SetActive(true);
            SetStatus("主机已断开连接");
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            ForcedExit?.Invoke();
        }

        /// <summary>显示大厅并解锁鼠标（可带状态文案）。</summary>
        public void Show(string status = null)
        {
            if (!_initialized)
            {
                return;
            }
            if (status != null)
            {
                SetStatus(status);
            }
            _root.SetActive(true);
            _panel.SetActive(true);
            _disconnectDialog.SetActive(false);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>隐藏大厅（本地玩家就绪后由 GameBootstrap 调用）。</summary>
        public void Hide()
        {
            if (!_initialized)
            {
                return;
            }
            _root.SetActive(false);
        }

        public void SetStatus(string text)
        {
            if (_status != null)
            {
                _status.text = text;
            }
        }

        /// <summary>读取端口输入（非法输入回退默认端口 12305）。</summary>
        private int ReadPort()
        {
            return int.TryParse(_portField.text, out int port) ? port : 12305;
        }

        private void HideDisconnectDialog()
        {
            if (_disconnectDialog != null)
            {
                _disconnectDialog.SetActive(false);
            }
        }
    }
}
