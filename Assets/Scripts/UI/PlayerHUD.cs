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
    /// 玩家 HUD：体力槽（长度 + 数值 + 平滑插值）、准星、左下角操作说明、Esc 菜单。
    /// UI 结构与引用由 HudBuilder 在运行时构建，并通过 Initialize 注入（场景中无 UI 结构）。
    /// Esc：解锁鼠标并弹出菜单（再按 Esc 或点「继续游戏」关闭并重新锁定鼠标）。
    /// </summary>
    public class PlayerHUD : MonoBehaviour
    {
        /// <summary>点击「邀请 Steam 好友」（由 GameBootstrap 接到 NetworkBootstrap 打开 Steam 邀请面板）。</summary>
        public event Action InviteClicked;

        private static readonly Color StaminaGreen = new Color(0.3f, 0.85f, 0.4f);
        private static readonly Color StaminaYellow = new Color(0.9f, 0.8f, 0.2f);
        private static readonly Color StaminaRed = new Color(0.9f, 0.25f, 0.2f);

        private Image _staminaFill;
        private TextMeshProUGUI _staminaText;
        private StaminaSystem _stamina;
        private GameObject _crosshair;
        private GameObject _helpPanel;
        private GameObject _menuPanel;
        private Toggle _crosshairToggle;
        private Toggle _helpToggle;
        private Button _resumeButton;
        private Button _quitButton;
        private Button _inviteButton;
        private TextMeshProUGUI _lobbyIdText;
        private Button _copyButton;
        private TextMeshProUGUI _copyLabel;
        private TextMeshProUGUI _tappedText;
        private Coroutine _tappedCoroutine;
        private GameObject _noticeDialog;
        private TextMeshProUGUI _noticeText;
        private Button _noticeConfirm;
        private GameObject _friendListPanel;
        private Transform _friendRows;
        private Button _friendCloseButton;

        private const int MaxFriendRows = 8;
        private const float FriendRowHeight = 44f;
        private const float FriendRowTopY = 124f;

        private bool _initialized;
        private bool _menuOpen;
        private float _targetStamina = 1f;      // 实际体力（事件驱动）
        private float _displayedStamina = 1f;   // 显示体力（逐帧插值逼近）

        /// <summary>由 HudBuilder 在运行时调用，注入所有 UI 引用与体力系统。</summary>
        public void Initialize(Image staminaFill, TextMeshProUGUI staminaText, StaminaSystem stamina,
            GameObject crosshair, GameObject helpPanel, GameObject menuPanel,
            Toggle crosshairToggle, Toggle helpToggle, Button resumeButton, TextMeshProUGUI tappedText,
            Button quitButton, Button inviteButton = null, TextMeshProUGUI lobbyIdText = null,
            Button copyButton = null, GameObject noticeDialog = null, TextMeshProUGUI noticeText = null,
            Button noticeConfirm = null, GameObject friendListPanel = null, Transform friendRows = null,
            Button friendCloseButton = null)
        {
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
                _copyLabel = _copyButton.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            }
            _initialized = true;

            if (_stamina != null)
            {
                _stamina.Changed += OnStaminaChanged;
                OnStaminaChanged(_stamina.Normalized);
            }

            _crosshairToggle.isOn = _crosshair.activeSelf;
            _crosshairToggle.onValueChanged.AddListener(ShowCrosshair);
            _helpToggle.isOn = _helpPanel.activeSelf;
            _helpToggle.onValueChanged.AddListener(ShowHelp);
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

        /// <summary>
        /// 设置 Esc 菜单显示的 Steam 大厅 ID 纯数字文本（GameBootstrap 在本地玩家就绪时调用）。
        /// 「大厅ID：」描述是左侧独立静态文本；复制时只取这里的纯数字。
        /// </summary>
        public void SetLobbyId(string lobbyId)
        {
            if (_lobbyIdText != null)
            {
                _lobbyIdText.text = lobbyId;
            }
        }

        /// <summary>复制大厅 ID 到剪贴板，按钮变为「已复制」（下次打开菜单时恢复「复制」）。</summary>
        private void CopyLobbyId()
        {
            string id = _lobbyIdText != null ? _lobbyIdText.text : string.Empty;
            if (string.IsNullOrEmpty(id))
            {
                return;
            }
            GUIUtility.systemCopyBuffer = id;
            Debug.Log($"[PlayerHUD] 大厅 ID 已复制：{id}");
            if (_copyLabel != null)
            {
                _copyLabel.text = "已复制";
            }
        }

        private void Start()
        {
            if (!_initialized)
            {
                return;
            }
            SetMenuOpen(false);
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            if (Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                SetMenuOpen(!_menuOpen);
            }

            // 体力显示插值：指数平滑逼近目标值，画面帧率波动时长度变化依然丝滑
            _displayedStamina = Mathf.Lerp(_displayedStamina, _targetStamina,
                1f - Mathf.Exp(-10f * Time.deltaTime));
            RefreshStaminaView();
        }

        /// <summary>体力实际值变化（事件驱动），记录为目标值由插值逐帧追赶。</summary>
        private void OnStaminaChanged(float normalized)
        {
            _targetStamina = normalized;
        }

        /// <summary>刷新体力槽：长度（水平缩放）+ 绿/黄/红三阶段变色 + 0~100 数值。</summary>
        private void RefreshStaminaView()
        {
            // 左锚点 + 水平缩放实现长短变化（无精灵 Image 的 Filled 模式不生效）
            _staminaFill.rectTransform.localScale = new Vector3(_displayedStamina, 1f, 1f);
            _staminaFill.color = _displayedStamina > 0.66f ? StaminaGreen
                : _displayedStamina > 0.33f ? StaminaYellow
                : StaminaRed;
            _staminaText.text = Mathf.RoundToInt(_displayedStamina * 100f).ToString();
        }

        /// <summary>碰球提示：显示「Tapped」，约 0.8 秒后自动消失。</summary>
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

        private IEnumerator TappedRoutine()
        {
            _tappedText.gameObject.SetActive(true);
            yield return new WaitForSeconds(0.8f);
            _tappedText.gameObject.SetActive(false);
            _tappedCoroutine = null;
        }

        /// <summary>弹出居中提示对话框（内容文本 + 「确认」按钮，点击确认关闭）。</summary>
        public void ShowNotice(string text)
        {
            if (_noticeDialog == null || _noticeText == null)
            {
                return;
            }
            _noticeText.text = text;
            _noticeDialog.SetActive(true);
        }

        private void CloseNoticeDialog()
        {
            if (_noticeDialog != null)
            {
                _noticeDialog.SetActive(false);
            }
        }

        /// <summary>
        /// 弹出游戏内好友列表面板（Overlay 不可用时的邀请回退方案）：
        /// 每个在线好友一行「名字 + 邀请按钮」，点击邀请后按钮变「已邀请」并禁用。
        /// </summary>
        public void ShowFriendList(List<(string Name, ulong SteamId)> friends, Action<ulong> onInvite)
        {
            if (_friendListPanel == null || _friendRows == null)
            {
                return;
            }

            ClearFriendRows();
            if (friends == null || friends.Count == 0)
            {
                TextMeshProUGUI empty = UiFactory.CreateText(_friendRows, "Empty", "没有在线的好友", 20,
                    new Vector2(360f, FriendRowHeight), new Vector2(0f, FriendRowTopY));
                empty.color = new Color(0.7f, 0.75f, 0.8f);
            }
            else
            {
                int count = Mathf.Min(friends.Count, MaxFriendRows);
                for (int i = 0; i < count; i++)
                {
                    CreateFriendRow(friends[i], onInvite, i);
                }
            }
            _friendListPanel.SetActive(true);
        }

        private void CreateFriendRow((string Name, ulong SteamId) friend, Action<ulong> onInvite, int index)
        {
            float y = FriendRowTopY - index * FriendRowHeight;

            TextMeshProUGUI nameText = UiFactory.CreateText(_friendRows, $"Name{index}", friend.Name, 20,
                new Vector2(240f, FriendRowHeight), new Vector2(-75f, y));
            nameText.alignment = TextAlignmentOptions.MidlineLeft;
            nameText.color = new Color(0.9f, 0.93f, 0.96f);

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
            ColorBlock colors = inviteBtn.colors;
            colors.normalColor = new Color(0.25f, 0.5f, 0.35f);
            colors.highlightedColor = new Color(0.36f, 0.64f, 0.47f);
            colors.pressedColor = new Color(0.16f, 0.35f, 0.24f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            inviteBtn.colors = colors;
            TextMeshProUGUI label = UiFactory.CreateText(btnRt, "Label", "邀请", 18, new Vector2(96f, 34f), Vector2.zero);

            ulong steamId = friend.SteamId;
            inviteBtn.onClick.AddListener(() =>
            {
                onInvite?.Invoke(steamId);
                label.text = "已邀请";
                inviteBtn.interactable = false;
            });
        }

        private void ClearFriendRows()
        {
            if (_friendRows == null)
            {
                return;
            }
            for (int i = _friendRows.childCount - 1; i >= 0; i--)
            {
                Destroy(_friendRows.GetChild(i).gameObject);
            }
        }

        private void CloseFriendList()
        {
            if (_friendListPanel != null)
            {
                _friendListPanel.SetActive(false);
            }
        }

        private void SetMenuOpen(bool open)
        {
            _menuOpen = open;
            _menuPanel.SetActive(open);
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;

            // 再次打开菜单时，「已复制」按钮文案恢复为「复制」
            if (open && _copyLabel != null)
            {
                _copyLabel.text = "复制";
            }
        }

        private void ShowCrosshair(bool show)
        {
            _crosshair.SetActive(show);
        }

        private void ShowHelp(bool show)
        {
            _helpPanel.SetActive(show);
        }

        private void Resume()
        {
            SetMenuOpen(false);
        }

        /// <summary>
        /// 退出游戏：先关闭网络会话（主机关闭会通知所有客户端断开；
        /// 编辑器/MPPM 里 Application.Quit 可能不生效，关会话能保证主机真的退出），再尝试退出应用。
        /// </summary>
        private void QuitGame()
        {
            Debug.Log("[PlayerHUD] 退出游戏");
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null && networkManager.IsListening)
            {
                networkManager.Shutdown();
            }
            Application.Quit();
        }

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
}
