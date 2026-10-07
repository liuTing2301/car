using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    /// <summary>
    /// 数字孪生 UI 工厂：统一创建具有科幻风格的面板、文字、按钮等 uGUI 元素。
    /// 所有方法在运行时构建 UI，无需预制体。
    /// </summary>
    public static class UIFactory
    {
        // 科幻配色
        public static readonly Color BgDark = new Color(0.02f, 0.05f, 0.08f, 0.88f);
        public static readonly Color BgPanel = new Color(0.04f, 0.08f, 0.12f, 0.82f);
        public static readonly Color BorderCyan = new Color(0.0f, 0.85f, 0.95f, 0.9f);
        public static readonly Color BorderDim = new Color(0.0f, 0.6f, 0.7f, 0.4f);
        public static readonly Color TextPrimary = new Color(0.85f, 0.95f, 1f);
        public static readonly Color TextDim = new Color(0.5f, 0.65f, 0.75f);
        public static readonly Color AccentCyan = new Color(0.0f, 0.85f, 0.95f);
        public static readonly Color AccentGreen = new Color(0.2f, 1f, 0.55f);
        public static readonly Color AccentOrange = new Color(1f, 0.6f, 0.2f);

        private static Font _font;
        public static Font Font
        {
            get
            {
                if (_font == null)
                {
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }
                return _font;
            }
        }

        /// <summary>创建一个带圆角感（用多边框模拟）的面板。</summary>
        public static RectTransform CreatePanel(Transform parent, string name, Color bg, Color border)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = false;

            // 加一条内边框
            GameObject borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
            borderGO.transform.SetParent(go.transform, false);
            Image bImg = borderGO.GetComponent<Image>();
            bImg.color = border;
            bImg.raycastTarget = false;

            RectTransform bRT = borderGO.GetComponent<RectTransform>();
            bRT.anchorMin = Vector2.zero;
            bRT.anchorMax = Vector2.one;
            bRT.offsetMin = new Vector2(1, 1);
            bRT.offsetMax = new Vector2(-1, -1);

            // 边框外再套一层更细的透明遮罩，让背景只在边框内显示
            GameObject innerGO = new GameObject("Inner", typeof(RectTransform), typeof(Image));
            innerGO.transform.SetParent(borderGO.transform, false);
            Image iImg = innerGO.GetComponent<Image>();
            iImg.color = bg;
            iImg.raycastTarget = false;

            RectTransform iRT = innerGO.GetComponent<RectTransform>();
            iRT.anchorMin = Vector2.zero;
            iRT.anchorMax = Vector2.one;
            iRT.offsetMin = new Vector2(1, 1);
            iRT.offsetMax = new Vector2(-1, -1);

            return go.GetComponent<RectTransform>();
        }

        public static Text CreateText(Transform parent, string name, string content, int size,
            Color color, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Text t = go.AddComponent<Text>();
            t.font = Font;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            return t;
        }

        public static Button CreateButton(Transform parent, string name, string label, int size = 16)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = new Color(0.05f, 0.12f, 0.16f, 0.9f);

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.05f, 0.12f, 0.16f, 0.9f);
            cb.highlightedColor = new Color(0.1f, 0.3f, 0.38f, 0.95f);
            cb.pressedColor = new Color(0.0f, 0.5f, 0.6f, 1f);
            cb.selectedColor = new Color(0.0f, 0.45f, 0.55f, 1f);
            cb.fadeDuration = 0.1f;
            btn.colors = cb;

            Text t = CreateText(go.transform, "Label", label, size, TextPrimary, TextAnchor.MiddleCenter);
            RectTransform tRT = t.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = Vector2.zero;
            tRT.offsetMax = Vector2.zero;

            return btn;
        }

        /// <summary>创建单行输入框。</summary>
        public static InputField CreateInputField(Transform parent, string name, string content, int size = 14,
            InputField.ContentType contentType = InputField.ContentType.Standard)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = new Color(0.03f, 0.09f, 0.13f, 0.95f);

            InputField input = go.GetComponent<InputField>();
            input.contentType = contentType;
            input.text = content;

            // 文本显示
            GameObject textGO = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGO.transform.SetParent(go.transform, false);
            Text text = textGO.GetComponent<Text>();
            text.font = Font;
            text.fontSize = size;
            text.color = TextPrimary;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;

            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(8, 2);
            textRT.offsetMax = new Vector2(-8, -2);
            input.textComponent = text;

            // 占位文字
            GameObject phGO = new GameObject("Placeholder", typeof(RectTransform), typeof(Text));
            phGO.transform.SetParent(go.transform, false);
            Text ph = phGO.GetComponent<Text>();
            ph.font = Font;
            ph.fontSize = size;
            ph.color = TextDim;
            ph.alignment = TextAnchor.MiddleLeft;
            ph.fontStyle = FontStyle.Italic;
            ph.text = "输入...";
            ph.raycastTarget = false;

            RectTransform phRT = phGO.GetComponent<RectTransform>();
            phRT.anchorMin = Vector2.zero;
            phRT.anchorMax = Vector2.one;
            phRT.offsetMin = new Vector2(8, 2);
            phRT.offsetMax = new Vector2(-8, -2);
            input.placeholder = ph;

            return input;
        }

        /// <summary>给面板加一个带标题的表头。</summary>
        public static Text AddPanelHeader(RectTransform panel, string title)
        {
            GameObject headerGO = new GameObject("Header", typeof(RectTransform), typeof(Image));
            headerGO.transform.SetParent(panel, false);

            Image hImg = headerGO.GetComponent<Image>();
            hImg.color = new Color(0.0f, 0.4f, 0.5f, 0.5f);
            hImg.raycastTarget = false;

            RectTransform hRT = headerGO.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 1);
            hRT.anchorMax = new Vector2(1, 1);
            hRT.pivot = new Vector2(0.5f, 1);
            hRT.anchoredPosition = Vector2.zero;
            hRT.sizeDelta = new Vector2(0, 28);

            Text t = CreateText(headerGO.transform, "Title", title, 15, TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRT = t.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0, 0);
            tRT.anchorMax = new Vector2(1, 1);
            tRT.offsetMin = new Vector2(10, 0);
            tRT.offsetMax = new Vector2(-10, 0);

            return t;
        }
    }
}
