using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;
using SphereRoom.Player;

namespace SphereRoom.UI
{
    /// <summary>
    /// 运行时构建玩家 HUD：体力槽、准星、操作说明、Esc 菜单。
    /// 无美术资源：全部用纯色 Image + TMP 文字（SDF 渲染，任意缩放清晰）。
    /// 结构：
    /// PlayerHUD（Canvas）
    ///  ├─ StaminaBar 体力槽（左上角，绿/黄/红三阶段 + 0~100 数值）
    ///  ├─ Crosshair 准星（中心 4 条短线 + 中点，中央留空）
    ///  ├─ HelpPanel 操作说明（左下角，WASD + Shift + Esc）
    ///  └─ MenuPanel Esc 菜单（准星/操作说明开关 + 继续游戏，初始隐藏）
    /// </summary>
    public static class HudBuilder
    {
        public static PlayerHUD Build(StaminaSystem stamina)
        {
            GameObject hudRoot = new GameObject("PlayerHUD");
            Canvas canvas = hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = hudRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            hudRoot.AddComponent<GraphicRaycaster>();

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                // 新版 Input System 需要 InputSystemUIInputModule 处理 UI 点击
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }

            // ---- 体力槽（左上角）----
            GameObject staminaBar = new GameObject("StaminaBar", typeof(RectTransform));
            staminaBar.transform.SetParent(hudRoot.transform, false);
            RectTransform barRt = staminaBar.GetComponent<RectTransform>();
            barRt.anchorMin = barRt.anchorMax = new Vector2(0f, 1f);
            barRt.pivot = new Vector2(0f, 1f);
            barRt.sizeDelta = new Vector2(240f, 22f);
            barRt.anchoredPosition = new Vector2(24f, -24f);
            Image barBg = staminaBar.AddComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0.5f);

            // 填充条：无精灵的 Image 用 Filled 类型时 fillAmount 不生效，
            // 采用「左锚点 + 水平缩放」实现长短变化
            Image staminaFill = UiFactory.CreateImage(barRt, "Fill", new Vector2(232f, 14f), Vector2.zero,
                new Color(0.3f, 0.85f, 0.4f));
            RectTransform fillRt = staminaFill.rectTransform;
            fillRt.anchorMin = fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = new Vector2(4f, 0f);

            // 体力数值（居中显示在体力槽内）
            TextMeshProUGUI staminaText = UiFactory.CreateText(barRt, "Value", "100", 16, new Vector2(60f, 22f), Vector2.zero);

            // ---- 准星：中心 4 条短线（中央留空）+ 中点 ----
            GameObject crosshair = new GameObject("Crosshair", typeof(RectTransform));
            crosshair.transform.SetParent(hudRoot.transform, false);
            RectTransform crosshairRt = crosshair.GetComponent<RectTransform>();
            crosshairRt.anchorMin = crosshairRt.anchorMax = new Vector2(0.5f, 0.5f);
            crosshairRt.sizeDelta = new Vector2(40f, 40f);
            crosshairRt.anchoredPosition = Vector2.zero;

            Color crossColor = new Color(1f, 1f, 1f, 0.85f);
            UiFactory.CreateImage(crosshairRt, "Bar_Top", new Vector2(3f, 16f), new Vector2(0f, 10f), crossColor);
            UiFactory.CreateImage(crosshairRt, "Bar_Bottom", new Vector2(3f, 16f), new Vector2(0f, -10f), crossColor);
            UiFactory.CreateImage(crosshairRt, "Bar_Left", new Vector2(16f, 3f), new Vector2(-10f, 0f), crossColor);
            UiFactory.CreateImage(crosshairRt, "Bar_Right", new Vector2(16f, 3f), new Vector2(10f, 0f), crossColor);
            UiFactory.CreateImage(crosshairRt, "Dot", new Vector2(4f, 4f), Vector2.zero, crossColor);

            // ---- 操作说明（左下角）：WASD + Shift + Esc ----
            GameObject helpPanel = new GameObject("HelpPanel", typeof(RectTransform));
            helpPanel.transform.SetParent(hudRoot.transform, false);
            RectTransform helpRt = helpPanel.GetComponent<RectTransform>();
            helpRt.anchorMin = helpRt.anchorMax = new Vector2(0f, 0f);
            helpRt.pivot = new Vector2(0f, 0f);
            helpRt.sizeDelta = new Vector2(240f, 240f);
            helpRt.anchoredPosition = new Vector2(30f, 30f);

            CreateKeyHint(helpRt, "W", "前进", 0);
            CreateKeyHint(helpRt, "A", "左移", 1);
            CreateKeyHint(helpRt, "S", "后退", 2);
            CreateKeyHint(helpRt, "D", "右移", 3);
            CreateKeyHint(helpRt, "Shift", "加速", 4);
            CreateKeyHint(helpRt, "Esc", "菜单设置", 5);

            // ---- Esc 菜单（初始隐藏）：中文 + 纯色层次美化（描边/下划线/按钮变色）----
            GameObject menu = new GameObject("MenuPanel", typeof(RectTransform));
            menu.transform.SetParent(hudRoot.transform, false);
            RectTransform menuRt = menu.GetComponent<RectTransform>();
            menuRt.anchorMin = menuRt.anchorMax = new Vector2(0.5f, 0.5f);
            menuRt.sizeDelta = new Vector2(360f, 280f);
            menuRt.anchoredPosition = Vector2.zero;

