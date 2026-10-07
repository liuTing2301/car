using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 点云管道建模（截面环连接，增量）：
    /// 激光雷达每帧给出一个 2D 截面环（角度 + 距离），小车前进时逐帧累积截面环，
    /// 相邻截面环连接成三角带，形成随移动变长、两端开口的管道网格。
    /// 这是性能最优的方案：O(n) 直接连接、增量追加、不封口、简单直观。
    /// </summary>
    public class PointCloudRebuilder : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private RobotDataReader robotDataReader;
        [SerializeField] private Transform car;

        [Header("重建参数")]
        [Tooltip("点云距离 → 场景尺寸的缩放系数。")]
        [SerializeField] private float pointScale = 1f;
        [Tooltip("重建结果所在的 Layer（用于预览相机隔离渲染，需与 UI 端保持一致）。")]
        [SerializeField] private int resultLayer = 31;

        [Header("持续重建")]
        [Tooltip("雷达传输数据时实时持续重建网格。")]
        [SerializeField] private bool autoRebuild = true;
        [Tooltip("自动重建的时间间隔（秒）。")]
        [SerializeField] private float rebuildInterval = 0.2f;

        // 累积的所有截面环（不丢弃，管道随时间变长）
        private readonly List<Vector3[]> sections = new List<Vector3[]>();
        private int sectionSize;

        // 增量网格数据
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<int> tris = new List<int>();
        private readonly List<Color> colors = new List<Color>();
        private int processedSections;

        // 每个截面环的中心、朝向、半径（用于射线拾取与缺陷定位，避免大碰撞体）
        private readonly List<Vector3> sectionCenters = new List<Vector3>();
        private readonly List<Quaternion> sectionRotations = new List<Quaternion>();
        private readonly List<float> sectionRadii = new List<float>();
        private float avgRadius = 1f;

        private GameObject resultObject;
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;
        private Mesh mesh;
        private bool hasResult;
        private float rebuildTimer;
        private bool hasRebuilt;
        private bool sectionsDirty;

        /// <summary>已累积的截面环数量。</summary>
        public int SectionCount => sections.Count;
        /// <summary>当前小车所在截面索引（最新累积的截面，用于缺陷轴向定位）。</summary>
        public int CurrentSectionIndex => sections.Count - 1;
        /// <summary>平均管道半径（世界坐标，用于调试标记缩放等）。</summary>
        public float AverageRadius => avgRadius;
        public bool HasResult => hasResult;
        /// <summary>重建结果网格（顶点为世界坐标）。</summary>
        public MeshFilter ResultMeshFilter => meshFilter;

        public void Setup(RobotDataReader reader, Transform carTransform)
        {
            if (robotDataReader != null)
            {
                robotDataReader.OnPointCloudReceived -= HandlePointCloud;
            }

            this.robotDataReader = reader;
            this.car = carTransform;

            if (robotDataReader != null)
            {
                robotDataReader.OnPointCloudReceived += HandlePointCloud;
            }
        }

        private void Awake()
        {
            resultObject = new GameObject("ReconstructedMesh");
            // 固定在场景根（世界原点），不随小车/Manager 移动；网格顶点使用世界坐标
            resultObject.transform.SetParent(null);
            meshFilter = resultObject.AddComponent<MeshFilter>();
            meshRenderer = resultObject.AddComponent<MeshRenderer>();
            meshRenderer.material = CreateMaterial();
            resultObject.layer = resultLayer;
            resultObject.SetActive(false);

            mesh = new Mesh();
            mesh.MarkDynamic();
            meshFilter.mesh = mesh;
        }

        private void OnDestroy()
        {
            if (robotDataReader != null)
            {
                robotDataReader.OnPointCloudReceived -= HandlePointCloud;
            }
        }

        private void Update()
        {
            if (!autoRebuild || sections.Count < 2 || !sectionsDirty)
            {
                return;
            }

            rebuildTimer += Time.deltaTime;
            if (!hasRebuilt || rebuildTimer >= rebuildInterval)
            {
                hasRebuilt = true;
                rebuildTimer = 0f;
                sectionsDirty = false;
                Rebuild();
            }
        }

        private void HandlePointCloud(StatusPacket pkt)
        {
            if (pkt == null || pkt.scan == null || car == null)
            {
                return;
            }

            Accumulate(pkt.scan.angles, pkt.scan.distances);
        }

        /// <summary>把一帧扫描环变换到世界坐标，作为一个截面累积。</summary>
        private void Accumulate(List<float> angles, List<float> distances)
        {
            int n = Mathf.Min(angles.Count, distances.Count);
            if (n < 3)
            {
                return;
            }

            if (sectionSize == 0)
            {
                sectionSize = n;
            }

            // 固定每截面点数：帧点数不足用 NaN 补齐、超出则截断，保证截面对齐且不重置
            Vector3[] section = new Vector3[sectionSize];

            for (int i = 0; i < sectionSize; i++)
            {
                if (i >= n)
                {
                    section[i] = new Vector3(float.NaN, float.NaN, float.NaN);
                    continue;
                }

                float dist = distances[i];
                if (float.IsNaN(dist) || dist <= 0f)
                {
                    section[i] = new Vector3(float.NaN, float.NaN, float.NaN);
                    continue;
                }

                float rad = angles[i];
                // 扫描平面垂直于小车前进方向（forward=Z），环在局部 XY 平面
                Vector3 local = new Vector3(Mathf.Sin(rad) * dist, Mathf.Cos(rad) * dist, 0f) * pointScale;
                section[i] = car.TransformPoint(local);
            }

            FillNaNs(section);

            sections.Add(section);
            sectionsDirty = true;

            // 记录截面环中心、朝向、平均半径（用于射线拾取）
            Vector3 center = Vector3.zero;
            int valid = 0;
            foreach (Vector3 p in section)
            {
                if (!float.IsNaN(p.x)) { center += p; valid++; }
            }
            float radius = 0f;
            if (valid > 0)
            {
                center /= valid;
                foreach (Vector3 p in section)
                {
                    if (!float.IsNaN(p.x)) radius += Vector3.Distance(p, center);
                }
                radius /= valid;
                if (radius > 0f) avgRadius = radius;
            }
            sectionCenters.Add(center);
            sectionRotations.Add(car != null ? car.rotation : Quaternion.identity);
            sectionRadii.Add(radius);

            if (sections.Count == 1)
            {
                Debug.Log($"[PointCloudRebuilder] 收到首个截面，每截面 {sectionSize} 点");
            }
            else if (sections.Count % 20 == 0)
            {
                Debug.Log($"[PointCloudRebuilder] 已累积 {sections.Count} 截面");
            }
        }

        /// <summary>用相邻有效点填充 NaN 缺口，保证截面环连续（首尾循环）。</summary>
        private static void FillNaNs(Vector3[] section)
        {
            int n = section.Length;

            Vector3 last = new Vector3(float.NaN, float.NaN, float.NaN);
            for (int i = 0; i < n; i++)
            {
                if (!float.IsNaN(section[i].x))
                {
                    last = section[i];
                }
                else if (!float.IsNaN(last.x))
                {
                    section[i] = last;
                }
            }

            last = new Vector3(float.NaN, float.NaN, float.NaN);
            for (int i = n - 1; i >= 0; i--)
            {
                if (!float.IsNaN(section[i].x))
                {
                    last = section[i];
                }
                else if (!float.IsNaN(last.x))
                {
                    section[i] = last;
                }
            }
        }

        /// <summary>增量重建：只追加新增截面，保留所有历史。</summary>
        public Mesh Rebuild()
        {
            while (processedSections < sections.Count)
            {
                AppendSection(processedSections);
                processedSections++;
            }

            if (tris.Count < 3)
            {
                return null;
            }

            mesh.Clear();
            mesh.indexFormat = verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            resultObject.SetActive(true);
            hasResult = true;

            Debug.Log($"[PointCloudRebuilder] 重建：{sections.Count} 截面 / {verts.Count} 顶点 / {tris.Count / 3} 三角形");
            return mesh;
        }

        /// <summary>把第 s 个截面追加到网格，并与前一个截面连接成三角带。</summary>
        private void AppendSection(int s)
        {
            Vector3[] sec = sections[s];
            int row0 = s * sectionSize;
            for (int i = 0; i < sectionSize; i++)
            {
                verts.Add(sec[i]);
                colors.Add(new Color(0.2f, 0.8f, 1f, 0.6f));
            }

            if (s >= 1)
            {
                int rowPrev = (s - 1) * sectionSize;
                for (int i = 0; i < sectionSize; i++)
                {
                    int iNext = (i + 1) % sectionSize;
                    int a = rowPrev + i;
                    int b = rowPrev + iNext;
                    int c = row0 + iNext;
                    int d = row0 + i;

                    // 跳过 NaN 顶点（理论上已填充，此处兜底）
                    if (float.IsNaN(verts[a].x) || float.IsNaN(verts[b].x) ||
                        float.IsNaN(verts[c].x) || float.IsNaN(verts[d].x))
                    {
                        continue;
                    }

                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(a); tris.Add(d); tris.Add(c);
                }
            }
        }

        /// <summary>把指定截面上、指定角度范围内的点标记为红色（用于缺陷标注）。</summary>
        public void MarkDefectAt(int sectionIndex, float angleDeg, float rangeDeg)
        {
            if (sectionSize <= 0 || sections.Count == 0)
            {
                return;
            }
            if (sectionIndex < 0 || sectionIndex >= sections.Count)
            {
                return;
            }

            // 截面环角度数组为 -π ~ π（索引 0 对应 -180°），需正确映射到角度
            int centerIdx = AngleToIndex(angleDeg);
            int halfRange = Mathf.Max(5, Mathf.RoundToInt(rangeDeg / 360f * sectionSize * 0.5f));

            for (int off = -halfRange; off <= halfRange; off++)
            {
                int idx = (centerIdx + off + sectionSize) % sectionSize;
                int vertIdx = sectionIndex * sectionSize + idx;
                if (vertIdx < colors.Count)
                {
                    colors[vertIdx] = new Color(1f, 0.05f, 0.05f, 1f);
                }
            }

            if (mesh != null)
            {
                mesh.SetColors(colors);
            }
        }

        /// <summary>把 0~360° 角度映射到截面环点索引（角度数组为 -π ~ π）。</summary>
        private int AngleToIndex(float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            if (rad > Mathf.PI)
            {
                rad -= 2f * Mathf.PI;
            }
            int idx = Mathf.RoundToInt((rad + Mathf.PI) / (2f * Mathf.PI) * (sectionSize - 1));
            idx = ((idx % sectionSize) + sectionSize) % sectionSize;
            return idx;
        }

        /// <summary>由 (截面索引, 环向角度) 反查管道壁上的世界坐标点（用于调试标记与标定）。</summary>
        public bool TryGetWorldPoint(int sectionIndex, float angleDeg, out Vector3 point)
        {
            point = Vector3.zero;
            if (sectionIndex < 0 || sectionIndex >= sectionCenters.Count)
            {
                return false;
            }
            float r = sectionRadii.Count > sectionIndex
                ? Mathf.Max(0.5f, sectionRadii[sectionIndex])
                : Mathf.Max(0.5f, avgRadius);
            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 local = new Vector3(Mathf.Sin(rad), Mathf.Cos(rad), 0f) * r;
            point = sectionCenters[sectionIndex] + sectionRotations[sectionIndex] * local;
            return true;
        }

        /// <summary>清空累积的截面与重建结果。</summary>
        private void ResetAll()
        {
            sections.Clear();
            verts.Clear();
            tris.Clear();
            colors.Clear();
            sectionCenters.Clear();
            sectionRotations.Clear();
            sectionRadii.Clear();
            avgRadius = 1f;
            processedSections = 0;
            hasResult = false;
            hasRebuilt = false;
            sectionsDirty = false;
            if (mesh != null)
            {
                mesh.Clear();
            }
            if (resultObject != null)
            {
                resultObject.SetActive(false);
            }
        }

        /// <summary>清空累积的截面与重建结果。</summary>
        public void Clear()
        {
            ResetAll();
            sectionSize = 0;
        }

        private Material CreateMaterial()
        {
            // 用支持顶点颜色的透明 shader，便于缺陷标红
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material mat = new Material(shader);
            mat.color = Color.white; // 颜色由顶点颜色决定，避免主颜色遮罩标红

            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return mat;
        }
    }
}
