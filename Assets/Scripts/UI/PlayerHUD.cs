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

        private bool _initialized;
        private bool _menuOpen;
        private float _targetStamina = 1f;      // 实际体力（事件驱动）
        private float _displayedStamina = 1f;   // 显示体力（逐帧插值逼近）

        /// <summary>由 HudBuilder 在运行时调用，注入所有 UI 引用与体力系统。</summary>
        public void Initialize(Image staminaFill, TextMeshProUGUI staminaText, StaminaSystem stamina,
            GameObject crosshair, GameObject helpPanel, GameObject menuPanel,
            Toggle crosshairToggle, Toggle helpToggle, Button resumeButton)
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

        private void SetMenuOpen(bool open)
        {
            _menuOpen = open;
            _menuPanel.SetActive(open);
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
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
        }
    }
}
