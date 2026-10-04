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
    ///  ├─ MenuPanel Esc 菜单（大厅 ID 行 / 开关 / 邀请 / 继续 / 退出，初始隐藏）
    ///  ├─ NoticeDialog 提示对话框（居中，初始隐藏）
    ///  └─ FriendListPanel 好友列表面板（居中，初始隐藏，Steam 模式）
    /// 所有位置参数均为 1920×1080 参考分辨率下的像素值（CanvasScaler 缩放适配）。
    /// 布局约定：父节点统一「居中锚点」，子元素用 anchoredPosition 表达相对位置。
    /// </summary>
    public static class HudBuilder
    {
        /// <summary>
        /// 构建整套 HUD 并返回 PlayerHUD（所有引用经 Initialize 注入）。
        /// </summary>
        /// <param name="stamina">本地玩家体力系统（体力条数据源）</param>
        /// <param name="showSteamInvite">是否 Steam 联机模式（决定是否创建大厅 ID 行/邀请按钮/好友列表面板）</param>
        public static PlayerHUD Build(StaminaSystem stamina, bool showSteamInvite)
        {
            // ================= Canvas 根：屏幕空间覆盖 + 1920×1080 缩放 =================
            // HUD 与大厅菜单同用 ScreenSpaceOverlay（覆盖在游戏画面上，无 3D 相机参与）
            GameObject hudRoot = new GameObject("PlayerHUD");
            Canvas canvas = hudRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // CanvasScaler：按 1920×1080 缩放，宽高各取一半权重（不同分辨率下 UI 比例不失真）
            CanvasScaler scaler = hudRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            hudRoot.AddComponent<GraphicRaycaster>();

            // UI 事件系统：全局只允许一份（大厅已创建过就不重复建）
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                // 新版 Input System 需要 InputSystemUIInputModule 处理 UI 点击
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }

            // ================= 体力槽（左上角） =================
            // 结构：底条（半透明黑）→ 填充条（水平缩放）→ 数值文本（居中）
            // 填充条的缩放由 PlayerHUD 每帧驱动（targetStamina 事件 + 显示值插值）
            GameObject staminaBar = new GameObject("StaminaBar", typeof(RectTransform));
            staminaBar.transform.SetParent(hudRoot.transform, false);
            RectTransform barRt = staminaBar.GetComponent<RectTransform>();
            // 左上角锚点 + 左上角枢轴：位置就是「距屏幕左上角的偏移」
            barRt.anchorMin = barRt.anchorMax = new Vector2(0f, 1f);
            barRt.pivot = new Vector2(0f, 1f);
            barRt.sizeDelta = new Vector2(240f, 22f);
            barRt.anchoredPosition = new Vector2(24f, -24f);
            // 底条：无交互（raycast 关闭）
            Image barBg = staminaBar.AddComponent<Image>();
            barBg.color = new Color(0f, 0f, 0f, 0.5f);   // 半透明黑底

            // 填充条：无精灵的 Image 用 Filled 类型时 fillAmount 不生效，
            // 采用「左锚点 + 水平缩放」实现长短变化（缩放由 PlayerHUD 每帧驱动）
            Image staminaFill = UiFactory.CreateImage(barRt, "Fill", new Vector2(232f, 14f), Vector2.zero,
                new Color(0.3f, 0.85f, 0.4f));
            RectTransform fillRt = staminaFill.rectTransform;
            // 锚定左中 + 左中枢轴：水平缩放时只向右伸长（左边固定）
            fillRt.anchorMin = fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = new Vector2(4f, 0f);   // 与底边留 4px 内边距

            // 体力数值（居中显示在体力槽内）
            TextMeshProUGUI staminaText = UiFactory.CreateText(barRt, "Value", "100", 16, new Vector2(60f, 22f), Vector2.zero);

            // ================= 准星：中心 4 条短线（中央留空）+ 中点 =================
            // 锚定屏幕中心：短线上/下/左/右各偏移 10px，中央空出（瞄准目标不被遮挡）
            // Esc 菜单里可开关（PlayerHUD 接 Toggle 事件控制 SetActive）
            GameObject crosshair = new GameObject("Crosshair", typeof(RectTransform));
            crosshair.transform.SetParent(hudRoot.transform, false);
            RectTransform crosshairRt = crosshair.GetComponent<RectTransform>();
            crosshairRt.anchorMin = crosshairRt.anchorMax = new Vector2(0.5f, 0.5f);
            crosshairRt.sizeDelta = new Vector2(40f, 40f);
            crosshairRt.anchoredPosition = Vector2.zero;

            // 上下左右四条短线向四周偏移 10px，中心空出（瞄准目标不被遮挡），再加一个 4px 中心点
            Color crossColor = new Color(1f, 1f, 1f, 0.85f);
            UiFactory.CreateImage(crosshairRt, "Bar_Top", new Vector2(3f, 16f), new Vector2(0f, 10f), crossColor);
            UiFactory.CreateImage(crosshairRt, "Bar_Bottom", new Vector2(3f, 16f), new Vector2(0f, -10f), crossColor);
            UiFactory.CreateImage(crosshairRt, "Bar_Left", new Vector2(16f, 3f), new Vector2(-10f, 0f), crossColor);
            UiFactory.CreateImage(crosshairRt, "Bar_Right", new Vector2(16f, 3f), new Vector2(10f, 0f), crossColor);
            UiFactory.CreateImage(crosshairRt, "Dot", new Vector2(4f, 4f), Vector2.zero, crossColor);

            // 碰球提示「Tapped」（屏幕上方，初始隐藏，触球时短暂显示）
            // 由 BallPusher 触发 PlayerHUD.ShowTapped 显示（0.8 秒后自动隐藏）
            TextMeshProUGUI tappedText = UiFactory.CreateText(hudRoot.transform, "Tapped", "Tapped", 40,
                new Vector2(300f, 60f), new Vector2(0f, -140f));
            tappedText.color = new Color(1f, 0.85f, 0.3f);
            tappedText.gameObject.SetActive(false);

            // ================= 操作说明（左下角）：WASD + Shift + Esc =================
            GameObject helpPanel = new GameObject("HelpPanel", typeof(RectTransform));
            helpPanel.transform.SetParent(hudRoot.transform, false);
            RectTransform helpRt = helpPanel.GetComponent<RectTransform>();
            // 左下角锚点：面板尺寸固定，行内容从顶部往下排
            helpRt.anchorMin = helpRt.anchorMax = new Vector2(0f, 0f);
            helpRt.pivot = new Vector2(0f, 0f);
            helpRt.sizeDelta = new Vector2(240f, 240f);
            helpRt.anchoredPosition = new Vector2(30f, 30f);

            // 六行操作说明（index 决定从上往下的排列位置）
            CreateKeyHint(helpRt, "W", "前进", 0);
            CreateKeyHint(helpRt, "A", "左移", 1);
            CreateKeyHint(helpRt, "S", "后退", 2);
            CreateKeyHint(helpRt, "D", "右移", 3);
            CreateKeyHint(helpRt, "Shift", "加速", 4);
            CreateKeyHint(helpRt, "Esc", "菜单设置", 5);

            // ================= Esc 菜单（初始隐藏） =================
            // 布局（从上往下）：标题 → 大厅 ID 行（仅 Steam）→ 准星开关 → 操作说明开关
            //                → 邀请按钮（仅 Steam）→ 继续游戏 → 退出游戏
            GameObject menu = new GameObject("MenuPanel", typeof(RectTransform));
            menu.transform.SetParent(hudRoot.transform, false);
            RectTransform menuRt = menu.GetComponent<RectTransform>();
            menuRt.anchorMin = menuRt.anchorMax = new Vector2(0.5f, 0.5f);
            menuRt.sizeDelta = new Vector2(380f, 470f);
            menuRt.anchoredPosition = Vector2.zero;

            // 描边底 + 面板底（先创建的在下层）：外框比内底大一圈，露出边缘形成描边效果
            UiFactory.CreateImage(menuRt, "Border", new Vector2(386f, 476f), Vector2.zero,
                new Color(0.45f, 0.62f, 0.9f, 0.45f));
            // 面板底带 raycast：拦截点击，防止穿透点到游戏画面
            UiFactory.CreateImage(menuRt, "Background", new Vector2(380f, 470f), Vector2.zero,
                new Color(0.09f, 0.1f, 0.14f, 0.97f), raycast: true);

            // 标题 + 蓝色下划线
            TextMeshProUGUI title = UiFactory.CreateText(menuRt, "Title", "菜单", 32, new Vector2(200f, 44f), new Vector2(0f, 150f));
            title.color = new Color(0.88f, 0.92f, 1f);
            UiFactory.CreateImage(menuRt, "TitleUnderline", new Vector2(120f, 2f), new Vector2(0f, 126f),
                new Color(0.45f, 0.62f, 0.9f, 0.8f));

            // 准星 / 操作说明两个显示开关（勾选框 + 标签）
            Toggle crosshairToggle = CreateMenuToggle(menuRt, "CrosshairToggle", "准星", 32f);
            Toggle helpToggle = CreateMenuToggle(menuRt, "HelpToggle", "操作说明", -26f);

            // ---- 继续游戏按钮（蓝色系，悬停变亮 / 按下变暗）----
            // 手工构建（不用 CreateButton）：HUD 菜单按钮配色独立于大厅，便于单独微调
            // 位置在邀请按钮下方（两按钮垂直相邻，操作动线从上往下）
            GameObject btnGo = new GameObject("ResumeButton", typeof(RectTransform));
            btnGo.transform.SetParent(menuRt, false);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.sizeDelta = new Vector2(200f, 46f);
            btnRt.anchoredPosition = new Vector2(0f, -140f);
            Image btnBg = btnGo.AddComponent<Image>();
            btnBg.color = new Color(0.25f, 0.55f, 0.95f);
            Button button = btnGo.AddComponent<Button>();
            button.targetGraphic = btnBg;   // 按钮状态变化时 Unity 会改 targetGraphic 的颜色
            // ColorBlock 四态：普通 / 悬停亮 / 按下暗 / 选中（同悬停）
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.25f, 0.55f, 0.95f);
            colors.highlightedColor = new Color(0.38f, 0.66f, 1f);
            colors.pressedColor = new Color(0.16f, 0.4f, 0.75f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            UiFactory.CreateText(btnRt, "Label", "继续游戏", 24, new Vector2(200f, 46f), Vector2.zero);

            // ---- 退出游戏按钮（红色系）----
            // 红色系暗示「离开」的危险动作（与蓝色/绿色功能按钮形成语义区分）
            GameObject quitGo = new GameObject("QuitButton", typeof(RectTransform));
            quitGo.transform.SetParent(menuRt, false);
            RectTransform quitRt = quitGo.GetComponent<RectTransform>();
            quitRt.anchorMin = quitRt.anchorMax = new Vector2(0.5f, 0.5f);
            quitRt.sizeDelta = new Vector2(200f, 44f);
            quitRt.anchoredPosition = new Vector2(0f, -197f);
            Image quitBg = quitGo.AddComponent<Image>();
            quitBg.color = new Color(0.65f, 0.25f, 0.25f);
            Button quitButton = quitGo.AddComponent<Button>();
            quitButton.targetGraphic = quitBg;
            ColorBlock quitColors = quitButton.colors;
            quitColors.normalColor = new Color(0.65f, 0.25f, 0.25f);
            quitColors.highlightedColor = new Color(0.8f, 0.35f, 0.35f);
            quitColors.pressedColor = new Color(0.45f, 0.15f, 0.15f);
            quitColors.selectedColor = quitColors.highlightedColor;
            quitColors.fadeDuration = 0.08f;
            quitButton.colors = quitColors;
            UiFactory.CreateText(quitRt, "Label", "退出游戏", 22, new Vector2(200f, 44f), Vector2.zero);

            // 邀请 Steam 好友按钮（仅 Steam 联机模式显示，打开 Steam 好友邀请面板），位于「继续游戏」上方
            Button inviteButton = CreateSteamInviteButton(menuRt, showSteamInvite);

            // ---- 大厅 ID 行（仅 Steam 联机模式显示）----
            // 三层结构：「大厅ID」描述 + 数字展示框 + 「复制」按钮，整体居中
            // 局域网模式下这些引用保持 null，PlayerHUD 里对应逻辑自动跳过
            // 数字由 GameBootstrap 在玩家就绪后通过 SetLobbyId 写入
            TextMeshProUGUI lobbyIdText = null;
            Button copyButton = null;
            if (showSteamInvite)
            {
                // 描述文字（框外左侧，右对齐贴着数字框，与大厅页标签同风格）
                TextMeshProUGUI lobbyIdLabel = UiFactory.CreateText(menuRt, "LobbyIdLabel", "大厅ID", 14,
                    new Vector2(60f, 34f), new Vector2(-130f, 88f));
                lobbyIdLabel.alignment = TextAlignmentOptions.MidlineRight;
                lobbyIdLabel.color = new Color(0.8f, 0.85f, 0.9f);
                // 数字展示框背景（只读展示，不是输入框——玩家不能改，只能复制）
                UiFactory.CreateImage(menuRt, "LobbyIdBg", new Vector2(162f, 34f), new Vector2(-13f, 88f),
                    new Color(0f, 0f, 0f, 0.45f));
                // 纯数字文本（复制时只复制这里的内容，不带「大厅ID」前缀）
                lobbyIdText = UiFactory.CreateText(menuRt, "LobbyIdText", "", 14,
                    new Vector2(146f, 34f), new Vector2(-13f, 88f));
                lobbyIdText.alignment = TextAlignmentOptions.MidlineLeft;
                lobbyIdText.color = new Color(0.9f, 0.93f, 0.96f);
                copyButton = CreateCopyButton(menuRt, new Vector2(118f, 88f));
            }

            // 菜单初始隐藏（按 Esc 才显示）
            menu.SetActive(false);

            // ================= 提示对话框（居中，初始隐藏） =================
            // 用途：邀请面板打不开等系统提示，玩家点「确认」关闭（ShowNotice 弹出）
            // 挂在 HUD 根而不是菜单面板下：菜单关闭时对话框也能独立显示
            GameObject noticeDialog = new GameObject("NoticeDialog", typeof(RectTransform));
            noticeDialog.transform.SetParent(hudRoot.transform, false);
            RectTransform noticeRt = noticeDialog.GetComponent<RectTransform>();
            noticeRt.anchorMin = noticeRt.anchorMax = new Vector2(0.5f, 0.5f);
            noticeRt.sizeDelta = new Vector2(560f, 200f);
            noticeRt.anchoredPosition = Vector2.zero;

            // 红色系描边 + 深色底（提示/警告性质）
            UiFactory.CreateImage(noticeRt, "Border", new Vector2(566f, 206f), Vector2.zero,
                new Color(0.8f, 0.4f, 0.35f, 0.6f));
            // 背景带 raycast：挡住后面的菜单，防止误点
            UiFactory.CreateImage(noticeRt, "Background", new Vector2(560f, 200f), Vector2.zero,
                new Color(0.09f, 0.1f, 0.14f, 0.97f), raycast: true);

            // 提示文本：居中、开启换行（文案可能带 \n 手动换行，自动换行兜底）
            TextMeshProUGUI noticeText = UiFactory.CreateText(noticeRt, "Text", "", 20,
                new Vector2(520f, 100f), new Vector2(0f, 18f));
            noticeText.alignment = TextAlignmentOptions.Midline;
            noticeText.enableWordWrapping = true;
            noticeText.color = new Color(1f, 0.75f, 0.65f);

            // 「确认」按钮（蓝色系，与大厅确认按钮一致）
            GameObject confirmGo = new GameObject("NoticeConfirmButton", typeof(RectTransform));
            confirmGo.transform.SetParent(noticeRt, false);
            RectTransform confirmRt = confirmGo.GetComponent<RectTransform>();
            confirmRt.anchorMin = confirmRt.anchorMax = new Vector2(0.5f, 0.5f);
            confirmRt.sizeDelta = new Vector2(140f, 44f);
            confirmRt.anchoredPosition = new Vector2(0f, -58f);
            Image confirmBg = confirmGo.AddComponent<Image>();
            confirmBg.color = new Color(0.25f, 0.55f, 0.95f);
            Button noticeConfirm = confirmGo.AddComponent<Button>();
            noticeConfirm.targetGraphic = confirmBg;
            ColorBlock confirmColors = noticeConfirm.colors;
            confirmColors.normalColor = new Color(0.25f, 0.55f, 0.95f);
            confirmColors.highlightedColor = new Color(0.38f, 0.66f, 1f);
            confirmColors.pressedColor = new Color(0.16f, 0.4f, 0.75f);
            confirmColors.selectedColor = confirmColors.highlightedColor;
            confirmColors.fadeDuration = 0.08f;
            noticeConfirm.colors = confirmColors;
            UiFactory.CreateText(confirmRt, "Label", "确认", 22, new Vector2(140f, 44f), Vector2.zero);

            noticeDialog.SetActive(false);

            // ================= 好友列表面板（Overlay 回退方案，初始隐藏） =================
            // 用途：Steam 游戏内界面（Overlay）注入失败时，用游戏内好友列表代替官方邀请面板
            // 行内容是动态生成的（PlayerHUD.ShowFriendList），本段只搭面板骨架
            GameObject friendListPanel = new GameObject("FriendListPanel", typeof(RectTransform));
            friendListPanel.transform.SetParent(hudRoot.transform, false);
            RectTransform friendRt = friendListPanel.GetComponent<RectTransform>();
            friendRt.anchorMin = friendRt.anchorMax = new Vector2(0.5f, 0.5f);
            friendRt.sizeDelta = new Vector2(420f, 540f);
            friendRt.anchoredPosition = Vector2.zero;

            // 蓝色描边 + 深色底（常规面板配色）
            UiFactory.CreateImage(friendRt, "Border", new Vector2(426f, 546f), Vector2.zero,
                new Color(0.45f, 0.62f, 0.9f, 0.45f));
            UiFactory.CreateImage(friendRt, "Background", new Vector2(420f, 540f), Vector2.zero,
                new Color(0.09f, 0.1f, 0.14f, 0.97f), raycast: true);

            // 面板标题
            TextMeshProUGUI friendTitle = UiFactory.CreateText(friendRt, "Title", "邀请 Steam 好友", 26,
                new Vector2(300f, 40f), new Vector2(0f, 190f));
            friendTitle.color = new Color(0.88f, 0.92f, 1f);

            // 好友行容器（行由 PlayerHUD 按需动态生成，这里只给一个空容器）
            // 行内容每次打开面板时重建：好友列表可能变化，且避免重复打开积累旧行
            // 容器与面板同尺寸同锚点，行用 anchoredPosition 定位（y 从 124 往下每行 44）
            GameObject rowsGo = new GameObject("Rows", typeof(RectTransform));
            rowsGo.transform.SetParent(friendRt, false);
            RectTransform rowsRt = rowsGo.GetComponent<RectTransform>();
            rowsRt.anchorMin = rowsRt.anchorMax = new Vector2(0.5f, 0.5f);
            rowsRt.sizeDelta = new Vector2(420f, 540f);
            rowsRt.anchoredPosition = Vector2.zero;

            // 关闭按钮（底部居中，蓝色系与标题描边呼应）
            GameObject closeGo = new GameObject("CloseButton", typeof(RectTransform));
            closeGo.transform.SetParent(friendRt, false);
            RectTransform closeRt = closeGo.GetComponent<RectTransform>();
            closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.5f, 0.5f);
            closeRt.sizeDelta = new Vector2(140f, 44f);
            closeRt.anchoredPosition = new Vector2(0f, -232f);
            Image closeBg = closeGo.AddComponent<Image>();
            closeBg.color = new Color(0.25f, 0.55f, 0.95f);
            Button friendCloseButton = closeGo.AddComponent<Button>();
            friendCloseButton.targetGraphic = closeBg;
            ColorBlock closeColors = friendCloseButton.colors;
            closeColors.normalColor = new Color(0.25f, 0.55f, 0.95f);
            closeColors.highlightedColor = new Color(0.38f, 0.66f, 1f);
            closeColors.pressedColor = new Color(0.16f, 0.4f, 0.75f);
            closeColors.selectedColor = closeColors.highlightedColor;
            closeColors.fadeDuration = 0.08f;
            friendCloseButton.colors = closeColors;
            UiFactory.CreateText(closeRt, "Label", "关闭", 22, new Vector2(140f, 44f), Vector2.zero);

            friendListPanel.SetActive(false);

            // ================= 接线 =================
            // 把所有引用注入 PlayerHUD（Steam 附加 UI 只在 Steam 模式传，局域网模式保持 null）
            PlayerHUD hud = hudRoot.AddComponent<PlayerHUD>();
            hud.Initialize(staminaFill, staminaText, stamina, crosshair, helpPanel, menu,
                crosshairToggle, helpToggle, button, tappedText, quitButton, inviteButton,
                lobbyIdText, copyButton, noticeDialog, noticeText, noticeConfirm,
                friendListPanel, rowsRt, friendCloseButton);
            return hud;
        }

        /// <summary>
        /// 一行操作说明：【键位图标】说明文字（多字母键如 Shift 用加宽图标）。
        /// 布局：深色键位小方块（内含键名）在左，说明文字在右，每行 36px 从上往下排。
        /// </summary>
        /// <param name="parent">操作说明面板</param>
        /// <param name="key">键名（图标里显示，如 W / Shift）</param>
        /// <param name="description">说明文字（如 前进 / 加速）</param>
        /// <param name="index">行号（0 起，决定从上往下的位置）</param>
        private static void CreateKeyHint(Transform parent, string key, string description, int index)
        {
            bool wide = key.Length > 1;      // 多字母键（Shift）用 60px 宽图标，单字母用 34px
            float iconWidth = wide ? 60f : 34f;
            float y = -22f - 36f * index;    // 行位置：从容器顶部往下，每行 36px

            // 键位图标（深色小方块 + 键名）：锚定容器左上角，逐行下移
            Image icon = UiFactory.CreateImage(parent, $"Key_{key}", new Vector2(iconWidth, 28f), Vector2.zero,
                new Color(0.15f, 0.15f, 0.18f, 0.85f));
            RectTransform iconRt = icon.rectTransform;
            iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 1f);
            iconRt.pivot = new Vector2(0f, 1f);
            iconRt.anchoredPosition = new Vector2(0f, y);
            UiFactory.CreateText(iconRt, "Label", key, wide ? 14 : 16, new Vector2(iconWidth, 28f), Vector2.zero);

            // 说明文字（图标右侧 10px，左对齐）
            TextMeshProUGUI desc = UiFactory.CreateText(parent, $"Desc_{key}", description, 18, new Vector2(160f, 28f), Vector2.zero);
            RectTransform descRt = desc.rectTransform;
            descRt.anchorMin = descRt.anchorMax = new Vector2(0f, 1f);
            descRt.pivot = new Vector2(0f, 1f);
            descRt.anchoredPosition = new Vector2(iconWidth + 10f, y);
            desc.alignment = TextAlignmentOptions.MidlineLeft;
        }

        /// <summary>
        /// 「邀请 Steam 好友」按钮（绿色系，Steam 模式才激活，位于「继续游戏」上方）。
        /// 点击行为不在这里：按钮只发出 PlayerHUD.InviteClicked 事件，邀请逻辑由 GameBootstrap 接线。
        /// </summary>
        /// <param name="parent">菜单面板</param>
        /// <param name="showSteamInvite">是否 Steam 模式（false 时按钮整体隐藏）</param>
        private static Button CreateSteamInviteButton(Transform parent, bool showSteamInvite)
        {
            GameObject inviteGo = new GameObject("SteamInviteButton", typeof(RectTransform));
            inviteGo.transform.SetParent(parent, false);
            RectTransform inviteRt = inviteGo.GetComponent<RectTransform>();
            inviteRt.anchorMin = inviteRt.anchorMax = new Vector2(0.5f, 0.5f);
            inviteRt.sizeDelta = new Vector2(200f, 44f);
            inviteRt.anchoredPosition = new Vector2(0f, -84f);
            Image inviteBg = inviteGo.AddComponent<Image>();
            inviteBg.color = new Color(0.25f, 0.5f, 0.35f);
            Button inviteButton = inviteGo.AddComponent<Button>();
            inviteButton.targetGraphic = inviteBg;
            // 绿色系四态配色
            ColorBlock inviteColors = inviteButton.colors;
            inviteColors.normalColor = new Color(0.25f, 0.5f, 0.35f);
            inviteColors.highlightedColor = new Color(0.36f, 0.64f, 0.47f);
            inviteColors.pressedColor = new Color(0.16f, 0.35f, 0.24f);
            inviteColors.selectedColor = inviteColors.highlightedColor;
            inviteColors.fadeDuration = 0.08f;
            inviteButton.colors = inviteColors;
            UiFactory.CreateText(inviteRt, "Label", "邀请 Steam 好友", 20, new Vector2(200f, 44f), Vector2.zero);
            // 局域网模式不显示该按钮（直接隐藏整个物体）
            inviteGo.SetActive(showSteamInvite);
            return inviteButton;
        }

        /// <summary>
        /// 「复制」按钮（大厅 ID 行右侧，蓝灰色系）。
        /// 点击后文案变「已复制」且保持（Esc 菜单重开时恢复「复制」），交互逻辑在 PlayerHUD.CopyLobbyId。
        /// </summary>
        /// <param name="parent">菜单面板</param>
        /// <param name="anchoredPosition">位置（由大厅 ID 行布局决定）</param>
        private static Button CreateCopyButton(Transform parent, Vector2 anchoredPosition)
        {
            GameObject copyGo = new GameObject("CopyButton", typeof(RectTransform));
            copyGo.transform.SetParent(parent, false);
            RectTransform copyRt = copyGo.GetComponent<RectTransform>();
            copyRt.anchorMin = copyRt.anchorMax = new Vector2(0.5f, 0.5f);
            copyRt.sizeDelta = new Vector2(84f, 34f);
            copyRt.anchoredPosition = anchoredPosition;
            Image copyBg = copyGo.AddComponent<Image>();
            copyBg.color = new Color(0.3f, 0.42f, 0.6f);
            Button copyButton = copyGo.AddComponent<Button>();
            copyButton.targetGraphic = copyBg;
            // 蓝灰色系四态配色
            ColorBlock copyColors = copyButton.colors;
            copyColors.normalColor = new Color(0.3f, 0.42f, 0.6f);
            copyColors.highlightedColor = new Color(0.42f, 0.55f, 0.75f);
            copyColors.pressedColor = new Color(0.2f, 0.3f, 0.45f);
            copyColors.selectedColor = copyColors.highlightedColor;
            copyColors.fadeDuration = 0.08f;
            copyButton.colors = copyColors;
            UiFactory.CreateText(copyRt, "Label", "复制", 16, new Vector2(84f, 34f), Vector2.zero);
            return copyButton;
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

            // UGUI Toggle 结构：Background（勾选框底，拦截点击）+ Checkmark（勾选时显示的子物体）
            // 纯色实现：底为半透明白方块，勾选标记为绿色实心小方块（Toggle 勾选时自动显示/隐藏 graphic）
            Toggle toggle = toggleGo.AddComponent<Toggle>();
            Image bg = UiFactory.CreateImage(toggleRt, "Background", new Vector2(26f, 26f), new Vector2(-85f, 0f),
                new Color(1f, 1f, 1f, 0.18f), raycast: true);
            Image check = UiFactory.CreateImage(bg.transform, "Checkmark", new Vector2(18f, 18f), Vector2.zero,
                new Color(0.4f, 0.9f, 0.55f));
            toggle.targetGraphic = bg;      // 状态过渡色作用在底上
            toggle.graphic = check;         // 勾选标记，isOn 时显示
            toggle.isOn = true;             // 默认开启（准星/操作说明开局可见）

            // 标签（勾选框右侧）
            TextMeshProUGUI labelText = UiFactory.CreateText(toggleRt, "Label", label, 22, new Vector2(160f, 30f),
                new Vector2(55f, 0f));
            labelText.alignment = TextAlignmentOptions.MidlineLeft;
            labelText.color = new Color(0.9f, 0.93f, 0.96f);
            return toggle;
        }
    }

    // ==================== 设计笔记 ====================
    // 为什么 HUD 全用代码构建（不用 Prefab + 场景摆放）？
    // 1. 项目无美术资源，纯色 Image + TMP 文字已足够表达全部 UI，Prefab 反而增加序列化耦合
    // 2. HUD 需要按「联机方式」动态增删（Steam 附加面板只在 Steam 模式创建），
    //    代码分支比「Prefab + 运行时开关」更直接、更不易漏关
    // 3. 所有位置参数集中在 Build() 里，改版式就是改数字，diff 一目了然
    //
    // 布局坐标系约定（重要）：
    // - Canvas 用 CanvasScaler 按 1920×1080 缩放，所有位置参数都是「参考分辨率像素」
    // - 父节点统一居中锚点（0.5, 0.5），子元素用 anchoredPosition 表达相对偏移
    // - 例外：体力槽/操作说明用「角落锚点」（左上/左下），保证不同分辨率下贴角不漂移
    //
    // 层级顺序 = 创建顺序：先创建的渲染在下层（边框 > 底 > 内容的层次就是这么来的）
}