            // 描边底 + 面板底（先创建的在下层）
            UiFactory.CreateImage(menuRt, "Border", new Vector2(366f, 286f), Vector2.zero,
                new Color(0.45f, 0.62f, 0.9f, 0.45f));
            UiFactory.CreateImage(menuRt, "Background", new Vector2(360f, 280f), Vector2.zero,
                new Color(0.09f, 0.1f, 0.14f, 0.97f), raycast: true);

            TextMeshProUGUI title = UiFactory.CreateText(menuRt, "Title", "菜单", 32, new Vector2(200f, 44f), new Vector2(0f, 108f));
            title.color = new Color(0.88f, 0.92f, 1f);
            UiFactory.CreateImage(menuRt, "TitleUnderline", new Vector2(120f, 2f), new Vector2(0f, 86f),
                new Color(0.45f, 0.62f, 0.9f, 0.8f));

            Toggle crosshairToggle = CreateMenuToggle(menuRt, "CrosshairToggle", "准星", 32f);
            Toggle helpToggle = CreateMenuToggle(menuRt, "HelpToggle", "操作说明", -6f);

            // 继续游戏按钮（悬停变亮 / 按下变暗）
            GameObject btnGo = new GameObject("ResumeButton", typeof(RectTransform));
            btnGo.transform.SetParent(menuRt, false);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.sizeDelta = new Vector2(200f, 50f);
            btnRt.anchoredPosition = new Vector2(0f, -92f);
            Image btnBg = btnGo.AddComponent<Image>();
            btnBg.color = new Color(0.25f, 0.55f, 0.95f);
            Button button = btnGo.AddComponent<Button>();
            button.targetGraphic = btnBg;
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.25f, 0.55f, 0.95f);
            colors.highlightedColor = new Color(0.38f, 0.66f, 1f);
            colors.pressedColor = new Color(0.16f, 0.4f, 0.75f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            UiFactory.CreateText(btnRt, "Label", "继续游戏", 24, new Vector2(200f, 50f), Vector2.zero);

            menu.SetActive(false);

            // ---- 接线 ----
            PlayerHUD hud = hudRoot.AddComponent<PlayerHUD>();
            hud.Initialize(staminaFill, staminaText, stamina, crosshair, helpPanel, menu,
                crosshairToggle, helpToggle, button);
            return hud;
        }

        /// <summary>一行操作说明：【键位图标】说明文字。</summary>
        private static void CreateKeyHint(Transform parent, string key, string description, int index)
        {
            bool wide = key.Length > 1;
            float iconWidth = wide ? 60f : 34f;
            float y = -22f - 36f * index;

            Image icon = UiFactory.CreateImage(parent, $"Key_{key}", new Vector2(iconWidth, 28f), Vector2.zero,
                new Color(0.15f, 0.15f, 0.18f, 0.85f));
            RectTransform iconRt = icon.rectTransform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 1f);
            iconRt.pivot = new Vector2(0f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, y);
            UiFactory.CreateText(iconRt, "Label", key, wide ? 14 : 16, new Vector2(iconWidth, 28f), Vector2.zero);

            TextMeshProUGUI desc = UiFactory.CreateText(parent, $"Desc_{key}", description, 18, new Vector2(160f, 28f), Vector2.zero);
            RectTransform descRt = desc.rectTransform;
            descRt.anchorMin = descRt.anchorMax = new Vector2(0f, 1f);
            descRt.pivot = new Vector2(0f, 1f);
            descRt.anchoredPosition = new Vector2(iconWidth + 10f, y);
            desc.alignment = TextAlignmentOptions.MidlineLeft;
        }

        /// <summary>创建菜单开关行（勾选框 + 标签），返回 Toggle。</summary>
        private static Toggle CreateMenuToggle(Transform parent, string name, string label, float y)
        {
            GameObject toggleGo = new GameObject(name, typeof(RectTransform));
            toggleGo.transform.SetParent(parent, false);
            RectTransform toggleRt = toggleGo.GetComponent<RectTransform>();
            toggleRt.anchorMin = toggleRt.anchorMax = new Vector2(0.5f, 0.5f);
            toggleRt.sizeDelta = new Vector2(220f, 30f);
            toggleRt.anchoredPosition = new Vector2(0f, y);

            Toggle toggle = toggleGo.AddComponent<Toggle>();
            Image bg = UiFactory.CreateImage(toggleRt, "Background", new Vector2(26f, 26f), new Vector2(-85f, 0f),
                new Color(1f, 1f, 1f, 0.18f), raycast: true);
            Image check = UiFactory.CreateImage(bg.transform, "Checkmark", new Vector2(18f, 18f), Vector2.zero,
                new Color(0.4f, 0.9f, 0.55f));
            toggle.targetGraphic = bg;
            toggle.graphic = check;
            toggle.isOn = true;

            TextMeshProUGUI labelText = UiFactory.CreateText(toggleRt, "Label", label, 22, new Vector2(160f, 30f),
                new Vector2(55f, 0f));
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = new Color(0.9f, 0.93f, 0.96f);
            return toggle;
        }
    }
}
