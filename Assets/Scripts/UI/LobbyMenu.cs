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

        private GameObject _root;
        private GameObject _panel;
        private GameObject _disconnectDialog;
        private Button _hostButton;
        private Button _joinButton;
        private Button _confirmButton;
        private TMP_InputField _ipField;
        private TMP_InputField _portField;
        private TextMeshProUGUI _status;
        private Coroutine _forcedExitCoroutine;
        private bool _initialized;

        public void Initialize(GameObject root, GameObject panel, GameObject disconnectDialog,
            Button hostButton, Button joinButton, Button confirmButton,
            TMP_InputField ipField, TMP_InputField portField, TextMeshProUGUI status)
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

            Show();
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
