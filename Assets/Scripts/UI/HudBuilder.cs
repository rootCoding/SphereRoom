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
    /// 无美术资源：全部用纯色 Image + TMP 文字。
    /// 文字统一使用运行时从系统字体创建的 SDF 动态字体资产（TextMeshPro），
    /// 中文按需烘焙进图集，任意 Canvas 缩放比例下都保持清晰（legacy Text 动态字体在首帧会模糊）。
    /// 结构：
    /// PlayerHUD（Canvas）
    ///  ├─ StaminaBar 体力槽（左上角，绿/黄/红三阶段 + 0~100 数值）
    ///  ├─ Crosshair 准星（中心 4 条短线 + 中点，中央留空）
    ///  ├─ HelpPanel 操作说明（左下角，WASD + Shift + Esc）
    ///  └─ MenuPanel Esc 菜单（准星/操作说明开关 + 继续游戏，初始隐藏）
    /// </summary>
    public static class HudBuilder
    {
        private static TMP_FontAsset _fontAsset;

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
            Image staminaFill = CreateImage(barRt, "Fill", new Vector2(232f, 14f), Vector2.zero,
                new Color(0.3f, 0.85f, 0.4f));
            RectTransform fillRt = staminaFill.rectTransform;
            fillRt.anchorMin = fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = new Vector2(4f, 0f);

            // 体力数值（居中显示在体力槽内）
            TextMeshProUGUI staminaText = CreateText(barRt, "Value", "100", 16, new Vector2(60f, 22f), Vector2.zero);

            // ---- 准星：中心 4 条短线（中央留空）+ 中点 ----
            GameObject crosshair = new GameObject("Crosshair", typeof(RectTransform));
            crosshair.transform.SetParent(hudRoot.transform, false);
            RectTransform crosshairRt = crosshair.GetComponent<RectTransform>();
            crosshairRt.anchorMin = crosshairRt.anchorMax = new Vector2(0.5f, 0.5f);
            crosshairRt.sizeDelta = new Vector2(40f, 40f);
            crosshairRt.anchoredPosition = Vector2.zero;

            Color crossColor = new Color(1f, 1f, 1f, 0.85f);
            CreateImage(crosshairRt, "Bar_Top", new Vector2(3f, 16f), new Vector2(0f, 10f), crossColor);
            CreateImage(crosshairRt, "Bar_Bottom", new Vector2(3f, 16f), new Vector2(0f, -10f), crossColor);
            CreateImage(crosshairRt, "Bar_Left", new Vector2(16f, 3f), new Vector2(-10f, 0f), crossColor);
            CreateImage(crosshairRt, "Bar_Right", new Vector2(16f, 3f), new Vector2(10f, 0f), crossColor);
            CreateImage(crosshairRt, "Dot", new Vector2(4f, 4f), Vector2.zero, crossColor);

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
            CreateImage(menuRt, "Border", new Vector2(366f, 286f), Vector2.zero,
                new Color(0.45f, 0.62f, 0.9f, 0.45f));
            CreateImage(menuRt, "Background", new Vector2(360f, 280f), Vector2.zero,
                new Color(0.09f, 0.1f, 0.14f, 0.97f), raycast: true);

            TextMeshProUGUI title = CreateText(menuRt, "Title", "菜单", 32, new Vector2(200f, 44f), new Vector2(0f, 108f));
            title.color = new Color(0.88f, 0.92f, 1f);
            CreateImage(menuRt, "TitleUnderline", new Vector2(120f, 2f), new Vector2(0f, 86f),
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
            CreateText(btnRt, "Label", "继续游戏", 24, new Vector2(200f, 50f), Vector2.zero);

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

            Image icon = CreateImage(parent, $"Key_{key}", new Vector2(iconWidth, 28f), Vector2.zero,
                new Color(0.15f, 0.15f, 0.18f, 0.85f));
            RectTransform iconRt = icon.rectTransform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 1f);
            iconRt.pivot = new Vector2(0f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, y);
            CreateText(iconRt, "Label", key, wide ? 14 : 16, new Vector2(iconWidth, 28f), Vector2.zero);

            TextMeshProUGUI desc = CreateText(parent, $"Desc_{key}", description, 18, new Vector2(160f, 28f), Vector2.zero);
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
            Image bg = CreateImage(toggleRt, "Background", new Vector2(26f, 26f), new Vector2(-85f, 0f),
                new Color(1f, 1f, 1f, 0.18f), raycast: true);
            Image check = CreateImage(bg.transform, "Checkmark", new Vector2(18f, 18f), Vector2.zero,
                new Color(0.4f, 0.9f, 0.55f));
            toggle.targetGraphic = bg;
            toggle.graphic = check;
            toggle.isOn = true;

            TextMeshProUGUI labelText = CreateText(toggleRt, "Label", label, 22, new Vector2(160f, 30f),
                new Vector2(55f, 0f));
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = new Color(0.9f, 0.93f, 0.96f);
            return toggle;
        }

        /// <summary>创建纯色 Image（无精灵时 UGUI 默认渲染为纯色矩形，无需美术资源）。</summary>
        private static Image CreateImage(Transform parent, string name, Vector2 size, Vector2 anchoredPos, Color color, bool raycast = false)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        /// <summary>
        /// 创建 TMP 文字（共享 SDF 动态字体资产）。
        /// 先禁用后激活：避免 AddComponent 在字体赋值前触发初始化。
        /// </summary>
        private static TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize, Vector2 size, Vector2 anchoredPos)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.font = GetFontAsset();
            text.text = content;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Midline;
            text.raycastTarget = false;

            go.SetActive(true);
            return text;
        }

        /// <summary>
        /// 共享 SDF 动态字体资产：走 TMP 官方「系统字体引用」路径——
        /// 按字体族名 + 样式名解析系统字体文件（CreateFontAsset(Font) 对动态 OS 字体不可用，
        /// 它要求字体内嵌数据），DynamicOS 模式字形按需从系统字体烘焙进图集，任意缩放清晰。
        /// 另显式指定 TMP SDF 着色器（未导入 TMP Essentials 时 TMP Settings 缺失，材质可能无着色器）。
        /// </summary>
        private static TMP_FontAsset GetFontAsset()
        {
            if (_fontAsset != null)
            {
                return _fontAsset;
            }

            string[] families = { "Microsoft YaHei", "SimHei", "SimSun", "Microsoft JhengHei", "Arial" };
            string[] styles = { "Regular", "Normal" };
            foreach (string family in families)
            {
                foreach (string style in styles)
                {
                    _fontAsset = TMP_FontAsset.CreateFontAsset(family, style, 90);
                    if (_fontAsset != null)
                    {
                        EnsureSdfShader(_fontAsset);
                        return _fontAsset;
                    }
                }
            }

            Debug.LogError("[HudBuilder] 未能从系统解析任何可用字体（尝试了微软雅黑/黑体/宋体/雅黑繁中/Arial）。");
            return null;
        }

        /// <summary>为字体资产显式指定 TMP 距离场着色器（TMP Essentials 未导入时材质会缺着色器）。</summary>
        private static void EnsureSdfShader(TMP_FontAsset fontAsset)
        {
            if (fontAsset.material == null || fontAsset.material.shader != null)
            {
                return;
            }

            Shader shader = Shader.Find("TextMeshPro/Distance Field");
            if (shader == null)
            {
                shader = Shader.Find("TextMeshPro/Mobile/Distance Field");
            }
            if (shader != null)
            {
                fontAsset.material.shader = shader;
            }
        }
    }
}
