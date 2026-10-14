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
        [SerializeField] private CarController carController;

        [Header("重建参数")]
        [Tooltip("点云距离 → 场景尺寸的缩放系数。")]
        [SerializeField] private float pointScale = 1f;
        [Tooltip("重建结果所在的 Layer（用于预览相机隔离渲染，需与 UI 端保持一致）。")]
        [SerializeField] private int resultLayer = 31;
        [Tooltip("小车速度低于此值（cm/s）时不建模：只有小车在移动时才累积截面。")]
        [SerializeField] private float minSpeedToRebuild = 0.01f;

        [Header("持续重建")]
        [Tooltip("雷达传输数据时实时持续重建网格。")]
        [SerializeField] private bool autoRebuild = true;
        [Tooltip("自动重建的时间间隔（秒）。")]
        [SerializeField] private float rebuildInterval = 0.2f;

        [Header("轴向长度校正")]
        [Tooltip("小车在场景中以虚拟速度移动（实际速度 × virtualSpeedRatio），直接按车位置建管会让管道长度被放大该倍数。\n" +
                 "开启后按真实里程累积轴向位置，使重建的管道长度与实际里程一致。")]
        [SerializeField] private bool useRealAxialDistance = true;

        [Header("极坐标网格（在建模中显示，用于定位缺陷角度）")]
        [Tooltip("是否随建模显示极坐标网格。0° 方位线为黄色（管道顶部），顺时针为正。")]
        [SerializeField] private bool showPolarGrid = true;
        [Tooltip("极坐标网格外径 = 截面半径 × 该比例。")]
        [SerializeField] private float polarGridScale = 1.06f;

        private GameObject polarGridObject;
        private Transform polarGridTransform;

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
        // 是否接收实时雷达数据累积（读取本地存档时暂停，避免实时数据混入存档模型）
        private bool liveEnabled = true;

        // 轴向累积（按真实里程推进，避免虚拟速度把小车的场景位移放大、把管道拉长）
        private bool pipeFrameReady;
        private Vector3 pipeOrigin;
        private Vector3 pipeAxis = Vector3.forward;
        private float pipeAdvance;
        private Vector3 lastCarPos;

        /// <summary>已累积的截面环数量。</summary>
        public int SectionCount => sections.Count;
        /// <summary>当前小车所在截面索引（最新累积的截面，用于缺陷轴向定位）。</summary>
        public int CurrentSectionIndex => sections.Count - 1;
        /// <summary>平均管道半径（世界坐标，用于调试标记缩放等）。</summary>
        public float AverageRadius => avgRadius;
        public bool HasResult => hasResult;
        /// <summary>当前是否处于实时累积状态（false 表示正在查看本地存档）。</summary>
        public bool LiveEnabled => liveEnabled;
        /// <summary>重建结果网格（顶点为世界坐标）。</summary>
        public MeshFilter ResultMeshFilter => meshFilter;

        public void Setup(RobotDataReader reader, Transform carTransform, CarController controller)
        {
            if (robotDataReader != null)
            {
                robotDataReader.OnPointCloudReceived -= HandlePointCloud;
            }

            this.robotDataReader = reader;
            this.car = carTransform;
            this.carController = controller;

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
            UpdatePolarGrid();

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
            // 查看本地存档时暂停实时累积，避免实时数据混入存档模型
            if (!liveEnabled)
            {
                return;
            }

            // 只有小车在移动（有速度）时才建模：静止时不断累积会得到大量重叠的截面
            if (carController == null || carController.Speed < minSpeedToRebuild)
            {
                return;
            }

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

            // 截面中心：按真实里程沿管道轴推进，避免虚拟速度把管道长度放大若干倍
            Vector3 sectionOrigin = ComputeSectionOrigin();

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
                // 环中心用「按真实里程推进的截面中心」，环朝向沿用小车当前旋转
                section[i] = sectionOrigin + car.rotation * local;
            }

            FillNaNs(section);

            sections.Add(section);
            sectionsDirty = true;

            // 记录截面环中心、朝向、平均半径（用于缺陷定位）
            AddSectionStats(section, car != null ? car.rotation : Quaternion.identity);

            if (sections.Count == 1)
            {
                Debug.Log($"[PointCloudRebuilder] 收到首个截面，每截面 {sectionSize} 点");
            }
            else if (sections.Count % 20 == 0)
            {
                Debug.Log($"[PointCloudRebuilder] 已累积 {sections.Count} 截面");
            }
        }

        /// <summary>
        /// 计算当前截面的中心：以首个截面为原点，沿管道轴向按「真实里程」推进。
        /// 小车场景位移含 virtualSpeedRatio 倍率，除以该倍率还原真实里程，使管道长度与实际里程一致。
        /// </summary>
        private Vector3 ComputeSectionOrigin()
        {
            if (!pipeFrameReady)
            {
                pipeOrigin = car.position;
                Vector3 f = car.forward; f.y = 0f;
                Vector3 r = car.right; r.y = 0f;
                Vector3 dir = f.sqrMagnitude >= r.sqrMagnitude ? f : r;
                pipeAxis = dir.sqrMagnitude < 1e-4f ? Vector3.forward : dir.normalized;
                pipeAdvance = 0f;
                lastCarPos = car.position;
                pipeFrameReady = true;
                return pipeOrigin;
            }

            Vector3 step = car.position - lastCarPos;
            step.y = 0f;
            lastCarPos = car.position;

            float ratio = (useRealAxialDistance && carController != null)
                ? Mathf.Max(1f, carController.VirtualSpeedRatio)
                : 1f;
            pipeAdvance += Vector3.Dot(step, pipeAxis) / ratio;

            return pipeOrigin + pipeAxis * pipeAdvance;
        }

        /// <summary>计算并记录一个截面环的中心、朝向与平均半径（缺陷定位用）。</summary>
        private void AddSectionStats(Vector3[] section, Quaternion rotation)
        {
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
            sectionRotations.Add(rotation);
            sectionRadii.Add(radius);
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
                colors.Add(new Color(0.32f, 0.86f, 1f, 1f)); // 亮青色不透明，保证预览清晰
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

        /// <summary>把指定截面上、指定角度范围内的点标记为指定颜色（用于缺陷标注，按缺陷类型着色）。</summary>
        public void MarkDefectAt(int sectionIndex, float angleDeg, float rangeDeg, Color color)
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
                    colors[vertIdx] = color;
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
            pipeFrameReady = false;
            pipeAdvance = 0f;
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

        /// <summary>回到实时建模：清空当前（存档）数据并恢复实时雷达累积。</summary>
        public void ResumeLive()
        {
            Clear();
            liveEnabled = true;
        }

        /// <summary>仅切换是否接收实时雷达累积（不清空已累积数据），供脚本 / UI 控制。</summary>
        public void SetLive(bool enabled)
        {
            liveEnabled = enabled;
        }

        /// <summary>导出当前累积的截面环（世界坐标）副本，用于本地存档。</summary>
        public Vector3[][] GetSections()
        {
            Vector3[][] copy = new Vector3[sections.Count][];
            for (int i = 0; i < sections.Count; i++)
            {
                copy[i] = (Vector3[])sections[i].Clone();
            }
            return copy;
        }

        /// <summary>从本地存档加载截面环（世界坐标）并立即重建网格。</summary>
        public bool LoadSections(Vector3[][] data, float radius)
        {
            ResetAll();
            liveEnabled = false;
            if (data == null || data.Length == 0 || data[0] == null || data[0].Length == 0)
            {
                sectionSize = 0;
                return false;
            }

            sectionSize = data[0].Length;
            for (int s = 0; s < data.Length; s++)
            {
                Vector3[] sec = data[s];
                if (sec == null || sec.Length != sectionSize)
                {
                    continue;
                }
                sections.Add(sec);
                // 顶点已是世界坐标，故使用单位朝向
                AddSectionStats(sec, Quaternion.identity);
            }

            if (radius > 0f)
            {
                avgRadius = radius;
            }

            processedSections = 0;
            Mesh m = Rebuild();
            return m != null;
        }

        // ===================== 极坐标网格（建模内） =====================
        /// <summary>把极坐标网格对齐到最新截面：位置/朝向取该截面，半径取该截面半径。</summary>
        private void UpdatePolarGrid()
        {
            int count = sections.Count;
            if (!showPolarGrid || count == 0 || sectionCenters.Count < count || sectionRotations.Count < count)
            {
                if (polarGridObject != null && polarGridObject.activeSelf)
                {
                    polarGridObject.SetActive(false);
                }
                return;
            }

            EnsurePolarGrid();

            int idx = count - 1;
            float r = sectionRadii.Count > idx && sectionRadii[idx] > 0f
                ? sectionRadii[idx]
                : Mathf.Max(0.05f, avgRadius);

            polarGridTransform.position = sectionCenters[idx];
            polarGridTransform.rotation = sectionRotations[idx];
            float s = r * Mathf.Max(0.5f, polarGridScale);
            polarGridTransform.localScale = new Vector3(s, s, s);

            if (!polarGridObject.activeSelf)
            {
                polarGridObject.SetActive(true);
            }
        }

        private void EnsurePolarGrid()
        {
            if (polarGridObject != null)
            {
                return;
            }

            polarGridObject = new GameObject("PolarGrid");
            polarGridObject.layer = resultLayer;
            polarGridObject.transform.SetParent(null, false);
            polarGridTransform = polarGridObject.transform;

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            Material mat = new Material(shader);
            mat.color = Color.white;

            Color ringColor = new Color(0.45f, 0.80f, 1f, 0.55f);
            Color outerColor = new Color(0.60f, 0.95f, 1f, 0.90f);
            Color spokeColor = new Color(0.45f, 0.80f, 1f, 0.35f);
            Color zeroColor = new Color(1f, 0.95f, 0.30f, 0.95f); // 0° 方位线：黄色

            const int rings = 4;
            for (int i = 1; i <= rings; i++)
            {
                AddPolarCircle(i / (float)rings, i == rings ? outerColor : ringColor, mat);
            }

            const int spokes = 12;
            for (int k = 0; k < spokes; k++)
            {
                bool isZero = k == 0;
                AddPolarSpoke(k * (360f / spokes), isZero ? zeroColor : spokeColor, mat, isZero);
            }
        }

        /// <summary>在网格局部 XY 平面画一个圆（单位半径，缩放由父物体承担）。</summary>
        private void AddPolarCircle(float rNorm, Color color, Material mat)
        {
            GameObject go = new GameObject("PolarRing");
            go.layer = resultLayer;
            go.transform.SetParent(polarGridTransform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.useWorldSpace = false;
            lr.loop = true;
            lr.positionCount = 96;
            lr.startWidth = 0.03f;
            lr.endWidth = 0.03f;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCapVertices = 0;
            lr.numCornerVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            for (int i = 0; i < 96; i++)
            {
                float a = i / 96f * Mathf.PI * 2f;
                lr.SetPosition(i, new Vector3(Mathf.Sin(a), Mathf.Cos(a), 0f) * rNorm);
            }
        }

        /// <summary>从圆心画一条方位线（角度 0° 指向局部 +Y，顺时针为正）。</summary>
        private void AddPolarSpoke(float angleDeg, Color color, Material mat, bool emphasize)
        {
            GameObject go = new GameObject("PolarSpoke");
            go.layer = resultLayer;
            go.transform.SetParent(polarGridTransform, false);

            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            float width = emphasize ? 0.05f : 0.02f;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.startColor = color;
            lr.endColor = color;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            float rad = angleDeg * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(rad), Mathf.Cos(rad), 0f);
            lr.SetPosition(0, Vector3.zero);
            lr.SetPosition(1, dir);
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
