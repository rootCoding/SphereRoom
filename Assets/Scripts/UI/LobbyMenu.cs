using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SphereRoom.UI
{
    /// <summary>
    /// 大厅菜单：创建房间（主机）/ 加入房间（IP 直连）+ 状态提示。
    /// UI 结构与引用由 LobbyMenuBuilder 运行时构建并通过 Initialize 注入。
    /// 显示时解锁鼠标；本地玩家生成就绪后由 GameBootstrap 调用 Hide。
    /// </summary>
    public class LobbyMenu : MonoBehaviour
    {
        /// <summary>点击「创建房间」（参数：端口）。</summary>
        public event Action<int> OnHostClicked;

        /// <summary>点击「加入房间」（参数：输入的 IP 与端口）。</summary>
        public event Action<string, int> OnJoinClicked;

        private GameObject _root;
        private Button _hostButton;
        private Button _joinButton;
        private TMP_InputField _ipField;
        private TMP_InputField _portField;
        private TextMeshProUGUI _status;
        private bool _initialized;

        public void Initialize(GameObject root, Button hostButton, Button joinButton,
            TMP_InputField ipField, TMP_InputField portField, TextMeshProUGUI status)
        {
            _root = root;
            _hostButton = hostButton;
            _joinButton = joinButton;
            _ipField = ipField;
            _portField = portField;
            _status = status;
            _initialized = true;

            _hostButton.onClick.AddListener(() =>
            {
                Debug.Log("[LobbyMenu] 创建房间按钮被点击");
                SetStatus("正在创建房间…");
                OnHostClicked?.Invoke(ReadPort());
            });
            _joinButton.onClick.AddListener(() =>
            {
                Debug.Log("[LobbyMenu] 加入房间按钮被点击");
                SetStatus("正在加入房间…");
                OnJoinClicked?.Invoke(_ipField.text, ReadPort());
            });

            Show();
        }

        /// <summary>读取端口输入（非法输入回退 9050）。</summary>
        private int ReadPort()
        {
            return int.TryParse(_portField.text, out int port) ? port : 9050;
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
    }
}
