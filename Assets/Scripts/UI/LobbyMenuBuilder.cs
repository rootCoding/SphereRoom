using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

namespace SphereRoom.UI
{
    /// <summary>
    /// 【模块】运行时构建大厅菜单（Play 后首先出现）：
    /// 标题 + 主机 IP/大厅 ID 输入 + 端口输入（默认固定 12305）
    /// + 创建房间（主机）+ 加入房间（客户端）+ 状态提示 + 联机方式切换 + 主机断开对话框。
    /// 无美术资源：纯色 Image + TMP 文字（同 HudBuilder 风格）。
    ///
    /// 结构：
    /// LobbyMenu（Canvas）
    ///  ├─ Panel 主面板（标题/输入框/按钮/状态栏/联机方式按钮）
    ///  └─ DisconnectDialog 主机断开对话框（居中，初始隐藏）
    ///
    /// 布局约定：父节点「居中锚点」，子元素用 anchoredPosition 定位（1920×1080 参考分辨率）。
    /// 输入框双语义：同一输入框在局域网模式填「主机 IP」、Steam 模式填「大厅 ID」（文案/尺寸由 LobbyMenu 切换）。
    /// </summary>
    public static class LobbyMenuBuilder
    {
        /// <summary>
        /// 构建整套大厅 UI 并返回 LobbyMenu（所有引用经 Initialize 注入）。
        /// 构建顺序即层级顺序：先创建的在底层（Canvas → 面板底 → 边框 → 内容）。
        /// </summary>
        public static LobbyMenu Build()
        {
            // ---- Canvas 根 ----
            GameObject root = new GameObject("LobbyMenu");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 与 HudBuilder 相同的 1920×1080 缩放方案，保证两套 UI 在不同分辨率下观感一致
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            // 事件系统全局唯一（大厅先于 HUD 构建，这里通常就是唯一创建处；
            // HUD 构建时检测到已存在会跳过，不会重复创建导致输入冲突）
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }

            // ---- 主面板（居中）----
            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(440f, 460f);
            panelRt.anchoredPosition = Vector2.zero;

            // 描边 + 底色（同 Esc 菜单的层次做法）：外框比内底大一圈露出描边
            UiFactory.CreateImage(panelRt, "Border", new Vector2(446f, 466f), Vector2.zero,
                new Color(0.45f, 0.62f, 0.9f, 0.45f));
            UiFactory.CreateImage(panelRt, "Background", new Vector2(440f, 460f), Vector2.zero,
                new Color(0.09f, 0.1f, 0.14f, 0.97f), raycast: true);

            // 标题 + 下划线
            TextMeshProUGUI title = UiFactory.CreateText(panelRt, "Title", "球体房间", 36,
                new Vector2(300f, 50f), new Vector2(0f, 160f));
            title.color = new Color(0.88f, 0.92f, 1f);
            UiFactory.CreateImage(panelRt, "TitleUnderline", new Vector2(140f, 2f), new Vector2(0f, 128f),
                new Color(0.45f, 0.62f, 0.9f, 0.8f));

            // ---- 输入框（文字标签贴右对齐，紧挨输入框左侧）----
            // 标签右对齐到输入框左边缘：视觉上「标签 | 输入框」成组，切换模式时标签与框一起移动
            // IP 输入框在两种模式下分别承载「主机 IP」（局域网）与「大厅 ID」（Steam），
            // 由 LobbyMenu.ApplyModeUI 切换文案与尺寸
            TextMeshProUGUI ipLabel = UiFactory.CreateText(panelRt, "IpLabel", "主机 IP", 20,
                new Vector2(100f, 30f), new Vector2(-116f, 40f));
            ipLabel.alignment = TextAlignmentOptions.MidlineRight;
            ipLabel.color = new Color(0.8f, 0.85f, 0.9f);
            TMP_InputField ipField = CreateInput(panelRt, "IpInput", "例如 127.0.0.1", "127.0.0.1",
                new Vector2(56f, 40f), 220f);

            TextMeshProUGUI portLabel = UiFactory.CreateText(panelRt, "PortLabel", "端口", 20,
                new Vector2(100f, 30f), new Vector2(-116f, -8f));
            portLabel.alignment = TextAlignmentOptions.MidlineRight;
            portLabel.color = new Color(0.8f, 0.85f, 0.9f);
            // 固定端口 12305（便于联机测试；若提示端口被占用，重启编辑器释放即可）
            TMP_InputField portField = CreateInput(panelRt, "PortInput", "默认 12305", "12305",
                new Vector2(56f, -8f), 220f);

