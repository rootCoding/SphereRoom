using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace SphereRoom.UI
{
    /// <summary>
    /// UI 构建公用工厂：纯色 Image + TMP 文字（共享 SDF 动态字体资产）。
    /// 供 HudBuilder / LobbyMenuBuilder 等运行时 UI 构建使用。
    /// </summary>
    public static class UiFactory
    {
        private static TMP_FontAsset _fontAsset;

        /// <summary>创建纯色 Image（无精灵时 UGUI 默认渲染为纯色矩形，无需美术资源）。</summary>
        public static Image CreateImage(Transform parent, string name, Vector2 size, Vector2 anchoredPos, Color color, bool raycast = false)
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
        /// 创建 TMP 文字。
        /// 先禁用后激活：避免 AddComponent 在字体赋值前触发初始化。
        /// </summary>
        public static TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize, Vector2 size, Vector2 anchoredPos)
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
        public static TMP_FontAsset GetFontAsset()
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

            Debug.LogError("[UiFactory] 未能从系统解析任何可用字体（尝试了微软雅黑/黑体/宋体/雅黑繁中/Arial）。");
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
