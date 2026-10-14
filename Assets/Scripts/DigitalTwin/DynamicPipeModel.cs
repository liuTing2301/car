using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 动态管道模型：沿小车前进方向，用一圈圈圆环段拼接成一段半透明管道。
    /// 半径由小车自身外轮廓自动推算，始终紧贴小车；打开时立即生成覆盖小车的整段管道，之后随运动沿轴向延伸。
    /// 车轮痕迹与网格地板保持同步：使用同一组车轮（同样的筛选规则），沿管壁生成细窄的痕迹带。
    /// </summary>
    public class DynamicPipeModel : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private Transform car;
        [SerializeField] private CarController carController;
        [Tooltip("管道的固定世界位置。")]
        [SerializeField] private Vector3 pipePosition = new Vector3(6824f, 68617f, 96002f);

        [Header("管道尺寸")]
        [Tooltip("管壁相对小车外轮廓的放大系数（1 表示刚好贴住外轮廓）。")]
        [SerializeField] private float fitScale = 1.00f;
        [Tooltip("管壁厚度 = 管道半径 × 该比例（数值越大管壁越厚）。厚度朝外生长，内壁始终贴合小车。")]
        [SerializeField] private float wallThicknessRatio = 0.14f;
        [Tooltip("自动测量尺寸的校正系数。部分模型自带远大于车身的部件（底座/环境等）会把包围盒撑大，用它把尺寸拉回车身实际大小。")]
        [SerializeField] private float sizeScale = 0.092f;
        [Tooltip("管道两端超出小车的余量 = 管道半径 × 该比例。")]
        [SerializeField] private float endMarginRatio = 0.3f;

        [Header("采样")]
        [Tooltip("采样步长 = 管道半径 × 该比例，越小越圆滑。")]
        [SerializeField] private float sampleStepRatio = 0.35f;
        [Tooltip("最大截面数（安全上限，防止长时间运行内存无限增长）。")]
        [SerializeField] private int maxSegments = 20000;
        [Tooltip("圆周分段数，越高越圆滑。")]
        [SerializeField] private int radialSegments = 32;

        [Header("外观")]
        [Tooltip("外壁颜色：透明度越高越像实心（仍保留可看进管道）。")]
        [SerializeField] private Color pipeColor = new Color(0.16f, 0.58f, 1f, 0.45f);
        [Tooltip("内壁颜色：内壁挡在小车前面，需较透，才能看到管道内的小车 / 车轮痕迹。")]
        [SerializeField] private Color innerColor = new Color(0.06f, 0.30f, 0.62f, 0.30f);
        [Tooltip("两端端面（管壁截面）颜色，默认与侧壁一致，让它看起来就是同一圈管壁。")]
        [SerializeField] private Color endColor = new Color(0.16f, 0.58f, 1f, 0.45f);

        // ===== 车轮痕迹（车轮筛选与网格地板一致）=====
        [Header("车轮痕迹")]
        [Tooltip("痕迹宽度 = 管道半径 × 该比例（越小越细）。")]
        [SerializeField] private float trailWidthRatio = 0.02f;
        [Tooltip("痕迹颜色。")]
        [SerializeField] private Color trailColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [Tooltip("痕迹取车轮的父物体名（精确匹配，如「驱动轮组件」）。留空则不限。")]
        [SerializeField] private string trailWheelParentName = "驱动轮组件";
        [Tooltip("痕迹取车轮的名字关键字，留空则不限。")]
        [SerializeField] private string trailWheelNameKeyword = "智能车轮";

        // 管道网格（顶点为相对管道原点 origin 的坐标）
        private readonly List<Vector3> points = new List<Vector3>();
        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Vector3> norms = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> trisOuter = new List<int>();   // submesh 0：外壁
        private readonly List<int> trisInner = new List<int>();   // submesh 1：内壁

        // 痕迹网格
        private readonly List<Vector3> trailVerts = new List<Vector3>();
        private readonly List<Color> trailColors = new List<Color>();
        private readonly List<int> trailTris = new List<int>();

        private MeshFilter meshFilter;
        private Mesh mesh;
        private Material pipeMaterialOuter;
        private Material pipeMaterialInner;
        private Material pipeMaterialEnd;
        private Mesh endStartMesh;   // 起始端端面环
        private Mesh endEndMesh;     // 末端端面环
        private MeshFilter trailMeshFilter;
        private Mesh trailMesh;

        private Vector3 origin;          // 管道原点（固定世界坐标）
        private Vector3 axisDir;         // 管道轴向（世界坐标）
        private Vector3 axisRight;       // 截面横向轴（测量用）
        private Vector3 ringRight;       // 管道截面 0° 方向
        private Vector3 ringUp;          // 管道截面 90° 方向
        private float sMin = 0f;         // 已生成的轴向范围（相对 origin，沿 axisDir）
        private float sMax = 0f;
        private bool initialized;
        private bool loggedLimit;

        // 由小车外形推算出的尺寸
        private float pipeRadius = 1f;
        private float wallThickness = 0.1f;  // 管壁厚度
        private float halfLength;        // 小车沿轴向的半长
        private float halfHeight;        // 小车截面内的半高
        private float endMargin;         // 管道两端超出小车的余量
        private float sampleStep;
        private float trailWidth;
        private float contactGap;

        // 痕迹：每个车轮独立记录上次位置，保证快速移动时连线不断
        private Transform[] trailWheels;
        private Vector3[] lastContact;
        private Vector3[] lastRadial;
        private bool[] hasWheelPos;
        private bool trailInited;

        public void Setup(Transform carTransform, CarController controller)
        {
            this.car = carTransform;
            this.carController = controller;
        }

        private void Awake()
        {
            meshFilter = gameObject.AddComponent<MeshFilter>();
            MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
            pipeMaterialOuter = CreateTransparentMaterial(pipeColor);
            pipeMaterialInner = CreateTransparentMaterial(innerColor);
            pipeMaterialEnd = CreateEndMaterial(endColor);
            mr.sharedMaterials = new[] { pipeMaterialOuter, pipeMaterialInner };

            // 两端端面环：独立物体，保证从任何角度看管壁厚度都可见
            endStartMesh = CreateEndRingObject("PipeEndStart");
            endEndMesh = CreateEndRingObject("PipeEndEnd");

            mesh = new Mesh();
            mesh.MarkDynamic();
            meshFilter.mesh = mesh;

            // 痕迹层：与管道同一坐标系，单独一个网格与材质
            GameObject trailGO = new GameObject("WheelTrail");
            trailGO.transform.SetParent(transform, false);
            trailMeshFilter = trailGO.AddComponent<MeshFilter>();
            MeshRenderer tr = trailGO.AddComponent<MeshRenderer>();
            tr.material = CreateTrailMaterial();
            trailMesh = new Mesh();
            trailMesh.MarkDynamic();
            trailMeshFilter.mesh = trailMesh;
        }

        private void Update()
        {
            if (car == null)
            {
                return;
            }

            if (!initialized)
            {
                StartPipe();
                return;
            }

            ExtendPipe();
            UpdateTrail();
        }

        // ===================== 初始化 =====================
        /// <summary>打开时立即生成一段覆盖整个小车的管道。</summary>
        private void StartPipe()
        {
            InitFromCar();

            origin = pipePosition;
            transform.position = origin;
            transform.rotation = Quaternion.identity;

            // 初始截面覆盖「车尾 - 余量」到「车头 + 余量」（以小车当前位置为中心）
            float s0 = Vector3.Dot(car.position - origin, axisDir);
            sMin = s0 - (halfLength + endMargin);
            sMax = s0 + (halfLength + endMargin);

            points.Clear();
            int count = Mathf.Max(2, Mathf.CeilToInt((sMax - sMin) / sampleStep) + 1);
            for (int i = 0; i < count; i++)
            {
                float s = Mathf.Lerp(sMin, sMax, i / (float)(count - 1));
                points.Add(axisDir * s);
            }

            RebuildPipeMesh();
            ResetTrailContacts();
            ClearTrail();
            initialized = true;
        }

        /// <summary>小车沿轴向移动后，把管道向两端延伸，始终比小车多出一段余量。</summary>
        private void ExtendPipe()
        {
            if (points.Count >= maxSegments)
            {
                if (!loggedLimit)
                {
                    loggedLimit = true;
                    Debug.LogWarning($"[DynamicPipeModel] 已达最大截面数 {maxSegments}，管道停止延伸。");
                }
                return;
            }

            float s = Vector3.Dot(car.position - origin, axisDir);
            bool changed = false;

            // 管道两端始终至少超出小车「半长 + 余量」，随小车移动连续延伸
            float front = s + halfLength + endMargin;
            float back = s - halfLength - endMargin;

            // 前进方向：在末端追加
            while (points.Count < maxSegments && front > sMax + sampleStep)
            {
                sMax += sampleStep;
                points.Add(axisDir * sMax);
                changed = true;
            }

            // 后退方向：在首端前插
            while (points.Count < maxSegments && back < sMin - sampleStep)
            {
                sMin -= sampleStep;
                points.Insert(0, axisDir * sMin);
                changed = true;
            }

            if (changed)
            {
                RebuildPipeMesh();
            }
        }

        /// <summary>
        /// 依据小车外轮廓推算管道尺寸。
        /// 小车模型内部往往带旋转，直接取轴对齐包围盒会被放大；
        /// 因此把每个 Renderer 的真实包围盒投影到「轴向 / 横向 / 竖向」坐标架上，得到真实截面尺寸。
        /// </summary>
        private void InitFromCar()
        {
            axisDir = GetMoveDirection();
            axisRight = Vector3.Cross(Vector3.up, axisDir).normalized;
            ringRight = Vector3.Cross(axisDir, Vector3.up).normalized;
            ringUp = Vector3.Cross(ringRight, axisDir).normalized;

            Vector3 refPos = car.position;
            float vMin = float.MaxValue, vMax = float.MinValue;
            float wMin = float.MaxValue, wMax = float.MinValue;
            float uMin = float.MaxValue, uMax = float.MinValue;

            foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            {
                if (r == null)
                {
                    continue;
                }
                Bounds lb = r.localBounds;
                Vector3 c = lb.center;
                Vector3 e = lb.extents;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = c + new Vector3(
                        (i & 1) == 0 ? -e.x : e.x,
                        (i & 2) == 0 ? -e.y : e.y,
                        (i & 4) == 0 ? -e.z : e.z);
                    Vector3 rel = r.transform.TransformPoint(corner) - refPos;

                    float u = Vector3.Dot(rel, axisDir);
                    float v = Vector3.Dot(rel, axisRight);
                    float w = Vector3.Dot(rel, Vector3.up);

                    if (u < uMin) uMin = u;
                    if (u > uMax) uMax = u;
                    if (v < vMin) vMin = v;
                    if (v > vMax) vMax = v;
                    if (w < wMin) wMin = w;
                    if (w > wMax) wMax = w;
                }
            }

            if (uMax < uMin)
            {
                uMin = -1f; uMax = 1f; vMin = -1f; vMax = 1f; wMin = -1f; wMax = 1f;
            }

            float halfWidth = (vMax - vMin) * 0.5f;
            halfHeight = (wMax - wMin) * 0.5f;
            halfLength = (uMax - uMin) * 0.5f;

            // 尺寸校正：把测量值整体缩放到车身实际大小
            float k = Mathf.Max(0.001f, sizeScale);
            halfWidth *= k;
            halfHeight *= k;
            halfLength *= k;

            pipeRadius = Mathf.Max(0.05f, Mathf.Max(halfWidth, halfHeight) * Mathf.Max(1f, fitScale));
            wallThickness = Mathf.Clamp(pipeRadius * wallThicknessRatio, pipeRadius * 0.02f, pipeRadius * 0.45f);
            endMargin = pipeRadius * Mathf.Max(0.05f, endMarginRatio);
            sampleStep = Mathf.Max(0.05f, pipeRadius * Mathf.Max(0.05f, sampleStepRatio));
            trailWidth = Mathf.Max(0.001f, pipeRadius * Mathf.Max(0.001f, trailWidthRatio));
            contactGap = pipeRadius * 0.01f;   // 痕迹略微离开管壁，避免与管壁重合

            ResolveTrailWheels();

            Debug.Log($"[DynamicPipeModel] 半径 {pipeRadius:0.00}（半宽 {halfWidth:0.00} / 半高 {halfHeight:0.00} / 半长 {halfLength:0.00}），" +
                      $"校正系数 {sizeScale}，轴向 {axisDir}，车轮 {trailWheels.Length}，采样步长 {sampleStep:0.00}，痕迹宽 {trailWidth:0.00}");
        }

        /// <summary>解析痕迹车轮：与网格地板完全相同的规则（父物体名 + 名字关键字精确匹配）。</summary>
        private void ResolveTrailWheels()
        {
            List<Transform> list = new List<Transform>();

            Transform root = car != null ? car : transform;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                if (!string.IsNullOrEmpty(trailWheelNameKeyword) && !t.name.Contains(trailWheelNameKeyword))
                {
                    continue;
                }
                if (!string.IsNullOrEmpty(trailWheelParentName) && !BelongsToGroup(t, trailWheelParentName))
                {
                    continue;
                }
                list.Add(t);
            }

            // 兜底 1：按名字没筛出来时，用 CarController 收集到的车轮
            if (list.Count == 0 && carController != null && carController.Wheels != null)
            {
                for (int i = 0; i < carController.Wheels.Count; i++)
                {
                    if (carController.Wheels[i] != null)
                    {
                        list.Add(carController.Wheels[i]);
                    }
                }
            }

            // 兜底 2：仍为空则用车身位置
            if (list.Count == 0 && car != null)
            {
                list.Add(car);
            }

            trailWheels = list.ToArray();
            lastContact = new Vector3[trailWheels.Length];
            lastRadial = new Vector3[trailWheels.Length];
            hasWheelPos = new bool[trailWheels.Length];
        }

        /// <summary>
        /// 判断车轮是否属于指定轮组：从父级向上查找，先遇到名字精确相同的组即命中；
        /// 若先遇到同前缀的编号变体（如「驱动轮组件1」），则判定不属于目标组。
        /// </summary>
        private static bool BelongsToGroup(Transform t, string groupName)
        {
            Transform p = t.parent;
            while (p != null)
            {
                if (p.name == groupName)
                {
                    return true;
                }
                if (p.name.StartsWith(groupName))
                {
                    return false;
                }
                p = p.parent;
            }
            return false;
        }

        // ===================== 管道网格 =====================
        private void RebuildPipeMesh()
        {
            int n = points.Count;
            if (n < 2)
            {
                mesh.Clear();
                ClearEndRings();
                return;
            }

            verts.Clear();
            norms.Clear();
            uvs.Clear();
            trisOuter.Clear();
            trisInner.Clear();

            // 厚度朝外：内壁钉在贴合小车的半径上，外壁 = 内壁 + 壁厚（小车始终在管道内）
            float innerRadius = Mathf.Max(0.001f, pipeRadius);
            float outerRadius = innerRadius + wallThickness;
            int ring = radialSegments * 2; // 每圈：外壁 R 点 + 内壁 R 点

            // 外壁（法线朝外）与内壁（法线朝内）
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < radialSegments; j++)
                {
                    float angle = (float)j / radialSegments * Mathf.PI * 2f;
                    Vector3 dir = ringRight * Mathf.Cos(angle) + ringUp * Mathf.Sin(angle);
                    verts.Add(points[i] + dir * outerRadius);
                    norms.Add(dir);
                    uvs.Add(new Vector2((float)j / radialSegments, i * 0.05f));
                }
                for (int j = 0; j < radialSegments; j++)
                {
                    float angle = (float)j / radialSegments * Mathf.PI * 2f;
                    Vector3 dir = ringRight * Mathf.Cos(angle) + ringUp * Mathf.Sin(angle);
                    verts.Add(points[i] + dir * innerRadius);
                    norms.Add(-dir);
                    uvs.Add(new Vector2((float)j / radialSegments, i * 0.05f));
                }
            }

            // 侧壁三角带（外壁 + 内壁，内壁反向绕序）
            for (int i = 0; i < n - 1; i++)
            {
                for (int j = 0; j < radialSegments; j++)
                {
                    int jn = (j + 1) % radialSegments;

                    int oa = i * ring + j;
                    int ob = i * ring + jn;
                    int oc = (i + 1) * ring + j;
                    int od = (i + 1) * ring + jn;
                    trisOuter.Add(oa); trisOuter.Add(oc); trisOuter.Add(ob);
                    trisOuter.Add(ob); trisOuter.Add(oc); trisOuter.Add(od);

                    int ia = i * ring + radialSegments + j;
                    int ib = i * ring + radialSegments + jn;
                    int ic = (i + 1) * ring + radialSegments + j;
                    int id = (i + 1) * ring + radialSegments + jn;
                    trisInner.Add(ia); trisInner.Add(ib); trisInner.Add(ic);
                    trisInner.Add(ib); trisInner.Add(id); trisInner.Add(ic);
                }
            }

            // 两端端面环由独立物体渲染（见 BuildEndRing），这里只做管壁
            mesh.Clear();
            if (verts.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(trisOuter, 0);
            mesh.SetTriangles(trisInner, 1);
            mesh.RecalculateBounds();

            // 端面环：不透明 + 无光照 + 双面，任何角度（尤其正对端面）都能看见管壁厚度
            BuildEndRing(endStartMesh, points[0]);
            BuildEndRing(endEndMesh, points[n - 1]);
        }

        /// <summary>构建一个端面环（圆环，双面）网格——独立物体渲染，保证正对端面时也可见。</summary>
        private void BuildEndRing(Mesh m, Vector3 center)
        {
            if (m == null)
            {
                return;
            }

            int rs = Mathf.Max(3, radialSegments);
            float rIn = Mathf.Max(0.001f, pipeRadius);
            float rOut = rIn + wallThickness;

            List<Vector3> v = new List<Vector3>(rs * 2);
            List<int> t = new List<int>(rs * 12);
            for (int j = 0; j < rs; j++)
            {
                float a = (float)j / rs * Mathf.PI * 2f;
                Vector3 dir = ringRight * Mathf.Cos(a) + ringUp * Mathf.Sin(a);
                v.Add(center + dir * rOut);
            }
            for (int j = 0; j < rs; j++)
            {
                float a = (float)j / rs * Mathf.PI * 2f;
                Vector3 dir = ringRight * Mathf.Cos(a) + ringUp * Mathf.Sin(a);
                v.Add(center + dir * rIn);
            }
            for (int j = 0; j < rs; j++)
            {
                int jn = (j + 1) % rs;
                int o = j, on = jn, i0 = rs + j, i1 = rs + jn;
                // 正面
                t.Add(o); t.Add(i0); t.Add(on);
                t.Add(on); t.Add(i0); t.Add(i1);
                // 背面（双面，从任意方向都可见）
                t.Add(o); t.Add(on); t.Add(i0);
                t.Add(on); t.Add(i1); t.Add(i0);
            }

            Color32[] cols = new Color32[v.Count];
            for (int i = 0; i < cols.Length; i++)
            {
                cols[i] = new Color32(255, 255, 255, 255); // Sprites/Default 取顶点色，需给白色避免渲染成黑
            }

            m.Clear();
            m.SetVertices(v);
            m.SetColors(cols);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
        }

        private void ClearEndRings()
        {
            if (endStartMesh != null)
            {
                endStartMesh.Clear();
            }
            if (endEndMesh != null)
            {
                endEndMesh.Clear();
            }
        }

        /// <summary>创建一个端面环物体（自带 MeshFilter / MeshRenderer）。</summary>
        private Mesh CreateEndRingObject(string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, false);
            MeshFilter mf = go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = pipeMaterialEnd;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            Mesh m = new Mesh();
            m.MarkDynamic();
            mf.sharedMesh = m;
            return m;
        }

        // ===================== 车轮痕迹（沿管壁的细带状） =====================
        private void ResetTrailContacts()
        {
            if (hasWheelPos == null)
            {
                return;
            }
            for (int i = 0; i < hasWheelPos.Length; i++)
            {
                hasWheelPos[i] = false;
            }
        }

        /// <summary>每个车轮在管壁上滚过的位置画一条细痕迹带。</summary>
        private void UpdateTrail()
        {
            if (trailMesh == null || points.Count < 2)
            {
                return;
            }

            if (!trailInited)
            {
                trailInited = true;
                ResolveTrailWheels();
            }
            if (trailWheels == null || trailWheels.Length == 0)
            {
                return;
            }

            // 步长取痕迹宽度的一半，保证段与段首尾重叠、看起来是连续的一条线
            float step = Mathf.Max(0.005f, trailWidth * 0.5f);
            bool dirty = false;

            for (int i = 0; i < trailWheels.Length; i++)
            {
                Transform w = trailWheels[i];
                if (w == null)
                {
                    continue;
                }

                Vector3 radial;
                float s;
                if (!TryGetWallContact(w.position, out radial, out s))
                {
                    continue;
                }
                Vector3 contact = axisDir * s + radial * (pipeRadius - contactGap);

                if (!hasWheelPos[i])
                {
                    hasWheelPos[i] = true;
                    lastContact[i] = contact;
                    lastRadial[i] = radial;
                    continue;
                }

                float dist = Vector3.Distance(contact, lastContact[i]);
                if (dist < step)
                {
                    continue;
                }

                int sub = Mathf.Clamp(Mathf.CeilToInt(dist / step), 1, 256);
                for (int k = 1; k <= sub; k++)
                {
                    Vector3 q = Vector3.Lerp(lastContact[i], contact, k / (float)sub);
                    AddTrailQuad(lastContact[i], q, lastRadial[i]);
                    lastContact[i] = q;
                }
                lastRadial[i] = radial;
                dirty = true;
            }

            if (dirty)
            {
                UploadTrailMesh();
            }
        }

        /// <summary>车轮 → 管壁接触点：取车轮相对管道轴心的截面径向，并给出其轴向坐标。</summary>
        private bool TryGetWallContact(Vector3 wheelWorld, out Vector3 radial, out float s)
        {
            radial = Vector3.zero;
            s = 0f;
            Vector3 rel = wheelWorld - origin;
            s = Vector3.Dot(rel, axisDir);
            Vector3 v = rel - axisDir * s;
            if (v.sqrMagnitude < 1e-8f)
            {
                return false;
            }
            radial = v.normalized;
            return true;
        }

        private void AddTrailQuad(Vector3 from, Vector3 to, Vector3 radial)
        {
            Vector3 side = Vector3.Cross(axisDir, radial);
            if (side.sqrMagnitude < 1e-8f)
            {
                return;
            }
            side = side.normalized * (trailWidth * 0.5f);

            int b = trailVerts.Count;
            trailVerts.Add(from + side);
            trailVerts.Add(from - side);
            trailVerts.Add(to - side);
            trailVerts.Add(to + side);

            for (int i = 0; i < 4; i++)
            {
                trailColors.Add(trailColor);
            }

            trailTris.Add(b); trailTris.Add(b + 2); trailTris.Add(b + 1);
            trailTris.Add(b); trailTris.Add(b + 3); trailTris.Add(b + 2);
        }

        private void UploadTrailMesh()
        {
            trailMesh.Clear();
            if (trailVerts.Count > 65535)
            {
                trailMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            trailMesh.SetVertices(trailVerts);
            trailMesh.SetColors(trailColors);
            trailMesh.SetTriangles(trailTris, 0);
            trailMesh.RecalculateBounds();
        }

        private void ClearTrail()
        {
            trailVerts.Clear();
            trailColors.Clear();
            trailTris.Clear();
            if (trailMesh != null)
            {
                trailMesh.Clear();
            }
        }

        // ===================== 工具 =====================
        /// <summary>水平前进方向：在 forward / right 中取水平分量较大者，避免模型轴向不正。</summary>
        private Vector3 GetMoveDirection()
        {
            Vector3 f = car.forward;
            f.y = 0f;
            Vector3 r = car.right;
            r.y = 0f;
            Vector3 dir = f.sqrMagnitude >= r.sqrMagnitude ? f : r;
            return dir.sqrMagnitude < 1e-4f ? Vector3.forward : dir.normalized;
        }

        private Material CreateTransparentMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
            }

            Material mat = new Material(shader);
            mat.color = color;

            mat.SetFloat("_Mode", 3f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            return mat;
        }

        /// <summary>
        /// 端面材质：无光照（Unlit）+ 透明，观感与侧壁一致。
        /// 端面法线沿管道轴向，若用受光材质，正对端面看时会发暗而“消失”；无光照可保证任何角度都看得见，
        /// 同时颜色/透明度与侧壁相同，不会有突兀的亮色。
        /// </summary>
        private Material CreateEndMaterial(Color color)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material mat = new Material(shader);
            mat.color = color;
            mat.renderQueue = 3001; // 与侧壁同层，略后绘制
            return mat;
        }

        private Material CreateTrailMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Transparent");
            }
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material mat = new Material(shader);
            mat.color = Color.white;   // 颜色由顶点色决定
            mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            mat.renderQueue = 3005;    // 画在管道之上，保证痕迹可见
            return mat;
        }
    }
}
