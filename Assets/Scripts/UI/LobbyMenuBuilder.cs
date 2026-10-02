using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

namespace SphereRoom.UI
{
    /// <summary>
    /// 运行时构建大厅菜单（Play 后首先出现）：
    /// 标题 + 服务器 IP 输入 + 端口输入（默认随机，规避编辑器端口泄漏问题）
    /// + 创建房间（主机）+ 加入房间（客户端）+ 状态提示。
    /// 无美术资源：纯色 Image + TMP 文字（同 HudBuilder 风格）。
    /// </summary>
    public static class LobbyMenuBuilder
    {
        public static LobbyMenu Build()
        {
            GameObject root = new GameObject("LobbyMenu");
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root.AddComponent<GraphicRaycaster>();

            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            }

            // ---- 面板 ----
            GameObject panel = new GameObject("Panel", typeof(RectTransform));
            panel.transform.SetParent(root.transform, false);
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = new Vector2(440f, 420f);
            panelRt.anchoredPosition = Vector2.zero;

            UiFactory.CreateImage(panelRt, "Border", new Vector2(446f, 426f), Vector2.zero,
                new Color(0.45f, 0.62f, 0.9f, 0.45f));
            UiFactory.CreateImage(panelRt, "Background", new Vector2(440f, 420f), Vector2.zero,
                new Color(0.09f, 0.1f, 0.14f, 0.97f), raycast: true);

            TextMeshProUGUI title = UiFactory.CreateText(panelRt, "Title", "球体房间", 36,
                new Vector2(300f, 50f), new Vector2(0f, 150f));
            title.color = new Color(0.88f, 0.92f, 1f);
            UiFactory.CreateImage(panelRt, "TitleUnderline", new Vector2(140f, 2f), new Vector2(0f, 118f),
                new Color(0.45f, 0.62f, 0.9f, 0.8f));

            // ---- 输入框（文字标签贴右对齐，紧挨输入框左侧）----
            TextMeshProUGUI ipLabel = UiFactory.CreateText(panelRt, "IpLabel", "主机 IP", 20,
                new Vector2(100f, 30f), new Vector2(-116f, 36f));
            ipLabel.alignment = TextAlignmentOptions.MidlineRight;
            ipLabel.color = new Color(0.8f, 0.85f, 0.9f);
            TMP_InputField ipField = CreateInput(panelRt, "IpInput", "例如 127.0.0.1", "127.0.0.1",
                new Vector2(56f, 36f), 220f);

            TextMeshProUGUI portLabel = UiFactory.CreateText(panelRt, "PortLabel", "端口", 20,
                new Vector2(100f, 30f), new Vector2(-116f, -12f));
            portLabel.alignment = TextAlignmentOptions.MidlineRight;
            portLabel.color = new Color(0.8f, 0.85f, 0.9f);
            // 固定端口 12305（便于联机测试；若提示端口被占用，重启编辑器释放即可）
            TMP_InputField portField = CreateInput(panelRt, "PortInput", "默认 12305", "12305",
                new Vector2(56f, -12f), 220f);

            // ---- 按钮 ----
            Button hostButton = CreateButton(panelRt, "HostButton", "创建房间", new Vector2(0f, -72f),
                new Color(0.25f, 0.55f, 0.95f));
            Button joinButton = CreateButton(panelRt, "JoinButton", "加入房间", new Vector2(0f, -137f),
                new Color(0.25f, 0.6f, 0.45f));

            // ---- 状态提示 ----
            TextMeshProUGUI status = UiFactory.CreateText(panelRt, "Status", "", 16,
                new Vector2(380f, 30f), new Vector2(0f, -184f));
            status.color = new Color(1f, 0.55f, 0.5f);

            LobbyMenu menu = root.AddComponent<LobbyMenu>();
            menu.Initialize(root, hostButton, joinButton, ipField, portField, status);
            return menu;
        }

        /// <summary>创建带占位文本的输入框。</summary>
        private static TMP_InputField CreateInput(Transform parent, string name, string placeholderText, string defaultText, Vector2 anchoredPos, float width)
        {
            GameObject inputGo = new GameObject(name, typeof(RectTransform));
            inputGo.SetActive(false);
            inputGo.transform.SetParent(parent, false);
            RectTransform inputRt = inputGo.GetComponent<RectTransform>();
            inputRt.anchorMin = inputRt.anchorMax = new Vector2(0.5f, 0.5f);
            inputRt.sizeDelta = new Vector2(width, 44f);
            inputRt.anchoredPosition = anchoredPos;
            Image inputBg = inputGo.AddComponent<Image>();
            inputBg.color = new Color(0f, 0f, 0f, 0.45f);

            TMP_InputField inputField = inputGo.AddComponent<TMP_InputField>();

            // 文本区（左右留边 + 裁剪遮罩）
            GameObject textArea = new GameObject("Text Area", typeof(RectTransform));
            textArea.transform.SetParent(inputRt, false);
            RectTransform textAreaRt = textArea.GetComponent<RectTransform>();
            textAreaRt.anchorMin = Vector2.zero;
            textAreaRt.anchorMax = Vector2.one;
            textAreaRt.sizeDelta = new Vector2(-16f, -8f);
            textAreaRt.anchoredPosition = new Vector2(8f, 0f);
            textArea.AddComponent<RectMask2D>();

            TextMeshProUGUI inputText = UiFactory.CreateText(textAreaRt, "Text", "", 20,
                new Vector2(100f, 30f), Vector2.zero);
            Stretch(inputText.rectTransform);
            inputText.alignment = TextAlignmentOptions.MidlineLeft;
            inputText.color = Color.white;
            inputText.enableWordWrapping = false;

            TextMeshProUGUI placeholder = UiFactory.CreateText(textAreaRt, "Placeholder", placeholderText, 20,
                new Vector2(100f, 30f), Vector2.zero);
            Stretch(placeholder.rectTransform);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            placeholder.color = new Color(1f, 1f, 1f, 0.35f);
            placeholder.enableWordWrapping = false;

            inputField.textViewport = textAreaRt;
            inputField.textComponent = inputText;
            inputField.placeholder = placeholder;
            inputField.text = defaultText;

            inputGo.SetActive(true);
            return inputField;
        }

        /// <summary>创建按钮（带悬停变亮 / 按下变暗）。</summary>
        private static Button CreateButton(Transform parent, string name, string label, Vector2 anchoredPos, Color normal)
        {
            GameObject btnGo = new GameObject(name, typeof(RectTransform));
            btnGo.transform.SetParent(parent, false);
            RectTransform btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.anchorMin = btnRt.anchorMax = new Vector2(0.5f, 0.5f);
            btnRt.sizeDelta = new Vector2(200f, 50f);
            btnRt.anchoredPosition = anchoredPos;

            Image btnBg = btnGo.AddComponent<Image>();
            btnBg.color = normal;
            Button button = btnGo.AddComponent<Button>();
            button.targetGraphic = btnBg;
            ColorBlock colors = button.colors;
            colors.normalColor = normal;
            colors.highlightedColor = Color.Lerp(normal, Color.white, 0.2f);
            colors.pressedColor = Color.Lerp(normal, Color.black, 0.25f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            UiFactory.CreateText(btnRt, "Label", label, 24, new Vector2(200f, 50f), Vector2.zero);
            return button;
        }

        /// <summary>把 RectTransform 拉伸填满文本区。</summary>
        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.sizeDelta = Vector2.zero;
            rt.anchoredPosition = Vector2.zero;
        }
    }
}
