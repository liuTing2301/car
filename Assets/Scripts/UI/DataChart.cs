using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    /// <summary>
    /// 简易折线图：滚动显示最近 N 个数据点。
    /// 用于展示速度、距离等遥测数据趋势。
    /// </summary>
    public class DataChart : MonoBehaviour
    {
        [SerializeField] private int maxPoints = 120;
        [SerializeField] private float autoMin = 0f;
        [SerializeField] private float autoMax = 10f;
        [SerializeField] private bool autoScale = true;
        [SerializeField] private Color lineColor;

        private RectTransform chartArea;
        private Texture2D chartTex;
        private RawImage rawImage;
        private readonly Queue<float> values = new Queue<float>();
        private float currentMax = 10f;

        public void Build(RectTransform parent, Color color, float fixedMax = -1f)
        {
            lineColor = color;
            if (fixedMax > 0) { autoScale = false; autoMax = fixedMax; currentMax = fixedMax; }

            GameObject go = new GameObject("Chart", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            rawImage = go.GetComponent<RawImage>();
            rawImage.color = Color.white;
            rawImage.raycastTarget = false;

            chartArea = go.GetComponent<RectTransform>();
            chartArea.anchorMin = Vector2.zero;
            chartArea.anchorMax = Vector2.one;
            chartArea.offsetMin = new Vector2(4, 4);
            chartArea.offsetMax = new Vector2(-4, -4);

            chartTex = new Texture2D(256, 128, TextureFormat.RGBA32, false);
            chartTex.filterMode = FilterMode.Bilinear;
            rawImage.texture = chartTex;

            Redraw();
        }

        public void AddValue(float v)
        {
            values.Enqueue(v);
            if (values.Count > maxPoints) values.Dequeue();
            Redraw();
        }

        private void Redraw()
        {
            if (chartTex == null) return;
            int w = chartTex.width;
            int h = chartTex.height;

            // 背景
            Color bg = new Color(0.01f, 0.04f, 0.06f, 0.9f);
            Color grid = new Color(0f, 0.5f, 0.6f, 0.15f);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    chartTex.SetPixel(x, y, bg);

            // 网格线
            for (int i = 1; i < 5; i++)
            {
                int gy = (h * i) / 5;
                for (int x = 0; x < w; x++) chartTex.SetPixel(x, gy, grid);
            }
            for (int i = 1; i < 6; i++)
            {
                int gx = (w * i) / 6;
                for (int y = 0; y < h; y++) chartTex.SetPixel(gx, y, grid);
            }

            if (values.Count < 2) { chartTex.Apply(); return; }

            if (autoScale)
            {
                float mx = autoMax;
                foreach (float v in values) if (v > mx) mx = v;
                currentMax = Mathf.Max(mx, 0.1f);
            }

            float[] arr = System.Linq.Enumerable.ToArray(values);
            int n = arr.Length;

            // 绘制折线
            for (int i = 0; i < n - 1; i++)
            {
                float x0 = (float)i / (maxPoints - 1) * (w - 1);
                float x1 = (float)(i + 1) / (maxPoints - 1) * (w - 1);
                float y0 = (arr[i] / currentMax) * (h - 1);
                float y1 = (arr[i + 1] / currentMax) * (h - 1);
                DrawLine(x0, y0, x1, y1, lineColor);
            }

            // 最后一个点高亮
            float lx = (float)(n - 1) / (maxPoints - 1) * (w - 1);
            float ly = (arr[n - 1] / currentMax) * (h - 1);
            DrawDot(lx, ly, UIFactory.AccentCyan);

            chartTex.Apply();
        }

        private void DrawLine(float x0, float y0, float x1, float y1, Color c)
        {
            int steps = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0)));
            for (int s = 0; s <= steps; s++)
            {
                float t = s / (float)Mathf.Max(1, steps);
                int x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, t));
                int y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, t));
                if (x >= 0 && x < chartTex.width && y >= 0 && y < chartTex.height)
                    chartTex.SetPixel(x, y, c);
            }
        }

        private void DrawDot(float cx, float cy, Color c)
        {
            for (int dy = -3; dy <= 3; dy++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    if (dx * dx + dy * dy <= 9)
                    {
                        int x = Mathf.RoundToInt(cx) + dx;
                        int y = Mathf.RoundToInt(cy) + dy;
                        if (x >= 0 && x < chartTex.width && y >= 0 && y < chartTex.height)
                            chartTex.SetPixel(x, y, c);
                    }
                }
        }
    }
}