            // ---- 按钮（加宽以容纳 Steam 模式的长文案「创建房间（Steam）」）----
            // 创建按钮默认 200 宽，这里加宽到 300（约多 4 个汉字），两种模式的文案都放得下
            Button hostButton = CreateButton(panelRt, "HostButton", "创建房间", new Vector2(0f, -68f),
                new Color(0.25f, 0.55f, 0.95f));
            hostButton.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 50f);
            Button joinButton = CreateButton(panelRt, "JoinButton", "加入房间", new Vector2(0f, -133f),
                new Color(0.25f, 0.6f, 0.45f));
            joinButton.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 50f);

            // ---- 状态提示（连接状态/错误信息，淡红色醒目）----
            TextMeshProUGUI status = UiFactory.CreateText(panelRt, "Status", "", 16,
                new Vector2(380f, 30f), new Vector2(0f, -170f));
            status.color = new Color(1f, 0.55f, 0.5f);

            // ---- 联机方式切换按钮（局域网直连 ⇄ Steam 联机）----
            Button modeButton = CreateButton(panelRt, "ModeToggle", "联机方式：局域网直连", new Vector2(0f, -204f),
                new Color(0.35f, 0.38f, 0.45f));
            modeButton.GetComponent<RectTransform>().sizeDelta = new Vector2(260f, 34f);
            TextMeshProUGUI modeButtonLabel = modeButton.transform.Find("Label").GetComponent<TextMeshProUGUI>();
            modeButtonLabel.fontSize = 18;

            // ---- 主机断开对话框（居中，初始隐藏）：文本 + 「确认」按钮 ----
            GameObject dialog = new GameObject("DisconnectDialog", typeof(RectTransform));
            dialog.transform.SetParent(root.transform, false);
            RectTransform dialogRt = dialog.GetComponent<RectTransform>();
            dialogRt.anchorMin = dialogRt.anchorMax = new Vector2(0.5f, 0.5f);
            dialogRt.sizeDelta = new Vector2(460f, 220f);
            dialogRt.anchoredPosition = Vector2.zero;

            // 红色系描边 + 深色底（断线警告氛围）
            UiFactory.CreateImage(dialogRt, "Border", new Vector2(466f, 226f), Vector2.zero,
                new Color(0.8f, 0.4f, 0.35f, 0.6f));
            UiFactory.CreateImage(dialogRt, "Background", new Vector2(460f, 220f), Vector2.zero,
                new Color(0.09f, 0.1f, 0.14f, 0.97f), raycast: true);

            // 断开提示文本
            TextMeshProUGUI dialogText = UiFactory.CreateText(dialogRt, "Text", "主机玩家已断开连接", 28,
                new Vector2(420f, 50f), new Vector2(0f, 35f));
            dialogText.color = new Color(1f, 0.75f, 0.65f);

            // 「确认」按钮（蓝色系）
            Button confirmButton = CreateButton(dialogRt, "ConfirmButton", "确认", new Vector2(0f, -55f),
                new Color(0.25f, 0.55f, 0.95f));
            confirmButton.GetComponent<RectTransform>().sizeDelta = new Vector2(180f, 46f);

            dialog.SetActive(false);

            // ---- 接线：注入全部引用（按钮标签用 transform.Find 取，因为 CreateButton 里已按固定名创建）----
            LobbyMenu menu = root.AddComponent<LobbyMenu>();
            menu.Initialize(root, panel, dialog, hostButton, joinButton, confirmButton,
                ipField, portField, status,
                modeButton, modeButtonLabel,
                ipLabel, (TextMeshProUGUI)ipField.placeholder, portLabel.gameObject,
                hostButton.transform.Find("Label").GetComponent<TextMeshProUGUI>(),
                joinButton.transform.Find("Label").GetComponent<TextMeshProUGUI>());
            return menu;
        }

        /// <summary>
        /// 创建带占位文本的输入框（TMP_InputField 标准三件套结构：背景 + 文本区 + 占位文字）。
        /// 创建过程先禁用后激活：避免 AddComponent 在字体/引用赋值前触发初始化报错。
        /// </summary>
        /// <param name="parent">父节点</param>
        /// <param name="name">对象名</param>
        /// <param name="placeholderText">占位文字（输入为空时显示）</param>
        /// <param name="defaultText">默认文本</param>
        /// <param name="anchoredPos">相对父中心的位置</param>
        /// <param name="width">输入框宽度</param>
        private static TMP_InputField CreateInput(Transform parent, string name, string placeholderText, string defaultText, Vector2 anchoredPos, float width)
        {
            GameObject inputGo = new GameObject(name, typeof(RectTransform));
            inputGo.SetActive(false);
            inputGo.transform.SetParent(parent, false);
            RectTransform inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.anchorMin = inputRt.anchorMax = new Vector2(0.5f, 0.5f);
            inputRt.sizeDelta = new Vector2(width, 44f);
            inputRt.anchoredPosition = anchoredPos;
            // 输入框背景（半透明黑，与按钮区分）
            Image inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0f, 0f, 0f, 0.45f);

            TMP_InputField inputField = inputGo.AddComponent<TMP_InputField>();

            // 文本区（左右留边 + 裁剪遮罩）：长文本滚动时不会画出输入框边界。
            // 锚点拉伸填满输入框，sizeDelta 负值 = 四周内缩（左右各 8、上下各 4）
            GameObject textArea = new GameObject("Text Area", typeof(RectTransform));
            textArea.transform.SetParent(inputRt, false);
            RectTransform textAreaRt = textArea.GetComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.sizeDelta = new Vector2(-16f, -8f);
            textAreaRt.anchoredPosition = new Vector2(8f, 0f);
            // RectMask2D：超出文本区的部分被裁掉（长 IP 输入时文字滚动不溢出）
            textArea.AddComponent<RectMask2D>();

            // 输入正文（不换行，单行输入）
            TextMeshProUGUI inputText = UiFactory.CreateText(textAreaRt, "Text", "", 20,
                new Vector2(100f, 30f), Vector2.zero);
            Stretch(inputText.rectTransform);
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
            inputText.color = Color.white;
            inputText.enableWordWrapping = false;

            // 占位文字（输入为空时显示，浅色）
            TextMeshProUGUI placeholder = UiFactory.CreateText(textAreaRt, "Placeholder", placeholderText, 20,
                new Vector2(100f, 30f), Vector2.zero);
            Stretch(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.enableWordWrapping = false;

            // 三件套接线：viewport 决定文本显示区域，textComponent 是输入正文，placeholder 是占位
            inputField.textViewport = textAreaRt;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;
            inputField.text = defaultText;

            // 构建完成后激活（创建期间禁用是为了防止 AddComponent 提前初始化）
            inputGo.SetActive(true);
            return inputField;
        }

        /// <summary>
        /// 创建按钮（带悬停变亮 / 按下变暗）。所有纯色按钮的统一生成入口。
        /// </summary>
        /// <param name="parent">父节点</param>
        /// <param name="name">对象名</param>
        /// <param name="label">按钮文字（子物体名固定为 Label，外部可 Find 修改）</param>
        /// <param name="anchoredPos">相对父中心的位置</param>
        /// <param name="normal">普通态颜色（悬停/按下色由它自动派生）</param>
        private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Color normal)
        {
            GameObject btnGo = new GameObject(name, typeof(RectTransform));
            btnGo.transform.SetParent(parent, false);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.sizeDelta = new Vector2(200f, 50f);
            btnRt.anchoredPosition = anchoredPos;

            // 背景图就是按钮本体（无精灵 Image 渲染纯色）
            Image btnBg = btnGo.AddComponent<Image>();
            btnBg.color = normal;
            Button button = btnGo.AddComponent<Button>();
            button.targetGraphic = btnBg;   // 状态切换时 Unity 自动改 targetGraphic 颜色
            // ColorBlock 四态从「普通色」自动派生：悬停向白偏 20%、按下向黑偏 25%（任意配色都协调）
            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = Color.Lerp(normal, Color.white, 0.2f);
            colors.pressedColor = Color.Lerp(normal, Color.black, 0.25f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            // 按钮标签（子物体名固定为 "Label"，外部通过 transform.Find 获取）
            UiFactory.CreateText(btnRt, "Label", label, 24, new Vector2(200f, 50f), Vector2.zero);
            return button;
        }

        /// <summary>
        /// 把 RectTransform 拉伸填满文本区（用于输入框内部的文本/占位文字铺满 viewport）。
        /// 锚点四角拉开 + 边距归零 = 完全填满父区域。
        /// </summary>
        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }
    }

    // ==================== 设计笔记 ====================
    // 大厅 UI 与 HUD 的区别：
    // 大厅只在 Play 开头构建一次（GameBootstrap.Awake），常驻直到退出；
    // HUD 随玩家进出房间反复销毁重建。所以大厅的「输入框/对话框」结构可以稍复杂，
    // HUD 则刻意保持轻量（构建快、无状态残留）。
    //
    // 按钮/输入框/标签的生成方法（CreateButton/CreateInput）是大厅专用，
    // 与 HudBuilder 的同名概念刻意分离——两套 UI 的配色/尺寸需求不同，
    // 合用一个工厂反而会互相牵制（HUD 按钮 200 宽、大厅按钮 300 宽）。
}

