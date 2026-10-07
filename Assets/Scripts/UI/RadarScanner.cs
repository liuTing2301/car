using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    /// <summary>
    /// 雷达扫描面板：圆形雷达盘 + 旋转扫描线 + 回波点。
    /// 预留点云接入接口 FeedPointCloud(Vector3[])，未来可直接注入点云坐标。
    /// </summary>
    public class RadarScanner : MonoBehaviour
    {
        [Header("扫描参数")]
        [SerializeField] private float sweepSpeed = 120f; // 度/秒
        [SerializeField] private int ringCount = 4;
        [SerializeField] private int crossCount = 12; // 方位线数量
        [SerializeField] private float pointLife = 2.5f;

        [Header("点云（未来接入）")]
        [Tooltip("真实雷达范围半径（米），用于把世界坐标映射到雷达盘。")]
        [SerializeField] private float worldRadius = 50f;
        [Tooltip("激光雷达最大量程（米），用于把极坐标点云映射到雷达盘。")]
        [SerializeField] private float lidarRange = 12f;
        [Tooltip("点云纹理分辨率（像素）。")]
        [SerializeField] private int cloudResolution = 256;
        [Tooltip("点云半径倍率（放大点云分布，值越大点越分散）。")]
        [SerializeField] private float radiusScale = 30f;

        private RectTransform sweep;
        private float sweepAngle;
        private readonly List<Blip> blips = new List<Blip>();
        private Texture2D blipTex;
        private Canvas canvas;
        private RawImage cloudImage;
        private Texture2D cloudTexture;
        private Color32[] cloudPixels;

        private struct Blip
        {
            public Vector2 normPos; // 归一化位置 [-1,1]
            public float age;
            public Color color;
        }

        public void Build(RectTransform parent, Canvas canvas)
        {
            this.canvas = canvas;
            float pw = parent.rect.width;
            float ph = parent.rect.height;
            // 构建时布局可能未计算，给一个保底尺寸
            if (pw <= 1f) pw = 280f;
            if (ph <= 1f) ph = 280f;
            float size = Mathf.Min(pw, ph) - 20;

            // 雷达盘容器
            GameObject diskGO = new GameObject("RadarDisk", typeof(RectTransform), typeof(Image));
            diskGO.transform.SetParent(parent, false);
            Image diskBg = diskGO.GetComponent<Image>();
            diskBg.color = new Color(0.01f, 0.05f, 0.07f, 0.9f);
            diskBg.raycastTarget = false;

            RectTransform diskRT = diskGO.GetComponent<RectTransform>();
            diskRT.anchorMin = new Vector2(0.5f, 0.5f);
            diskRT.anchorMax = new Vector2(0.5f, 0.5f);
            diskRT.pivot = new Vector2(0.5f, 0.5f);
            diskRT.anchoredPosition = Vector2.zero;
            diskRT.sizeDelta = new Vector2(size, size);

            // 同心圆 + 十字线（用一张程序化纹理）
            DrawRadarTexture(diskRT, size);

            // 扫描扇形
            GameObject sweepGO = new GameObject("Sweep", typeof(RectTransform), typeof(Image));
            sweepGO.transform.SetParent(diskGO.transform, false);
            sweep = sweepGO.GetComponent<RectTransform>();
            sweep.anchorMin = new Vector2(0.5f, 0.5f);
            sweep.anchorMax = new Vector2(0.5f, 0.5f);
            sweep.pivot = new Vector2(0.5f, 0.5f);
            sweep.anchoredPosition = Vector2.zero;
            sweep.sizeDelta = new Vector2(size, size);

            Image sweepImg = sweepGO.GetComponent<Image>();
            sweepImg.color = new Color(0f, 0.85f, 0.95f, 0.35f);
            sweepImg.sprite = CreateSweepSprite(size);
            sweepImg.raycastTarget = false;

            // 点云图层（覆盖在雷达盘上，绘制真实激光雷达点云）
            GameObject cloudGO = new GameObject("PointCloud", typeof(RectTransform), typeof(RawImage));
            cloudGO.transform.SetParent(diskGO.transform, false);
            cloudImage = cloudGO.GetComponent<RawImage>();
            cloudImage.raycastTarget = false;
            cloudImage.color = Color.white;
            RectTransform cloudRT = cloudGO.GetComponent<RectTransform>();
            cloudRT.anchorMin = new Vector2(0.5f, 0.5f);
            cloudRT.anchorMax = new Vector2(0.5f, 0.5f);
            cloudRT.pivot = new Vector2(0.5f, 0.5f);
            cloudRT.anchoredPosition = Vector2.zero;
            cloudRT.sizeDelta = new Vector2(size, size);

            cloudTexture = new Texture2D(cloudResolution, cloudResolution, TextureFormat.RGBA32, false);
            cloudPixels = new Color32[cloudResolution * cloudResolution];
            ClearCloudTexture();
            cloudImage.texture = cloudTexture;

            // 中心点
            GameObject centerGO = new GameObject("Center", typeof(RectTransform), typeof(Image));
            centerGO.transform.SetParent(diskGO.transform, false);
            Image cImg = centerGO.GetComponent<Image>();
            cImg.color = UIFactory.AccentCyan;
            cImg.raycastTarget = false;
            RectTransform cRT = centerGO.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0.5f, 0.5f);
            cRT.anchorMax = new Vector2(0.5f, 0.5f);
            cRT.pivot = new Vector2(0.5f, 0.5f);
            cRT.sizeDelta = new Vector2(6, 6);
            cImg.sprite = CreateCircleSprite(6);

            // 标题文字已由外部 AddPanelHeader 添加
        }

        private void DrawRadarTexture(RectTransform diskRT, float size)
        {
            int px = 512;
            Texture2D tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
            Color clear = new Color(0, 0, 0, 0);
            Color ring = new Color(0f, 0.85f, 0.95f, 0.35f);
            Color cross = new Color(0f, 0.6f, 0.7f, 0.25f);
            Color bg = new Color(0.01f, 0.05f, 0.07f, 0.95f);

            float center = px / 2f;
            float maxR = px / 2f;

            for (int y = 0; y < px; y++)
            {
                for (int x = 0; x < px; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);

                    Color c = bg;
                    if (r <= maxR)
                    {
                        // 同心圆
                        for (int i = 1; i <= ringCount; i++)
                        {
                            float ringR = (maxR / ringCount) * i;
                            if (Mathf.Abs(r - ringR) < 1.2f)
                            {
                                c = Color.Lerp(c, ring, 0.9f);
                                break;
                            }
                        }
                        // 十字/方位线
                        float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                        for (int k = 0; k < crossCount; k++)
                        {
                            float targetAngle = k * (360f / crossCount);
                            float diff = Mathf.Abs(Mathf.DeltaAngle(angle, targetAngle));
                            if (diff < 0.6f && r < maxR)
                            {
                                c = Color.Lerp(c, cross, 0.8f);
                                break;
                            }
                        }
                        // 外边框
                        if (Mathf.Abs(r - maxR) < 1.5f)
                        {
                            c = UIFactory.BorderCyan;
                        }
                    }
                    else
                    {
                        c = clear;
                    }
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();

            Sprite sprite = Sprite.Create(tex, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f));
            Image img = diskRT.GetComponent<Image>();
            img.sprite = sprite;
            img.color = Color.white;
        }

        private Sprite CreateSweepSprite(float size)
        {
            int px = 512;
            Texture2D tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
            float center = px / 2f;
            float maxR = px / 2f;

            for (int y = 0; y < px; y++)
            {
                for (int x = 0; x < px; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r > maxR) { tex.SetPixel(x, y, Color.clear); continue; }

                    // 0~30度的扇形（从上方开始，顺时针）
                    float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    // 让 0 度指向正右，我们要扇形从上方(-90度)开始扫到 -60度
                    float norm = Mathf.DeltaAngle(-90f, angle);
                    if (norm >= 0 && norm <= 30f)
                    {
                        float t = norm / 30f;
                        float alpha = (1f - t) * 0.5f * (1f - r / maxR);
                        tex.SetPixel(x, y, new Color(0f, 0.85f, 0.95f, alpha));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f));
        }

        private Sprite CreateCircleSprite(int size)
        {
            int px = 64;
            Texture2D tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
            float c = px / 2f;
            for (int y = 0; y < px; y++)
                for (int x = 0; x < px; x++)
                {
                    float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    tex.SetPixel(x, y, r <= c ? Color.white : Color.clear);
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, px, px), new Vector2(0.5f, 0.5f));
        }

        /// <summary>注入点云数据（世界坐标），会映射到雷达盘上。</summary>
        public void FeedPointCloud(Vector3[] points, Transform car)
        {
            if (points == null || points.Length == 0) return;
            foreach (Vector3 p in points)
            {
                Vector3 rel = car != null ? car.InverseTransformPoint(p) : p;
                // 雷达盘：右为 X+，上为 Z+（俯视），XZ 平面
                float nx = Mathf.Clamp(rel.x / worldRadius, -1f, 1f);
                float nz = Mathf.Clamp(rel.z / worldRadius, -1f, 1f);
                blips.Add(new Blip { normPos = new Vector2(nx, nz), age = 0, color = UIFactory.AccentGreen });
            }
        }

        /// <summary>注入极坐标点云（角度[弧度] + 距离[米]），映射到雷达盘（俯视：0 为前方 Z+，+π/2 为右方 X+）。</summary>
        public void FeedPolarCloud(List<float> angles, List<float> distances)
        {
            if (angles == null || distances == null || cloudTexture == null)
            {
                return;
            }

            ClearCloudTexture();

            float maxR = lidarRange > 0f ? lidarRange : 12f;
            int n = Mathf.Min(angles.Count, distances.Count);
            int res = cloudResolution;
            float center = (res - 1) * 0.5f;
            float scale = (center / maxR) * radiusScale;

            for (int i = 0; i < n; i++)
            {
                float dist = distances[i];
                // NaN（JSON 中为 null，反序列化为 0）或无效距离直接忽略
                if (float.IsNaN(dist) || dist <= 0f)
                {
                    continue;
                }

                // angles 已是弧度
                float rad = angles[i];
                float x = Mathf.Sin(rad) * dist; // 右方
                float z = Mathf.Cos(rad) * dist; // 前方

                int px = Mathf.RoundToInt(Mathf.Clamp(center + x * scale, 0f, res - 1));
                int py = Mathf.RoundToInt(Mathf.Clamp(center + z * scale, 0f, res - 1));

                // 画 2x2 小块让点更明显
                SetCloudPixel(px, py, res);
            }

            cloudTexture.SetPixels32(cloudPixels);
            cloudTexture.Apply();
        }

        private void SetCloudPixel(int cx, int cy, int res)
        {
            Color32 c = UIFactory.AccentGreen;
            for (int dy = 0; dy < 2; dy++)
            {
                for (int dx = 0; dx < 2; dx++)
                {
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x >= 0 && x < res && y >= 0 && y < res)
                    {
                        cloudPixels[y * res + x] = c;
                    }
                }
            }
        }

        private void ClearCloudTexture()
        {
            if (cloudPixels == null)
            {
                return;
            }
            System.Array.Clear(cloudPixels, 0, cloudPixels.Length);
        }

        /// <summary>添加一个模拟回波点（用于无点云时的演示）。</summary>
        public void AddMockBlip()
        {
            float a = Random.Range(0f, Mathf.PI * 2f);
            float r = Random.Range(0.1f, 0.9f);
            blips.Add(new Blip
            {
                normPos = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r),
                age = 0,
                color = Random.value > 0.5f ? UIFactory.AccentCyan : UIFactory.AccentGreen
            });
        }

        private void Update()
        {
            if (sweep == null) return;
            sweepAngle = (sweepAngle + sweepSpeed * Time.deltaTime) % 360f;
            sweep.localRotation = Quaternion.Euler(0, 0, -sweepAngle);

            // 更新回波点
            for (int i = blips.Count - 1; i >= 0; i--)
            {
                Blip b = blips[i];
                b.age += Time.deltaTime;
                blips[i] = b;
                if (b.age >= pointLife) blips.RemoveAt(i);
            }
            DrawBlips();
        }

        private void DrawBlips()
        {
            // 简单做法：每次重建点纹理（点少，性能可接受）
            // 这里用多个 UI Image 子物体太耗；改用一张纹理覆盖到雷达盘上
            // 为简洁，每个回波点用一个小 RawImage
            // 清理旧点
            Transform disk = sweep != null ? sweep.parent : null;
            if (disk == null) return;

            // 移除旧点
            for (int i = disk.childCount - 1; i >= 0; i--)
            {
                Transform ch = disk.GetChild(i);
                if (ch.name.StartsWith("Blip")) Destroy(ch.gameObject);
            }

            float diskSize = (disk as RectTransform).rect.width;
            float half = diskSize / 2f;

            if (blipTex == null)
            {
                blipTex = new Texture2D(16, 16);
                float c = 8f;
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                    {
                        float r = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                        blipTex.SetPixel(x, y, r <= 6f ? Color.white : Color.clear);
                    }
                blipTex.Apply();
            }

            foreach (Blip b in blips)
            {
                float alpha = 1f - (b.age / pointLife);
                GameObject go = new GameObject("Blip", typeof(RectTransform), typeof(RawImage));
                go.transform.SetParent(disk, false);
                RawImage ri = go.GetComponent<RawImage>();
                ri.texture = blipTex;
                ri.color = new Color(b.color.r, b.color.g, b.color.b, alpha);
                ri.raycastTarget = false;

                RectTransform rt = go.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(b.normPos.x * half, b.normPos.y * half);
                rt.sizeDelta = new Vector2(10, 10);
            }
        }
    }
}
