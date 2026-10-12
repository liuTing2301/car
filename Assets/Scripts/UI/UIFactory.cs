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
        // ===== 工业仪表盘配色（参照《管道探测小车 · 数字孪生平台》原型）=====
        /// <summary>页面底色 #171a1f：用作 3D 主相机背景与整体背景。</summary>
        public static readonly Color BgRoot = new Color(0.0902f, 0.1020f, 0.1216f, 1f);
        /// <summary>顶栏 / 底栏底色 #20242a。</summary>
        public static readonly Color BgDark = new Color(0.1255f, 0.1412f, 0.1647f, 1f);
        /// <summary>主面板底色 #22262c（surface）。</summary>
        public static readonly Color BgPanel = new Color(0.1333f, 0.1490f, 0.1725f, 1f);
        /// <summary>主分隔线 #3b4148。</summary>
        public static readonly Color BorderCyan = new Color(0.2314f, 0.2549f, 0.2824f, 1f);
        /// <summary>次分隔线 #30363e。</summary>
        public static readonly Color BorderDim = new Color(0.1882f, 0.2118f, 0.2431f, 1f);
        public static readonly Color TextPrimary = new Color(0.9137f, 0.9294f, 0.9490f, 1f);
        public static readonly Color TextDim = new Color(0.6667f, 0.7020f, 0.7490f, 1f);
        /// <summary>强调蓝 #6ea9f3。</summary>
        public static readonly Color AccentCyan = new Color(0.4314f, 0.6627f, 0.9529f, 1f);
        /// <summary>成功绿 #92c79e。</summary>
        public static readonly Color AccentGreen = new Color(0.5725f, 0.7804f, 0.6196f, 1f);
        /// <summary>警告橙 #e0b579。</summary>
        public static readonly Color AccentOrange = new Color(0.8784f, 0.7098f, 0.4745f, 1f);
        public static readonly Color AccentPurple = new Color(0.72f, 0.66f, 0.90f, 1f);
        /// <summary>主色（按钮） #2463a2。</summary>
        public static readonly Color AccentBlue = new Color(0.1412f, 0.3882f, 0.6353f, 1f);
        /// <summary>危险色 #efa09d（退出 / 删除）。</summary>
        public static readonly Color Danger = new Color(0.9373f, 0.6275f, 0.6157f, 1f);
        /// <summary>面板顶部微光（几乎不可见，仅为层次感）。</summary>
        public static readonly Color PanelGlow = new Color(0.43f, 0.66f, 0.95f, 0.05f);

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

        // ===== 圆角（用九宫格精灵实现）=====
        private static Sprite _roundSprite;
        /// <summary>圆角矩形（九宫格）精灵：所有面板 / 按钮 / 输入框共用。</summary>
        public static Sprite RoundSprite
        {
            get
            {
                if (_roundSprite == null)
                {
                    _roundSprite = CreateRoundedSprite(24, 6);
                }
                return _roundSprite;
            }
        }

        /// <summary>生成白色圆角矩形精灵（alpha 做圆角 + 抗锯齿），并设置九宫格边框。</summary>
        public static Sprite CreateRoundedSprite(int size, int radius)
        {
            int s = Mathf.Max(size, radius * 2 + 4);
            Texture2D tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.hideFlags = HideFlags.HideAndDontSave;

            Color32[] px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, radius, s - radius);
                    float cy = Mathf.Clamp(y + 0.5f, radius, s - radius);
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    float a = Mathf.Clamp01(radius - d + 0.5f);
                    px[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply();

            float b = radius + 1f;
            return Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        /// <summary>给一个 Image 应用圆角九宫格精灵。</summary>
        public static void ApplyRounded(Image img)
        {
            if (img == null)
            {
                return;
            }
            img.sprite = RoundSprite;
            img.type = Image.Type.Sliced;
        }

        /// <summary>生成竖向渐变纹理（top 亮 → bottom 透明），用于面板顶部冷光。</summary>
        public static Texture2D CreateFadeTexture(int height, Color top)
        {
            Texture2D tex = new Texture2D(1, height, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            Color[] px = new Color[height];
            for (int i = 0; i < height; i++)
            {
                float t = i / (float)(height - 1);
                px[i] = new Color(top.r, top.g, top.b, top.a * t); // 底部透明
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>创建面板（可选圆角）。</summary>
        public static RectTransform CreatePanel(Transform parent, string name, Color bg, Color border, bool rounded = true)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = false;
            if (rounded) ApplyRounded(img);

            // 加一条内边框
            GameObject borderGO = new GameObject("Border", typeof(RectTransform), typeof(Image));
            borderGO.transform.SetParent(go.transform, false);
            Image bImg = borderGO.GetComponent<Image>();
            bImg.color = border;
            bImg.raycastTarget = false;
            if (rounded) ApplyRounded(bImg);

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
            if (rounded) ApplyRounded(iImg);

            RectTransform iRT = innerGO.GetComponent<RectTransform>();
            iRT.anchorMin = Vector2.zero;
            iRT.anchorMax = Vector2.one;
            iRT.offsetMin = new Vector2(1, 1);
            iRT.offsetMax = new Vector2(-1, -1);

            // 工业仪表盘风格：干净利落，不加发光与角标
            return go.GetComponent<RectTransform>();
        }

        /// <summary>面板顶部向下淡出的青色冷光。</summary>
        public static void AddTopGlow(RectTransform panel, float height)
        {
            GameObject go = new GameObject("TopGlow", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(panel, false);

            RawImage ri = go.GetComponent<RawImage>();
            ri.texture = FadeTexture;
            ri.color = Color.white;
            ri.raycastTarget = false;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0, height);
        }

        private static Texture2D _fadeTex;
        private static Texture2D FadeTexture
        {
            get
            {
                if (_fadeTex == null)
                {
                    _fadeTex = CreateFadeTexture(64, PanelGlow);
                }
                return _fadeTex;
            }
        }

        /// <summary>面板四角 L 形装饰角标。</summary>
        public static void AddCornerBrackets(RectTransform panel, Color color)
        {
            const float len = 22f;
            const float thick = 2.5f;
            Vector2[] corners = { new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 0), new Vector2(1, 0) };
            foreach (Vector2 c in corners)
            {
                AddBracketLine(panel, c, new Vector2(len, thick), color);
                AddBracketLine(panel, c, new Vector2(thick, len), color);
            }
        }

        private static void AddBracketLine(RectTransform parent, Vector2 anchor, Vector2 size, Color color)
        {
            GameObject go = new GameObject("Bracket", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;
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
            img.color = Color.white; // 颜色交给 ColorBlock（避免与状态色叠加变暗）
            ApplyRounded(img);

            Button btn = go.AddComponent<Button>();
            ColorBlock cb = btn.colors;
            cb.normalColor = new Color(0.1647f, 0.1882f, 0.2157f, 1f);
            cb.highlightedColor = new Color(0.2039f, 0.2353f, 0.2745f, 1f);
            cb.pressedColor = new Color(0.2314f, 0.2784f, 0.3373f, 1f);
            cb.selectedColor = new Color(0.2039f, 0.2353f, 0.2745f, 1f);
            cb.disabledColor = new Color(0.1529f, 0.1725f, 0.2000f, 1f);
            cb.fadeDuration = 0.12f;
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
            img.color = new Color(0.1020f, 0.1176f, 0.1412f, 1f); // #1a1e24 input
            ApplyRounded(img);

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

        /// <summary>给面板加一个带下划线的表头。</summary>
        public static Text AddPanelHeader(RectTransform panel, string title)
        {
            GameObject headerGO = new GameObject("Header", typeof(RectTransform));
            headerGO.transform.SetParent(panel, false);

            RectTransform hRT = headerGO.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 1);
            hRT.anchorMax = new Vector2(1, 1);
            hRT.pivot = new Vector2(0.5f, 1);
            hRT.anchoredPosition = Vector2.zero;
            hRT.sizeDelta = new Vector2(0, 42);

            // 左侧强调竖条
            GameObject accentGO = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accentGO.transform.SetParent(headerGO.transform, false);
            Image aImg = accentGO.GetComponent<Image>();
            aImg.color = AccentCyan;
            aImg.raycastTarget = false;
            RectTransform aRT = accentGO.GetComponent<RectTransform>();
            aRT.anchorMin = new Vector2(0, 0.5f);
            aRT.anchorMax = new Vector2(0, 0.5f);
            aRT.pivot = new Vector2(0, 0.5f);
            aRT.anchoredPosition = new Vector2(14, 0);
            aRT.sizeDelta = new Vector2(3, 16);

            Text t = CreateText(headerGO.transform, "Title", title, 14, TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRT = t.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0, 0);
            tRT.anchorMax = new Vector2(1, 1);
            tRT.offsetMin = new Vector2(25, 0);
            tRT.offsetMax = new Vector2(-12, 0);

            // 底部 1px 分隔线
            GameObject lineGO = new GameObject("HeaderLine", typeof(RectTransform), typeof(Image));
            lineGO.transform.SetParent(headerGO.transform, false);
            Image lImg = lineGO.GetComponent<Image>();
            lImg.color = BorderCyan;
            lImg.raycastTarget = false;
            RectTransform lRT = lineGO.GetComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0, 0);
            lRT.anchorMax = new Vector2(1, 0);
            lRT.pivot = new Vector2(0.5f, 0);
            lRT.anchoredPosition = Vector2.zero;
            lRT.sizeDelta = new Vector2(0, 1);

            return t;
        }
    }
}
