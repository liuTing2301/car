using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using DigitalTwin.Scripting;

namespace DigitalTwin
{
    /// <summary>
    /// 数字孪生主界面：参照《管道探测小车 · 数字孪生平台》原型构建。
    /// 顶部栏（品牌 / 状态 / 时间 / 退出）+ 左侧设备操作栏（连接 / 驾驶模式 / 数字建模）
    /// + 中栏（检测读数带 · 三维舞台 · 行驶状态带）+ 右侧感知栏（摄像头识别 / 雷达扫描）+ 底部状态栏。
    /// 由 DigitalTwinManager 在运行时自动装配。
    /// </summary>
    public class DigitalTwinUI : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private CarController car;
        [SerializeField] private Transform carTransform;
        [SerializeField] private DigitalTwinClient client;
        [SerializeField] private CameraReceiver cameraReceiver;
        [SerializeField] private CameraRotor cameraRotor;
        [SerializeField] private RobotDataReader robotDataReader;
        [SerializeField] private DynamicPipeModel dynamicPipeModel;
        [SerializeField] private PointCloudRebuilder pointCloudRebuilder;

        private Canvas canvas;
        private Text timeText;
        private Text dateText;
        private Text statusText;
        private Image statusChipImg;
        private Text stageModeText;
        private Text travelStateText;
        private Text footerText;

        private Text speedText;
        private Text distanceText;
        private Text positionText;
        private DataChart speedChart;
        private DataChart distanceChart;
        private RadarScanner radar;
        private ModeController modeController;

        private RawImage cameraImage;
        private Texture2D cameraTexture;
        private Text recognitionText;
        private Text cameraAngleText;
        private GameObject cameraOverlay;
        private readonly List<GameObject> overlayBoxes = new List<GameObject>();

        private Text pipeStatusText;
        private InputField ipInput;
        private InputField portInput;
        private Button connectBtn;
        private Button pipeModelBtn;
        private bool pipeModelVisible;
        private GameObject quitConfirmPanel;
        private GameObject chartPanel;
        private GameObject rebuildPanel;
        private RawImage rebuildPreview;
        private Text rebuildStatusText;
        private Button backToLiveBtn;
        private Text noticeText;
        private float noticeTimer;
        private GameObject archivePanel;
        private RectTransform archiveListContainer;
        private Camera previewCamera;
        private RenderTexture previewRT;
        private int rebuildLayer = 31;
        private Vector3 previewTarget;
        private float previewDistance = 1f;
        private float previewRadius = 1f;
        private float previewYaw = 45f;
        private float previewPitch = 25f;
        private bool previewInited;

        [Header("缺陷标注")]
        [SerializeField] private float cameraFov = 60f;
        [Tooltip("缺陷置信度阈值，低于此分数不标注。")]
        [SerializeField] private float defectScoreThreshold = 0.5f;
        [SerializeField] private int maxDefectFrames = 30;
        [Tooltip("每个缺陷最多标红的截面数（控制标红长度，避免持续多帧累积过长）。")]
        [SerializeField] private int maxMarkSections = 6;
        [Tooltip("云台环向角 → 管道环向角的标定偏移（度）。云台 CurrentAngle=0 时镜头实际指向的环向角，用调试小球校准。")]
        [SerializeField] private float gimbalAngleOffset = 90f;
        private readonly List<DefectRecord> defects = new List<DefectRecord>();
        private GameObject aimMarker;

        /// <summary>点云重建弹窗是否打开（用于禁用主界面操作）。</summary>
        public static bool RebuildPanelOpen { get; private set; }
        private GameObject imagePanel;
        private RawImage imagePreview;
        private Text imageTitle;
        private RectTransform defectButtonContainer;
        // ===== 窗口拖拽 / 边框缩放（Windows 窗口模式）=====
        private class WindowState
        {
            public RectTransform panel;
            public RectTransform titleBar;
            public float minWidth;
            public float minHeight;
        }

        private readonly List<WindowState> windows = new List<WindowState>();
        private WindowState activeWindow;
        private int resizeEdge;
        private bool draggingWindow;
        private Vector2 winDragStartMouse;
        private Vector2 winDragStartAnchored;
        private Vector2 winDragStartSize;
        /// <summary>边框缩放的可抓取宽度（屏幕像素）。</summary>
        private const float WindowEdgeSize = 14f;
        private DataChart deviationChart;
        private DataChart ellipticityChart;
        private DataChart confidenceChart;
        private DataChart radiusChart;
        private Text deviationValueText;
        private Text ellipticityValueText;
        private Text confidenceValueText;
        private Text radiusValueText;
        private Text mainDeviationText;
        private Text mainEllipticityText;
        private Text mainConfidenceText;
        private Text mainRadiusText;

        private float radarMockTimer;
        private bool pointCloudActive;

        // ===== 脚本编写 =====
        private ScriptRunner scriptRunner;
        private GameObject scriptPanel;
        private InputField scriptInput;
        private ScrollRect scriptScroll;
        private RectTransform scriptViewportRect;
        private int scriptFontSize = 14;
        private Text scriptStatusText;
        private Text scriptOutputText;
        private ScrollRect scriptOutputScroll;
        private string scriptOutputCache;
        private GameObject scriptListPanel;
        private RectTransform scriptListContainer;
        private GameObject scriptNamePanel;
        private InputField scriptNameInput;
        private string scriptMsg;
        private float scriptMsgTimer;
        private bool scriptMsgError;

        // ===== 布局尺寸（参考分辨率 1920×1080）=====
        private const float TopBarHeight = 64f;
        private const float FooterHeight = 30f;
        /// <summary>左右侧栏统一宽度（保证左右长度对齐）。</summary>
        private const float SideBarWidth = 400f;
        private const float BarMargin = 12f;

        // ===== 视觉特效 =====
        private static readonly Color GlowLine = new Color(0f, 0.9f, 1f, 1f);
        private RectTransform scanLineRT;

        // ===== 侧栏展开 / 收起（横向）=====
        private RectTransform leftSidebarRect;
        private RectTransform rightSidebarRect;
        private RectTransform leftToggleRect;
        private RectTransform rightToggleRect;
        private Text leftToggleLabel;
        private Text rightToggleLabel;
        private float leftPosX;
        private float rightPosX;
        private float leftTargetX;
        private float rightTargetX;
        private bool leftCollapsed;
        private bool rightCollapsed;
        private bool sidebarsInited;
        private const float SidebarAnimSpeed = 2400f;

        // ===== 数据面板（检测数据 / 行驶状态）展开 / 收起（纵向）=====
        private RectTransform readoutBandRect;
        private RectTransform travelBandRect;
        private RectTransform stageRect;
        private RectTransform topDataToggleRect;
        private RectTransform bottomDataToggleRect;
        private Text topDataToggleLabel;
        private Text bottomDataToggleLabel;
        private float readoutT;
        private float travelT;
        private bool readoutCollapsed;
        private bool travelCollapsed;
        private const float ReadoutBandHeight = 104f;
        private const float TravelBandHeight = 190f;
        private const float StageToolbarHeight = 44f;
        private const float StageCoordsHeight = 34f;

        // ===== 网格地板 =====
        [Header("网格地板")]
        [Tooltip("地板边长（世界单位）。")]
        [SerializeField] private float gridFloorSize = 2000f;
        [Tooltip("每格边长（世界单位），决定网格疏密。")]
        [SerializeField] private float gridCellSize = 100f;
        [Tooltip("地板相对小车的高度偏移（世界单位）。")]
        [SerializeField] private float gridFloorOffsetY = 0f;
        [Tooltip("网格线颜色。")]
        [SerializeField] private Color gridLineColor = new Color(0.15f, 0.55f, 1f, 1f);

        // ===== 移动痕迹 =====
        [Header("移动痕迹")]
        [Tooltip("痕迹层相对地板的高度偏移（世界单位），闪烁时调大。")]
        [SerializeField] private float trailYOffset = 0.5f;
        [Tooltip("痕迹纹理分辨率（越大越细腻，开销越高）。")]
        [SerializeField] private int trailResolution = 512;
        [Tooltip("痕迹画笔半径（纹理像素）。")]
        [SerializeField] private int trailBrushRadius = 6;
        [Tooltip("痕迹颜色。")]
        [SerializeField] private Color trailColor = new Color(0.2f, 0.9f, 1f, 0.9f);
        [Tooltip("痕迹取车轮的父物体名（精确匹配，如「驱动轮组件」）。用于只保留某一组驱动轮的胎印，留空则不限。")]
        [SerializeField] private string trailWheelParentName = "驱动轮组件";
        [Tooltip("痕迹取车轮的名字关键字，留空则不限。")]
        [SerializeField] private string trailWheelNameKeyword = "智能车轮";

        private GameObject trailPlane;
        private Texture2D trailTexture;
        private Color32[] trailPixels;
        private Vector3 trailCenter;
        private float trailWorldSize = 1f;

        // 轮胎痕迹：每个车轮独立记录上次位置，保证快速移动时连线不断
        private Transform[] trailWheels;
        private Vector3[] lastWheelPos;
        private bool[] hasWheelPos;
        private bool trailInited;

        public void Setup(CarController car, Transform carTransform, DigitalTwinClient client, CameraReceiver cam, CameraRotor cameraRotor, RobotDataReader robotDataReader, DynamicPipeModel dynamicPipeModel, PointCloudRebuilder pointCloudRebuilder, ScriptRunner scriptRunner)
        {
            this.car = car;
            this.carTransform = carTransform;
            this.client = client;
            this.cameraReceiver = cam;
            this.cameraRotor = cameraRotor;
            this.robotDataReader = robotDataReader;
            this.dynamicPipeModel = dynamicPipeModel;
            this.pointCloudRebuilder = pointCloudRebuilder;
            this.scriptRunner = scriptRunner;
        }

        private void Start()
        {
            // 先设置 3D 主相机底色，避免后续 UI 构建若出现异常导致此步不执行
            SetupMainCameraBackground();
            CreateGridFloor();

            BuildCanvas();
            BuildTopBar();
            BuildLeftSidebar();
            BuildCenterColumn();
            BuildRightColumn();
            BuildFooter();
            BuildSidebarToggles();
            BuildChartPanel();
            BuildRebuildPanel();
            BuildArchivePanel();
            BuildImagePanel();
            BuildScriptPanel();
            BuildScriptListPanel();
            BuildScriptNamePanel();
            CreateAimMarker();
            BuildQuitConfirm();
            WireCamera();
            WireRobotData();
        }

        private void Update()
        {
            UpdateClock();
            UpdateTelemetry();
            UpdateStatus();
            UpdateRadarMock();
            UpdateRobotData();
            UpdateRebuildStatus();
            UpdatePreviewCamera();
            UpdateAimMarker();
            UpdateVisualEffects();
            UpdateTrail();
            UpdateNotice();
            UpdateSidebars();
            UpdateDataBands();
            UpdateWindows();
            UpdateScriptEditorLayout();
            UpdateScriptZoom();
            UpdateScriptStatus();
            UpdateScriptOutput();
            HandleQuitInput();
        }

        private void UpdateRadarMock()
        {
            if (radar == null || pointCloudActive) return;
            radarMockTimer += Time.deltaTime;
            // 无真实点云时，每 0.4 秒生成一个模拟回波点，演示雷达扫描效果
            if (radarMockTimer >= 0.4f)
            {
                radarMockTimer = 0f;
                radar.AddMockBlip();
            }
        }

        // ===================== 整体框架 =====================
        private void BuildCanvas()
        {
            // 确保事件系统存在，否则按钮无法响应点击（场景中通常缺少 EventSystem）
            if (UnityEngine.EventSystems.EventSystem.current == null)
            {
                new GameObject("EventSystem",
                    typeof(UnityEngine.EventSystems.EventSystem),
                    typeof(UnityEngine.EventSystems.StandaloneInputModule));
            }

            GameObject canvasGO = new GameObject("DigitalTwinUICanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);

            canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
        }

        /// <summary>在场景中创建一层蓝色网格地板（替代原先覆盖全屏的 UI 网格）。</summary>
        private void CreateGridFloor()
        {
            GameObject floor = new GameObject("GridFloor");
            // 放在场景根（不挂到小车下）：地板固定在世界坐标，小车移动才能在地板上体现出来
            floor.transform.SetParent(null);

            // 以小车所在位置为地板中心，并对其底部高度
            Vector3 center = carTransform != null ? carTransform.position : Vector3.zero;
            float floorY = center.y;
            if (carTransform != null)
            {
                Renderer[] renderers = carTransform.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds b = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        b.Encapsulate(renderers[i].bounds);
                    }
                    floorY = b.min.y;
                }
            }
            floor.transform.position = new Vector3(center.x, floorY + gridFloorOffsetY, center.z);

            MeshFilter mf = floor.AddComponent<MeshFilter>();
            MeshRenderer mr = floor.AddComponent<MeshRenderer>();

            float size = Mathf.Max(1f, gridFloorSize);
            mf.mesh = CreateFloorMesh(size);

            int tiling = Mathf.Max(1, Mathf.RoundToInt(size / Mathf.Max(0.01f, gridCellSize)));
            Shader shader = Shader.Find("Unlit/Texture");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            Material mat = new Material(shader);
            mat.mainTexture = CreateGridFloorTexture(128, gridLineColor, new Color(0.012f, 0.028f, 0.058f, 1f));
            mat.mainTextureScale = new Vector2(tiling, tiling);
            mr.sharedMaterial = mat;

            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            // 地板之上再叠一层可绘制的痕迹层
            CreateTrailPlane(center, floorY, size);
        }

        /// <summary>创建小车移动痕迹层：一块与地板同尺寸的透明面片，纹理由程序实时绘制。</summary>
        private void CreateTrailPlane(Vector3 center, float floorY, float size)
        {
            trailWorldSize = size;
            trailCenter = new Vector3(center.x, 0f, center.z);

            trailPlane = new GameObject("CarTrail");
            trailPlane.transform.SetParent(null);
            trailPlane.transform.position = new Vector3(center.x, floorY + trailYOffset, center.z);

            MeshFilter mf = trailPlane.AddComponent<MeshFilter>();
            MeshRenderer mr = trailPlane.AddComponent<MeshRenderer>();
            mf.mesh = CreateFloorMesh(size);

            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Standard");

            trailTexture = new Texture2D(Mathf.Max(64, trailResolution), Mathf.Max(64, trailResolution),
                TextureFormat.RGBA32, false);
            trailTexture.wrapMode = TextureWrapMode.Clamp;
            trailTexture.filterMode = FilterMode.Bilinear;
            trailPixels = new Color32[trailTexture.width * trailTexture.height];
            ClearTrailTexture();

            Material mat = new Material(shader);
            mat.mainTexture = trailTexture;
            mat.color = Color.white;
            mr.sharedMaterial = mat;

            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void ClearTrailTexture()
        {
            if (trailPixels == null)
            {
                return;
            }
            System.Array.Clear(trailPixels, 0, trailPixels.Length);
            if (trailTexture != null)
            {
                trailTexture.SetPixels32(trailPixels);
                trailTexture.Apply();
            }
        }

        /// <summary>
        /// 解析痕迹车轮：默认只取「驱动轮组件」下的「智能车轮」，
        /// 精确匹配父名，避免把「驱动轮组件1」「驱动轮组件2」等其它轮组也算进来。
        /// </summary>
        private void ResolveTrailWheels()
        {
            List<Transform> list = new List<Transform>();

            Transform root = carTransform != null ? carTransform : transform;
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
            if (list.Count == 0 && car != null && car.Wheels != null)
            {
                for (int i = 0; i < car.Wheels.Count; i++)
                {
                    if (car.Wheels[i] != null)
                    {
                        list.Add(car.Wheels[i]);
                    }
                }
            }

            // 兜底 2：仍为空则用车身位置
            if (list.Count == 0 && carTransform != null)
            {
                list.Add(carTransform);
                Debug.LogWarning("[DigitalTwinUI] 未找到痕迹车轮，将从小车中心生成痕迹。");
            }

            trailWheels = list.ToArray();
            lastWheelPos = new Vector3[trailWheels.Length];
            hasWheelPos = new bool[trailWheels.Length];

            Debug.Log($"[DigitalTwinUI] 痕迹车轮数量：{trailWheels.Length}");
        }

        /// <summary>
        /// 判断车轮是否属于指定轮组：从父级向上查找，
        /// 先遇到名字精确相同的组即命中；若先遇到同前缀的编号变体（如「驱动轮组件1」「驱动轮组件2」），
        /// 则判定不属于目标组，从而把其它轮组的胎印排除掉。
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
                    return false; // 编号变体组
                }
                p = p.parent;
            }
            return false;
        }

        /// <summary>每个车轮移动一段距离就把经过的路径画进痕迹纹理。</summary>
        private void UpdateTrail()
        {
            if (trailPlane == null || trailPixels == null || trailTexture == null)
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

            // 半个像素的步长，避免快速移动时轨迹断开
            float step = trailWorldSize / trailTexture.width * 0.5f;
            bool dirty = false;

            for (int i = 0; i < trailWheels.Length; i++)
            {
                Transform w = trailWheels[i];
                if (w == null)
                {
                    continue;
                }

                Vector3 p = w.position;
                if (!hasWheelPos[i])
                {
                    hasWheelPos[i] = true;
                    lastWheelPos[i] = p;
                    dirty |= DrawTrailPoint(p);
                    continue;
                }

                float dist = Vector3.Distance(p, lastWheelPos[i]);
                if (dist < step)
                {
                    continue;
                }

                int steps = Mathf.Clamp(Mathf.CeilToInt(dist / Mathf.Max(0.0001f, step)), 1, 128);
                for (int k = 1; k <= steps; k++)
                {
                    Vector3 q = Vector3.Lerp(lastWheelPos[i], p, k / (float)steps);
                    dirty |= DrawTrailPoint(q);
                }
                lastWheelPos[i] = p;
            }

            if (dirty)
            {
                UploadTrailTexture();
            }
        }

        /// <summary>把世界坐标点映射到痕迹纹理并画一个圆点，返回是否真正画上了。</summary>
        private bool DrawTrailPoint(Vector3 world)
        {
            int res = trailTexture.width;
            float u = (world.x - trailCenter.x) / trailWorldSize + 0.5f;
            float v = (world.z - trailCenter.z) / trailWorldSize + 0.5f;
            if (u < 0f || u > 1f || v < 0f || v > 1f)
            {
                return false; // 超出地板范围
            }

            int cx = Mathf.RoundToInt(u * (res - 1));
            int cy = Mathf.RoundToInt(v * (res - 1));
            int r = Mathf.Max(1, trailBrushRadius);
            Color32 c = trailColor;

            for (int dy = -r; dy <= r; dy++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r)
                    {
                        continue;
                    }
                    int x = cx + dx;
                    int y = cy + dy;
                    if (x < 0 || x >= res || y < 0 || y >= res)
                    {
                        continue;
                    }
                    trailPixels[y * res + x] = c;
                }
            }
            return true;
        }

        private void UploadTrailTexture()
        {
            if (trailTexture == null || trailPixels == null)
            {
                return;
            }
            trailTexture.SetPixels32(trailPixels);
            trailTexture.Apply();
        }

        /// <summary>创建水平地板面片（朝上）。</summary>
        private static Mesh CreateFloorMesh(float size)
        {
            float h = size * 0.5f;
            Mesh mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(-h, 0f, -h),
                new Vector3( h, 0f, -h),
                new Vector3( h, 0f,  h),
                new Vector3(-h, 0f,  h),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>生成可平铺的网格纹理（蓝色线条 + 深蓝底）。</summary>
        private static Texture2D CreateGridFloorTexture(int size, Color line, Color bg)
        {
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;

            const int lineWidth = 3;
            Color[] px = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool isLine = x < lineWidth || y < lineWidth;
                    px[y * size + x] = isLine ? line : bg;
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>把 3D 主相机背景设为深空蓝黑（Solid Color），让 3D 区域自带深空底色。</summary>
        private void SetupMainCameraBackground()
        {
            Camera main = Camera.main;

            // 兜底 1：按类型查找可用相机（排除点云预览相机）
            if (main == null || main == previewCamera)
            {
                main = null;
                foreach (Camera cam in FindObjectsOfType<Camera>())
                {
                    if (cam != previewCamera && cam.enabled)
                    {
                        main = cam;
                        break;
                    }
                }
            }

            if (main == null)
            {
                Debug.LogWarning("[DigitalTwinUI] 未找到主相机，3D 区域底色未设置。");
                return;
            }

            main.clearFlags = CameraClearFlags.SolidColor;
            main.backgroundColor = UIFactory.BgRoot;
            Debug.Log($"[DigitalTwinUI] 主相机 '{main.name}' 背景已设为深空色 {UIFactory.BgRoot}");
        }

        /// <summary>视觉特效驱动：顶栏扫描线。</summary>
        private void UpdateVisualEffects()
        {
            float t = Time.time;

            // 顶栏扫描线自左向右循环
            if (scanLineRT != null)
            {
                float p = Mathf.Repeat(t * 0.22f, 1f);
                scanLineRT.anchorMin = new Vector2(p, 0f);
                scanLineRT.anchorMax = new Vector2(p, 1f);

                Image img = scanLineRT.GetComponent<Image>();
                if (img != null)
                {
                    // 两端淡出，中间最亮
                    Color c = GlowLine;
                    c.a = 0.15f + 0.5f * Mathf.Sin(p * Mathf.PI);
                    img.color = c;
                }
            }
        }

        // ===================== 顶部栏 =====================
        private void BuildTopBar()
        {
            RectTransform bar = UIFactory.CreatePanel(canvas.transform, "TopBar", UIFactory.BgDark, UIFactory.BorderCyan, false);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(0, TopBarHeight);

            // 品牌图标（蓝色方块）
            RectTransform mark = UIFactory.CreatePanel(bar, "BrandMark", new Color(0.30f, 0.44f, 0.62f, 1f), UIFactory.AccentCyan);
            mark.anchorMin = new Vector2(0, 0.5f);
            mark.anchorMax = new Vector2(0, 0.5f);
            mark.pivot = new Vector2(0, 0.5f);
            mark.anchoredPosition = new Vector2(18, 0);
            mark.sizeDelta = new Vector2(34, 34);
            Text markIcon = UIFactory.CreateText(mark, "Icon", "◎", 20, Color.white, TextAnchor.MiddleCenter, FontStyle.Bold);
            FillRect(markIcon);

            // 标题 / 副标题
            Text title = UIFactory.CreateText(bar, "Title", "管道探测小车", 18, UIFactory.TextPrimary, TextAnchor.LowerLeft, FontStyle.Bold);
            RectTransform tRT = title.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0, 0.5f);
            tRT.anchorMax = new Vector2(0, 1);
            tRT.pivot = new Vector2(0, 0.5f);
            tRT.anchoredPosition = new Vector2(64, 3);
            tRT.sizeDelta = new Vector2(400, 0);

            Text subtitle = UIFactory.CreateText(bar, "Subtitle", "数字孪生平台", 12, UIFactory.TextDim, TextAnchor.UpperLeft);
            RectTransform stRT = subtitle.GetComponent<RectTransform>();
            stRT.anchorMin = new Vector2(0, 0);
            stRT.anchorMax = new Vector2(0, 0.5f);
            stRT.pivot = new Vector2(0, 0.5f);
            stRT.anchoredPosition = new Vector2(64, -3);
            stRT.sizeDelta = new Vector2(400, 0);

            // 状态胶囊：本地模式 + 连接状态
            Text modeChipText;
            CreateChip(bar, "ModeChip", "本地模式", UIFactory.TextDim, 240f, 84f, out modeChipText);
            statusChipImg = CreateChip(bar, "StatusChip", "未连接", UIFactory.AccentOrange, 334f, 96f, out statusText);

            // 右侧：时间 / 日期 / 退出
            timeText = UIFactory.CreateText(bar, "Time", "", 15, UIFactory.TextPrimary, TextAnchor.LowerRight);
            RectTransform tiRT = timeText.GetComponent<RectTransform>();
            tiRT.anchorMin = new Vector2(1, 0.5f);
            tiRT.anchorMax = new Vector2(1, 1);
            tiRT.pivot = new Vector2(1, 0.5f);
            tiRT.anchoredPosition = new Vector2(-104, 3);
            tiRT.sizeDelta = new Vector2(260, 0);

            dateText = UIFactory.CreateText(bar, "Date", "", 11, UIFactory.TextDim, TextAnchor.UpperRight);
            RectTransform daRT = dateText.GetComponent<RectTransform>();
            daRT.anchorMin = new Vector2(1, 0);
            daRT.anchorMax = new Vector2(1, 0.5f);
            daRT.pivot = new Vector2(1, 0.5f);
            daRT.anchoredPosition = new Vector2(-104, -3);
            daRT.sizeDelta = new Vector2(260, 0);

            // 退出按钮（危险色）
            Button quitBtn = UIFactory.CreateButton(bar, "QuitBtn", "退出", 14);
            RectTransform qRT = quitBtn.GetComponent<RectTransform>();
            qRT.anchorMin = new Vector2(1, 0.5f);
            qRT.anchorMax = new Vector2(1, 0.5f);
            qRT.pivot = new Vector2(1, 0.5f);
            qRT.anchoredPosition = new Vector2(-16, 0);
            qRT.sizeDelta = new Vector2(76, 34);
            Image qImg = quitBtn.GetComponent<Image>();
            qImg.color = Color.white; // 颜色交给 ColorBlock
            ColorBlock qcb = quitBtn.colors;
            qcb.normalColor = new Color(0.30f, 0.13f, 0.13f, 1f);
            qcb.highlightedColor = new Color(0.42f, 0.18f, 0.18f, 1f);
            qcb.pressedColor = new Color(0.50f, 0.20f, 0.20f, 1f);
            qcb.selectedColor = new Color(0.42f, 0.18f, 0.18f, 1f);
            quitBtn.colors = qcb;
            Text qLabel = quitBtn.GetComponentInChildren<Text>();
            if (qLabel != null) qLabel.color = UIFactory.Danger;
            quitBtn.onClick.AddListener(ShowQuitConfirm);

            // 顶部横向扫描光柱（UpdateVisualEffects 驱动，从左向右循环）
            GameObject scanGO = new GameObject("ScanLine", typeof(RectTransform), typeof(Image));
            scanGO.transform.SetParent(bar, false);
            Image scanImg = scanGO.GetComponent<Image>();
            scanImg.color = GlowLine;
            scanImg.raycastTarget = false;
            scanLineRT = scanGO.GetComponent<RectTransform>();
            scanLineRT.pivot = new Vector2(0.5f, 0.5f);
            scanLineRT.anchorMin = new Vector2(0f, 0f);
            scanLineRT.anchorMax = new Vector2(0f, 1f);
            scanLineRT.anchoredPosition = Vector2.zero;
            scanLineRT.sizeDelta = new Vector2(70f, 0f);
        }

        /// <summary>把一个 UI 元素铺满父级（可指定左右内边距）。</summary>
        private static void FillRect(Component c, float padX = 0f)
        {
            RectTransform rt = c.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(padX, 0);
            rt.offsetMax = new Vector2(-padX, 0);
        }

        /// <summary>创建一个左侧锚定的状态胶囊，返回其背景图（用于切换状态色）。</summary>
        private Image CreateChip(RectTransform parent, string name, string label, Color textColor,
            float x, float width, out Text text)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image bg = go.GetComponent<Image>();
            bg.color = new Color(0.1647f, 0.1882f, 0.2157f, 1f);
            bg.raycastTarget = false;
            UIFactory.ApplyRounded(bg);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0.5f);
            rt.anchorMax = new Vector2(0, 0.5f);
            rt.pivot = new Vector2(0, 0.5f);
            rt.anchoredPosition = new Vector2(x, 0);
            rt.sizeDelta = new Vector2(width, 26);

            text = UIFactory.CreateText(go.transform, "Text", label, 12, textColor, TextAnchor.MiddleCenter);
            RectTransform tRT = text.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = new Vector2(8, 0);
            tRT.offsetMax = new Vector2(-8, 0);
            return bg;
        }

        // ===================== 左侧栏 =====================
        private void BuildLeftSidebar()
        {
            RectTransform sidebar = UIFactory.CreatePanel(canvas.transform, "LeftSidebar", UIFactory.BgPanel, UIFactory.BorderCyan);
            sidebar.anchorMin = new Vector2(0, 0);
            sidebar.anchorMax = new Vector2(0, 1);
            sidebar.pivot = new Vector2(0, 0.5f);
            sidebar.anchoredPosition = new Vector2(BarMargin, (FooterHeight - TopBarHeight) * 0.5f);
            sidebar.sizeDelta = new Vector2(SideBarWidth, -(TopBarHeight + FooterHeight + BarMargin * 2));
            leftSidebarRect = sidebar;
            sidebarsInited = false;

            // 内容容器（避开边框和标题）
            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(sidebar, false);
            RectTransform content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(14, 14);
            content.offsetMax = new Vector2(-14, -14);

            float y = -4;

            // ---- 连接设置（IP / 端口运行中可修改）----
            AddSectionHeader(content, "连接设置", ref y);

            Text ipLabel = UIFactory.CreateText(content, "IpLabel", "IP 地址", 12, UIFactory.TextDim, TextAnchor.MiddleLeft);
            LayoutY(ipLabel, ref y, 15);

            ipInput = UIFactory.CreateInputField(content, "IpInput", robotDataReader != null ? robotDataReader.ServerIp : "10.130.38.131", 13);
            Layout(ipInput, ref y, 30);

            Text portLabel = UIFactory.CreateText(content, "PortLabel", "端口", 12, UIFactory.TextDim, TextAnchor.MiddleLeft);
            LayoutY(portLabel, ref y, 15);

            portInput = UIFactory.CreateInputField(content, "PortInput", robotDataReader != null ? robotDataReader.Port.ToString() : "8888", 13, InputField.ContentType.IntegerNumber);
            Layout(portInput, ref y, 30);

            connectBtn = UIFactory.CreateButton(content, "ConnectBtn", "连接设备", 14);
            Layout(connectBtn, ref y, 34);
            connectBtn.onClick.AddListener(OnConnectClicked);

            pipeStatusText = UIFactory.CreateText(content, "PipeStatus", "连接：未连接", 12, UIFactory.AccentOrange, TextAnchor.MiddleLeft);
            LayoutY(pipeStatusText, ref y, 20);

            y -= 20;

            // ---- 驾驶模式 ----
            AddSectionHeader(content, "驾驶模式", ref y);

            modeController = gameObject.AddComponent<ModeController>();
            modeController.Setup(car, cameraRotor);
            scriptRunner?.SetModeController(modeController);
            GameObject modeContainer = new GameObject("ModeContainer", typeof(RectTransform));
            modeContainer.transform.SetParent(content, false);
            RectTransform mc = modeContainer.GetComponent<RectTransform>();
            mc.anchorMin = new Vector2(0, 1);
            mc.anchorMax = new Vector2(1, 1);
            mc.pivot = new Vector2(0.5f, 1);
            mc.anchoredPosition = new Vector2(0, y);
            mc.sizeDelta = new Vector2(0, 246);
            modeController.Build(mc);
            y -= 252;

            y -= 20;

            // ---- 数字建模 ----
            AddSectionHeader(content, "数字建模", ref y);

            // 管道模型（动态半透明管道，点击切换显示）
            pipeModelBtn = UIFactory.CreateButton(content, "PipeModelBtn", "管道模型", 14);
            Layout(pipeModelBtn, ref y, 38);
            pipeModelBtn.onClick.AddListener(TogglePipeModel);

            // 点云重建
            Button rebuildBtn = UIFactory.CreateButton(content, "RebuildBtn", "点云重建", 14);
            Layout(rebuildBtn, ref y, 38);
            rebuildBtn.onClick.AddListener(OpenRebuildPanel);

            // 脚本编写
            Button scriptBtn = UIFactory.CreateButton(content, "ScriptBtn", "脚本编写", 14);
            Layout(scriptBtn, ref y, 38);
            scriptBtn.onClick.AddListener(OpenScriptPanel);

            // 运行输出（常驻显示脚本 print 的结果，不放在弹窗里）
            Text outLabel = UIFactory.CreateText(content, "ScriptOutLabel", "运行输出", 12, UIFactory.AccentCyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            LayoutY(outLabel, ref y, 18);

            GameObject outBox = new GameObject("ScriptOutputBox", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
            outBox.transform.SetParent(content, false);
            Image obImg = outBox.GetComponent<Image>();
            obImg.color = new Color(0.0784f, 0.0941f, 0.1137f, 1f);
            UIFactory.ApplyRounded(obImg);
            Layout(outBox.GetComponent<RectTransform>(), ref y, 120);

            scriptOutputScroll = outBox.GetComponent<ScrollRect>();
            scriptOutputScroll.horizontal = false;
            scriptOutputScroll.vertical = true;
            scriptOutputScroll.scrollSensitivity = 24f;
            scriptOutputScroll.movementType = ScrollRect.MovementType.Clamped;

            GameObject outVpGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            outVpGO.transform.SetParent(outBox.transform, false);
            RectTransform outVpRT = outVpGO.GetComponent<RectTransform>();
            outVpRT.anchorMin = Vector2.zero;
            outVpRT.anchorMax = Vector2.one;
            outVpRT.offsetMin = Vector2.zero;
            outVpRT.offsetMax = Vector2.zero;

            scriptOutputText = UIFactory.CreateText(outVpGO.transform, "OutputText", "（未注入脚本）", 12,
                new Color(0.78f, 0.85f, 0.95f, 1f), TextAnchor.UpperLeft);
            RectTransform sotRT = scriptOutputText.GetComponent<RectTransform>();
            sotRT.anchorMin = new Vector2(0f, 1f);
            sotRT.anchorMax = new Vector2(1f, 1f);
            sotRT.pivot = new Vector2(0.5f, 1f);
            sotRT.anchoredPosition = Vector2.zero;
            sotRT.sizeDelta = new Vector2(-16f, 0f);
            ContentSizeFitter outCsf = scriptOutputText.gameObject.AddComponent<ContentSizeFitter>();
            outCsf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            outCsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scriptOutputScroll.viewport = outVpRT;
            scriptOutputScroll.content = sotRT;
        }

        /// <summary>侧栏分区标题：左侧强调条 + 标题 + 底部细分隔线。</summary>
        private void AddSectionHeader(RectTransform parent, string title, ref float y)
        {
            GameObject go = new GameObject("Section_" + title, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, y);
            rt.sizeDelta = new Vector2(0, 30);

            GameObject accentGO = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accentGO.transform.SetParent(go.transform, false);
            Image aImg = accentGO.GetComponent<Image>();
            aImg.color = UIFactory.AccentCyan;
            aImg.raycastTarget = false;
            RectTransform aRT = accentGO.GetComponent<RectTransform>();
            aRT.anchorMin = new Vector2(0, 0.5f);
            aRT.anchorMax = new Vector2(0, 0.5f);
            aRT.pivot = new Vector2(0, 0.5f);
            aRT.anchoredPosition = Vector2.zero;
            aRT.sizeDelta = new Vector2(3, 15);

            Text t = UIFactory.CreateText(go.transform, "Title", title, 15, UIFactory.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRT = t.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = new Vector2(11, 0);
            tRT.offsetMax = Vector2.zero;

            GameObject lineGO = new GameObject("Line", typeof(RectTransform), typeof(Image));
            lineGO.transform.SetParent(go.transform, false);
            Image lImg = lineGO.GetComponent<Image>();
            lImg.color = UIFactory.BorderDim;
            lImg.raycastTarget = false;
            RectTransform lRT = lineGO.GetComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0, 0);
            lRT.anchorMax = new Vector2(1, 0);
            lRT.pivot = new Vector2(0.5f, 0);
            lRT.anchoredPosition = Vector2.zero;
            lRT.sizeDelta = new Vector2(0, 1);

            y -= 48;
        }

        private void LayoutY(Text t, ref float y, float h)
        {
            RectTransform r = t.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(0, y);
            r.sizeDelta = new Vector2(0, h);
            y -= h + 8;
        }

        private void Layout(Component comp, ref float y, float h)
        {
            RectTransform r = comp.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(0, y);
            r.sizeDelta = new Vector2(0, h);
            y -= h + 8;
        }

        // ===================== 中栏（读数 / 三维舞台 / 行驶状态） =====================
        private void BuildCenterColumn()
        {
            // RectMask2D：数据面板收起时把滑出的部分裁掉，避免越界盖住顶/底栏
            GameObject centerGO = new GameObject("CenterColumn", typeof(RectTransform), typeof(RectMask2D));
            centerGO.transform.SetParent(canvas.transform, false);
            RectTransform center = centerGO.GetComponent<RectTransform>();
            center.anchorMin = Vector2.zero;
            center.anchorMax = Vector2.one;
            center.offsetMin = new Vector2(SideBarWidth + BarMargin * 2, FooterHeight + BarMargin);
            center.offsetMax = new Vector2(-(SideBarWidth + BarMargin * 2), -(TopBarHeight + BarMargin));

            // 读数带（顶部数据面板，可收起，位于「三维孪生」标题栏下方）
            RectTransform readout = UIFactory.CreatePanel(center, "ReadoutBand", UIFactory.BgPanel, UIFactory.BorderCyan);
            readout.anchorMin = new Vector2(0, 1);
            readout.anchorMax = new Vector2(1, 1);
            readout.pivot = new Vector2(0.5f, 1);
            readout.anchoredPosition = new Vector2(0, -StageToolbarHeight);
            readout.sizeDelta = new Vector2(0, ReadoutBandHeight);
            readoutBandRect = readout;
            BuildReadoutBand(readout);

            // 行驶状态带（底部数据面板，可收起，位于坐标条上方）
            RectTransform travel = UIFactory.CreatePanel(center, "TravelBand", UIFactory.BgPanel, UIFactory.BorderCyan);
            travel.anchorMin = new Vector2(0, 0);
            travel.anchorMax = new Vector2(1, 0);
            travel.pivot = new Vector2(0.5f, 0);
            travel.anchoredPosition = new Vector2(0, StageCoordsHeight);
            travel.sizeDelta = new Vector2(0, TravelBandHeight);
            travelBandRect = travel;
            BuildTravelBand(travel);

            // 中间：三维舞台（透明，透出 3D 场景）
            GameObject stageGO = new GameObject("Stage", typeof(RectTransform));
            stageGO.transform.SetParent(center, false);
            RectTransform stage = stageGO.GetComponent<RectTransform>();
            stage.anchorMin = Vector2.zero;
            stage.anchorMax = Vector2.one;
            stage.offsetMin = new Vector2(0, StageCoordsHeight + TravelBandHeight + 24f);
            stage.offsetMax = new Vector2(0, -(StageToolbarHeight + ReadoutBandHeight + 24f));
            stageRect = stage;

            // 「三维孪生」标题栏（最顶）+ 坐标条（最底）——放在数据面板之后，收起时数据面板滑到其后面
            BuildStageChrome(center);

            // 两个数据面板的收起 / 展开把手（放最上层）
            topDataToggleRect = CreateDataBandToggle(center, "TopDataToggle", "▲", true);
            bottomDataToggleRect = CreateDataBandToggle(center, "BottomDataToggle", "▼", false);
        }

        private void BuildReadoutBand(RectTransform band)
        {
            Text heading = UIFactory.CreateText(band, "Heading", "检测数据", 15, UIFactory.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform hRT = heading.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 0);
            hRT.anchorMax = new Vector2(0, 1);
            hRT.pivot = new Vector2(0, 0.5f);
            hRT.anchoredPosition = new Vector2(20, 8);
            hRT.sizeDelta = new Vector2(120, -28);

            Text sub = UIFactory.CreateText(band, "Sub", "实时检测指标", 11, UIFactory.TextDim, TextAnchor.MiddleLeft);
            RectTransform subRT = sub.GetComponent<RectTransform>();
            subRT.anchorMin = new Vector2(0, 0);
            subRT.anchorMax = new Vector2(0, 1);
            subRT.pivot = new Vector2(0, 0.5f);
            subRT.anchoredPosition = new Vector2(20, -14);
            subRT.sizeDelta = new Vector2(120, -28);

            GameObject metricsGO = new GameObject("Metrics", typeof(RectTransform));
            metricsGO.transform.SetParent(band, false);
            RectTransform metrics = metricsGO.GetComponent<RectTransform>();
            metrics.anchorMin = new Vector2(0, 0);
            metrics.anchorMax = new Vector2(1, 1);
            metrics.offsetMin = new Vector2(150, 14);
            metrics.offsetMax = new Vector2(-160, -14);

            mainDeviationText = CreateReadoutMetric(metrics, "偏差", "0.00", UIFactory.AccentCyan, 0, 4);
            mainEllipticityText = CreateReadoutMetric(metrics, "椭圆度", "0.00", UIFactory.AccentGreen, 1, 4);
            mainConfidenceText = CreateReadoutMetric(metrics, "置信度", "0.00", UIFactory.AccentOrange, 2, 4);
            mainRadiusText = CreateReadoutMetric(metrics, "半径", "0.00", UIFactory.AccentPurple, 3, 4);

            Button chartBtn = UIFactory.CreateButton(band, "ChartBtn", "数据曲线", 13);
            RectTransform cbRT = chartBtn.GetComponent<RectTransform>();
            cbRT.anchorMin = new Vector2(1, 0.5f);
            cbRT.anchorMax = new Vector2(1, 0.5f);
            cbRT.pivot = new Vector2(1, 0.5f);
            cbRT.anchoredPosition = new Vector2(-18, 0);
            cbRT.sizeDelta = new Vector2(120, 38);
            chartBtn.onClick.AddListener(ShowChartPanel);
        }

        /// <summary>读数带里的单个指标（标签 + 大号数值），按列等分。</summary>
        private Text CreateReadoutMetric(RectTransform parent, string label, string initValue, Color color, int col, int count)
        {
            GameObject go = new GameObject("Metric_" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(col / (float)count, 0);
            rt.anchorMax = new Vector2((col + 1) / (float)count, 1);
            rt.offsetMin = new Vector2(20, 0);
            rt.offsetMax = new Vector2(0, 0);

            if (col > 0)
            {
                GameObject lineGO = new GameObject("Sep", typeof(RectTransform), typeof(Image));
                lineGO.transform.SetParent(go.transform, false);
                Image lImg = lineGO.GetComponent<Image>();
                lImg.color = UIFactory.BorderCyan;
                lImg.raycastTarget = false;
                RectTransform lRT = lineGO.GetComponent<RectTransform>();
                lRT.anchorMin = new Vector2(0, 0.15f);
                lRT.anchorMax = new Vector2(0, 0.85f);
                lRT.pivot = new Vector2(0, 0.5f);
                lRT.anchoredPosition = Vector2.zero;
                lRT.sizeDelta = new Vector2(1, 0);
            }

            Text lab = UIFactory.CreateText(go.transform, "Label", label, 12, UIFactory.TextDim, TextAnchor.LowerLeft);
            RectTransform labRT = lab.GetComponent<RectTransform>();
            labRT.anchorMin = new Vector2(0, 0.5f);
            labRT.anchorMax = new Vector2(1, 1);
            labRT.offsetMin = Vector2.zero;
            labRT.offsetMax = Vector2.zero;

            Text val = UIFactory.CreateText(go.transform, "Value", initValue, 26, color, TextAnchor.UpperLeft);
            RectTransform vRT = val.GetComponent<RectTransform>();
            vRT.anchorMin = new Vector2(0, 0);
            vRT.anchorMax = new Vector2(1, 0.55f);
            vRT.offsetMin = Vector2.zero;
            vRT.offsetMax = Vector2.zero;

            return val;
        }

        /// <summary>中栏的「三维孪生」标题栏（最顶）与坐标条（最底）。</summary>
        private void BuildStageChrome(RectTransform center)
        {
            // 「三维孪生」标题栏（最顶）
            RectTransform toolbar = UIFactory.CreatePanel(center, "StageToolbar", new Color(0.1529f, 0.1725f, 0.1961f, 1f), UIFactory.BorderCyan);
            toolbar.anchorMin = new Vector2(0, 1);
            toolbar.anchorMax = new Vector2(1, 1);
            toolbar.pivot = new Vector2(0.5f, 1);
            toolbar.anchoredPosition = Vector2.zero;
            toolbar.sizeDelta = new Vector2(0, StageToolbarHeight);

            Text th = UIFactory.CreateText(toolbar, "Title", "三维孪生", 14, UIFactory.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform thRT = th.GetComponent<RectTransform>();
            thRT.anchorMin = new Vector2(0, 0);
            thRT.anchorMax = new Vector2(0.5f, 1);
            thRT.pivot = new Vector2(0, 0.5f);
            thRT.anchoredPosition = new Vector2(18, 0);
            thRT.sizeDelta = new Vector2(300, 0);

            stageModeText = UIFactory.CreateText(toolbar, "Mode", "手动模式", 12, UIFactory.TextDim, TextAnchor.MiddleRight);
            RectTransform smRT = stageModeText.GetComponent<RectTransform>();
            smRT.anchorMin = new Vector2(0.5f, 0);
            smRT.anchorMax = new Vector2(1, 1);
            smRT.pivot = new Vector2(1, 0.5f);
            smRT.anchoredPosition = new Vector2(-18, 0);
            smRT.sizeDelta = new Vector2(300, 0);

            // 坐标条（最底）
            RectTransform coords = UIFactory.CreatePanel(center, "Coordinates", new Color(0.1451f, 0.1608f, 0.1804f, 1f), UIFactory.BorderDim);
            coords.anchorMin = new Vector2(0, 0);
            coords.anchorMax = new Vector2(1, 0);
            coords.pivot = new Vector2(0.5f, 0);
            coords.anchoredPosition = Vector2.zero;
            coords.sizeDelta = new Vector2(0, StageCoordsHeight);

            positionText = UIFactory.CreateText(coords, "Position", "X 0.0    Y 0.0    Z 0.0", 12, UIFactory.TextDim, TextAnchor.MiddleLeft);
            RectTransform pRT = positionText.GetComponent<RectTransform>();
            pRT.anchorMin = Vector2.zero;
            pRT.anchorMax = Vector2.one;
            pRT.offsetMin = new Vector2(18, 0);
            pRT.offsetMax = new Vector2(-18, 0);
        }

        private void BuildTravelBand(RectTransform band)
        {
            Text head = UIFactory.CreateText(band, "Head", "行驶状态", 14, UIFactory.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform hRT = head.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0, 1);
            hRT.anchorMax = new Vector2(0, 1);
            hRT.pivot = new Vector2(0, 1);
            hRT.anchoredPosition = new Vector2(20, -16);
            hRT.sizeDelta = new Vector2(90, 24);

            speedText = UIFactory.CreateText(band, "Speed", "速度 0.0 cm/s", 19, UIFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform sRT = speedText.GetComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0, 1);
            sRT.anchorMax = new Vector2(0, 1);
            sRT.pivot = new Vector2(0, 1);
            sRT.anchoredPosition = new Vector2(120, -16);
            sRT.sizeDelta = new Vector2(200, 24);

            distanceText = UIFactory.CreateText(band, "Distance", "里程 0.0 cm", 19, UIFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform dRT = distanceText.GetComponent<RectTransform>();
            dRT.anchorMin = new Vector2(0, 1);
            dRT.anchorMax = new Vector2(0, 1);
            dRT.pivot = new Vector2(0, 1);
            dRT.anchoredPosition = new Vector2(340, -16);
            dRT.sizeDelta = new Vector2(200, 24);

            travelStateText = UIFactory.CreateText(band, "State", "静止", 13, UIFactory.TextDim, TextAnchor.MiddleRight);
            RectTransform tRT = travelStateText.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(1, 1);
            tRT.anchorMax = new Vector2(1, 1);
            tRT.pivot = new Vector2(1, 1);
            tRT.anchoredPosition = new Vector2(-20, -16);
            tRT.sizeDelta = new Vector2(160, 24);

            GameObject lineGO = new GameObject("Line", typeof(RectTransform), typeof(Image));
            lineGO.transform.SetParent(band, false);
            Image lImg = lineGO.GetComponent<Image>();
            lImg.color = UIFactory.BorderDim;
            lImg.raycastTarget = false;
            RectTransform lRT = lineGO.GetComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0, 1);
            lRT.anchorMax = new Vector2(1, 1);
            lRT.pivot = new Vector2(0.5f, 1);
            lRT.anchoredPosition = new Vector2(0, -46);
            lRT.sizeDelta = new Vector2(-40, 1);

            GameObject speedChartGO = new GameObject("SpeedChart", typeof(RectTransform), typeof(RawImage));
            speedChartGO.transform.SetParent(band, false);
            RectTransform scRT = speedChartGO.GetComponent<RectTransform>();
            scRT.anchorMin = new Vector2(0, 0);
            scRT.anchorMax = new Vector2(0.5f, 1);
            scRT.offsetMin = new Vector2(20, 14);
            scRT.offsetMax = new Vector2(-12, -54);
            speedChart = gameObject.AddComponent<DataChart>();
            speedChart.Build(scRT, UIFactory.AccentCyan, car != null ? Mathf.Max(1f, car.MaxSpeed) : 10f);

            GameObject distChartGO = new GameObject("DistChart", typeof(RectTransform), typeof(RawImage));
            distChartGO.transform.SetParent(band, false);
            RectTransform dcRT = distChartGO.GetComponent<RectTransform>();
            dcRT.anchorMin = new Vector2(0.5f, 0);
            dcRT.anchorMax = new Vector2(1, 1);
            dcRT.offsetMin = new Vector2(12, 14);
            dcRT.offsetMax = new Vector2(-20, -54);
            distanceChart = gameObject.AddComponent<DataChart>();
            distanceChart.Build(dcRT, UIFactory.AccentGreen, -1f);
        }

        // ===================== 右侧栏（摄像头识别 + 雷达扫描） =====================
        private void BuildRightColumn()
        {
            RectTransform column = UIFactory.CreatePanel(canvas.transform, "RightColumn", UIFactory.BgPanel, UIFactory.BorderCyan);
            column.anchorMin = new Vector2(1, 0);
            column.anchorMax = new Vector2(1, 1);
            column.pivot = new Vector2(1, 0.5f);
            column.anchoredPosition = new Vector2(-BarMargin, (FooterHeight - TopBarHeight) * 0.5f);
            column.sizeDelta = new Vector2(SideBarWidth, -(TopBarHeight + FooterHeight + BarMargin * 2));

            rightSidebarRect = column;
            sidebarsInited = false;

            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(column, false);
            RectTransform content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(14, 14);
            content.offsetMax = new Vector2(-14, -14);

            // 摄像头识别（上）
            RectTransform camPanel = CreateSubPanel(content, "CameraPanel", "摄像头识别",
                new Vector2(0, 0.55f), new Vector2(1, 1f), Vector2.zero, Vector2.zero);
            BuildCameraPanel(camPanel);

            // 雷达扫描（下）
            RectTransform radarPanel = CreateSubPanel(content, "RadarPanel", "雷达扫描 · 点云",
                new Vector2(0, 0f), new Vector2(1, 0.53f), Vector2.zero, Vector2.zero);
            radar = gameObject.AddComponent<RadarScanner>();
            radar.Build(radarPanel, canvas);
        }

        private RectTransform CreateSubPanel(RectTransform parent, string name, string title,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform panel = UIFactory.CreatePanel(parent, name, UIFactory.BgDark, UIFactory.BorderDim);
            panel.anchorMin = anchorMin;
            panel.anchorMax = anchorMax;
            panel.offsetMin = offsetMin;
            panel.offsetMax = offsetMax;

            UIFactory.AddPanelHeader(panel, title);

            // 内容区
            GameObject bodyGO = new GameObject("Body", typeof(RectTransform));
            bodyGO.transform.SetParent(panel, false);
            RectTransform body = bodyGO.GetComponent<RectTransform>();
            body.anchorMin = new Vector2(0, 0);
            body.anchorMax = new Vector2(1, 1);
            body.offsetMin = new Vector2(12, 12);
            body.offsetMax = new Vector2(-12, -48);

            return body;
        }

        private void BuildCameraPanel(RectTransform body)
        {
            // 顶部元信息行：检测画面 / 云台角度
            Text feedLabel = UIFactory.CreateText(body, "FeedLabel", "检测画面", 11, UIFactory.TextDim, TextAnchor.MiddleLeft);
            RectTransform flRT = feedLabel.GetComponent<RectTransform>();
            flRT.anchorMin = new Vector2(0, 1);
            flRT.anchorMax = new Vector2(0.5f, 1);
            flRT.pivot = new Vector2(0, 1);
            flRT.anchoredPosition = Vector2.zero;
            flRT.sizeDelta = new Vector2(0, 18);

            cameraAngleText = UIFactory.CreateText(body, "GimbalAngle", "云台 0°", 11, UIFactory.TextDim, TextAnchor.MiddleRight);
            RectTransform aRT = cameraAngleText.GetComponent<RectTransform>();
            aRT.anchorMin = new Vector2(0.5f, 1);
            aRT.anchorMax = new Vector2(1, 1);
            aRT.pivot = new Vector2(1, 1);
            aRT.anchoredPosition = Vector2.zero;
            aRT.sizeDelta = new Vector2(0, 18);

            // 摄像头画面
            GameObject camGO = new GameObject("CameraFeed", typeof(RectTransform), typeof(RawImage));
            camGO.transform.SetParent(body, false);
            cameraImage = camGO.GetComponent<RawImage>();
            cameraImage.color = new Color(0.078f, 0.090f, 0.106f, 1f);
            cameraImage.raycastTarget = false;

            RectTransform camRT = camGO.GetComponent<RectTransform>();
            camRT.anchorMin = new Vector2(0, 0.26f);
            camRT.anchorMax = new Vector2(1, 1);
            camRT.offsetMin = new Vector2(0, 4);
            camRT.offsetMax = new Vector2(0, -22);

            AspectRatioFitter fitter = camGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;

            // 检测框覆盖层
            GameObject overlayGO = new GameObject("DetectionOverlay", typeof(RectTransform));
            overlayGO.transform.SetParent(camGO.transform, false);
            cameraOverlay = overlayGO;
            RectTransform ovRT = overlayGO.GetComponent<RectTransform>();
            ovRT.anchorMin = Vector2.zero;
            ovRT.anchorMax = Vector2.one;
            ovRT.offsetMin = Vector2.zero;
            ovRT.offsetMax = Vector2.zero;

            // 识别结果
            recognitionText = UIFactory.CreateText(body, "Recognition", "识别结果：等待画面…", 12, UIFactory.TextPrimary, TextAnchor.UpperLeft);
            RectTransform rRT = recognitionText.GetComponent<RectTransform>();
            rRT.anchorMin = new Vector2(0, 0);
            rRT.anchorMax = new Vector2(1, 0.24f);
            rRT.offsetMin = new Vector2(0, 4);
            rRT.offsetMax = new Vector2(0, -2);
            recognitionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            recognitionText.verticalOverflow = VerticalWrapMode.Truncate;
        }

        // ===================== 底部状态栏 =====================
        private void BuildFooter()
        {
            RectTransform footer = UIFactory.CreatePanel(canvas.transform, "Footer", UIFactory.BgDark, UIFactory.BorderCyan, false);
            footer.anchorMin = new Vector2(0, 0);
            footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(0.5f, 0);
            footer.anchoredPosition = Vector2.zero;
            footer.sizeDelta = new Vector2(0, FooterHeight);

            footerText = UIFactory.CreateText(footer, "Source", "● 本地模式 · 未接入设备", 11, UIFactory.TextDim, TextAnchor.MiddleLeft);
            RectTransform sRT = footerText.GetComponent<RectTransform>();
            sRT.anchorMin = Vector2.zero;
            sRT.anchorMax = Vector2.one;
            sRT.offsetMin = new Vector2(16, 0);
            sRT.offsetMax = new Vector2(-260, 0);

            Text tag = UIFactory.CreateText(footer, "Tag", "管道检测 / 数字孪生", 11, UIFactory.TextDim, TextAnchor.MiddleRight);
            RectTransform tRT = tag.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = new Vector2(200, 0);
            tRT.offsetMax = new Vector2(-16, 0);
        }

        // ===================== 侧栏展开 / 收起 =====================
        /// <summary>在左右侧栏的内侧边缘各放一个收起/展开把手。</summary>
        private void BuildSidebarToggles()
        {
            leftToggleRect = CreateSidebarToggle("LeftToggle", "◀", new Vector2(0f, 0.5f), true);
            rightToggleRect = CreateSidebarToggle("RightToggle", "▶", new Vector2(1f, 0.5f), false);
        }

        private RectTransform CreateSidebarToggle(string name, string label, Vector2 anchor, bool left)
        {
            Button btn = UIFactory.CreateButton(canvas.transform, name, label, 15);
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = anchor;
            rt.sizeDelta = new Vector2(24f, 84f);
            rt.anchoredPosition = Vector2.zero;

            Image img = btn.GetComponent<Image>();
            img.color = new Color(0.05f, 0.22f, 0.30f, 0.92f);

            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.6f, 1.15f, 1.35f, 1f);
            cb.pressedColor = new Color(0.3f, 0.85f, 1f, 1f);
            cb.selectedColor = Color.white;
            btn.colors = cb;

            Text t = btn.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = UIFactory.AccentCyan;
                t.fontStyle = FontStyle.Bold;
            }

            if (left)
            {
                leftToggleLabel = t;
            }
            else
            {
                rightToggleLabel = t;
            }

            btn.onClick.AddListener(() => ToggleSidebar(left));
            return rt;
        }

        private void ToggleSidebar(bool left)
        {
            if (left)
            {
                leftCollapsed = !leftCollapsed;
                leftTargetX = leftCollapsed ? -SideBarWidth : BarMargin;
            }
            else
            {
                rightCollapsed = !rightCollapsed;
                rightTargetX = rightCollapsed ? SideBarWidth : -BarMargin;
            }
        }

        /// <summary>驱动侧栏横向滑入 / 滑出，并让把手始终贴在侧栏内侧边缘。</summary>
        private void UpdateSidebars()
        {
            if (leftSidebarRect == null || rightSidebarRect == null)
            {
                return;
            }

            if (!sidebarsInited)
            {
                sidebarsInited = true;
                leftPosX = leftTargetX = BarMargin;
                rightPosX = rightTargetX = -BarMargin;
            }

            leftPosX = Mathf.MoveTowards(leftPosX, leftTargetX, SidebarAnimSpeed * Time.deltaTime);
            rightPosX = Mathf.MoveTowards(rightPosX, rightTargetX, SidebarAnimSpeed * Time.deltaTime);

            Vector2 lp = leftSidebarRect.anchoredPosition;
            lp.x = leftPosX;
            leftSidebarRect.anchoredPosition = lp;

            Vector2 rp = rightSidebarRect.anchoredPosition;
            rp.x = rightPosX;
            rightSidebarRect.anchoredPosition = rp;

            if (leftToggleRect != null)
            {
                leftToggleRect.anchoredPosition = new Vector2(leftPosX + SideBarWidth, 0f);
            }
            if (rightToggleRect != null)
            {
                rightToggleRect.anchoredPosition = new Vector2(rightPosX - SideBarWidth, 0f);
            }

            if (leftToggleLabel != null)
            {
                leftToggleLabel.text = leftCollapsed ? "▶" : "◀";
            }
            if (rightToggleLabel != null)
            {
                rightToggleLabel.text = rightCollapsed ? "◀" : "▶";
            }
        }

        // ===================== 数据面板展开 / 收起 =====================
        private RectTransform CreateDataBandToggle(RectTransform parent, string name, string label, bool top)
        {
            Button btn = UIFactory.CreateButton(parent, name, label, 13);
            RectTransform rt = btn.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, top ? 1f : 0f);
            rt.anchorMax = rt.anchorMin;
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(78f, 22f);
            rt.anchoredPosition = top
                ? new Vector2(0f, -(StageToolbarHeight + ReadoutBandHeight))
                : new Vector2(0f, StageCoordsHeight + TravelBandHeight);

            Image img = btn.GetComponent<Image>();
            img.color = new Color(0.1647f, 0.1882f, 0.2157f, 0.96f);

            ColorBlock cb = btn.colors;
            cb.normalColor = Color.white;
            cb.highlightedColor = new Color(0.65f, 0.80f, 1f, 1f);
            cb.pressedColor = new Color(0.43f, 0.66f, 0.95f, 1f);
            cb.selectedColor = Color.white;
            btn.colors = cb;

            Text t = btn.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.color = UIFactory.AccentCyan;
                t.fontStyle = FontStyle.Bold;
            }

            if (top) topDataToggleLabel = t; else bottomDataToggleLabel = t;
            btn.onClick.AddListener(() => ToggleDataBand(top));
            return rt;
        }

        private void ToggleDataBand(bool top)
        {
            if (top) readoutCollapsed = !readoutCollapsed;
            else travelCollapsed = !travelCollapsed;
        }

        /// <summary>驱动「检测数据」「行驶状态」两个数据面板纵向滑出滑入，并让三维舞台随之伸缩。</summary>
        private void UpdateDataBands()
        {
            if (readoutBandRect == null || travelBandRect == null || stageRect == null)
            {
                return;
            }

            float targetR = readoutCollapsed ? 1f : 0f;
            float targetT = travelCollapsed ? 1f : 0f;
            readoutT = Mathf.MoveTowards(readoutT, targetR, Time.deltaTime * 6f);
            travelT = Mathf.MoveTowards(travelT, targetT, Time.deltaTime * 6f);

            // 检测数据面板：向上滑到「三维孪生」标题栏后面
            Vector2 rp = readoutBandRect.anchoredPosition;
            rp.y = -StageToolbarHeight + readoutT * (ReadoutBandHeight + StageToolbarHeight);
            readoutBandRect.anchoredPosition = rp;

            // 行驶状态面板：向下滑到坐标条后面
            Vector2 tp = travelBandRect.anchoredPosition;
            tp.y = StageCoordsHeight - travelT * (TravelBandHeight + StageCoordsHeight);
            travelBandRect.anchoredPosition = tp;

            // 三维舞台随两个面板的收起 / 展开而伸缩（始终为把手预留高度）
            float topGap = Mathf.Lerp(StageToolbarHeight + ReadoutBandHeight + 24f, StageToolbarHeight + 24f, readoutT);
            float bottomGap = Mathf.Lerp(StageCoordsHeight + TravelBandHeight + 24f, StageCoordsHeight + 24f, travelT);
            stageRect.offsetMax = new Vector2(0f, -topGap);
            stageRect.offsetMin = new Vector2(0f, bottomGap);

            // 把手贴在数据面板的内边缘
            if (topDataToggleRect != null)
            {
                topDataToggleRect.anchoredPosition = new Vector2(0f, -StageToolbarHeight - (1f - readoutT) * ReadoutBandHeight);
            }
            if (bottomDataToggleRect != null)
            {
                bottomDataToggleRect.anchoredPosition = new Vector2(0f, StageCoordsHeight + (1f - travelT) * TravelBandHeight);
            }

            if (topDataToggleLabel != null)
            {
                topDataToggleLabel.text = readoutCollapsed ? "▼" : "▲";
            }
            if (bottomDataToggleLabel != null)
            {
                bottomDataToggleLabel.text = travelCollapsed ? "▲" : "▼";
            }
        }

        // ===================== 摄像头接入 =====================
        private void WireCamera()
        {
            if (cameraReceiver != null)
            {
                cameraReceiver.OnFrameReceived -= OnCameraFrame;
                cameraReceiver.OnFrameReceived += OnCameraFrame;
                cameraReceiver.OnDetectionsReceived -= OnDetections;
                cameraReceiver.OnDetectionsReceived += OnDetections;
            }
        }

        private void OnCameraFrame(Texture2D tex)
        {
            if (cameraImage == null) return;
            cameraTexture = tex;
            cameraImage.texture = tex;
            cameraImage.color = Color.white;

            AspectRatioFitter f = cameraImage.GetComponent<AspectRatioFitter>();
            if (f != null && tex.width > 0) f.aspectRatio = (float)tex.width / tex.height;
        }

        private void OnDetections(List<Detection> dets)
        {
            // 下方文字区显示汇总
            if (recognitionText != null)
            {
                if (dets == null || dets.Count == 0)
                {
                    recognitionText.text = "识别结果：未检测到缺陷";
                }
                else
                {
                    string result = $"识别结果：{dets.Count} 个缺陷";
                    int n = Mathf.Min(dets.Count, 5);
                    for (int i = 0; i < n; i++)
                    {
                        Detection d = dets[i];
                        string name = string.IsNullOrEmpty(d.class_name) ? ("类别" + d.class_id) : d.class_name;
                        result += $"\n  {name}  {d.score:0.00}";
                    }
                    recognitionText.text = result;
                }
            }

            // 缺陷标注：标红点云 + 保存帧图片
            if (dets != null && dets.Count > 0)
            {
                MarkDefects(dets);
            }

            // 在画面上叠加检测框与标签
            DrawOverlay(dets);
        }

        /// <summary>缺陷记录（环向角度 + 帧图片，用于双击查看中间帧）。</summary>
        private class DefectRecord
        {
            public float angleDeg;    // 缺陷在管道上的环向角度（0~360，0=小车局部 +Y，正向 +X）
            public float rangeDeg;    // 缺陷环向角宽
            public int markCount;     // 已标红的截面数（从 startSection 起连续）
            public int startSection;  // 首次检测到该缺陷时的小车截面
            public readonly List<Texture2D> frames = new List<Texture2D>();
        }

        /// <summary>
        /// 检测到缺陷时，用「里程计截面 + 云台环向角」直接算出缺陷在管道参数空间中的 (截面, 角度)，
        /// 并标红对应位置、保存帧图片。不依赖相机 3D 朝向，比射线求交更可靠。
        /// </summary>
        private void MarkDefects(List<Detection> dets)
        {
            if (pointCloudRebuilder == null || cameraRotor == null)
            {
                return;
            }
            int currentSection = pointCloudRebuilder.CurrentSectionIndex;
            if (currentSection < 0)
            {
                return; // 还没有累积任何截面
            }

            Texture tex = cameraImage != null ? cameraImage.texture : null;
            int texW = tex != null && tex.width > 0 ? tex.width : 640;

            foreach (Detection d in dets)
            {
                // 只标注置信度高于阈值的缺陷
                if (d.score < defectScoreThreshold)
                {
                    continue;
                }

                float cx = d.x + d.w * 0.5f;

                // 环向角度 = 云台绕管道轴(Z)的旋转角 + 画面横向偏移 + 标定偏移
                float hOffset = (cx / texW - 0.5f) * cameraFov;
                float angle = NormalizeAngle(cameraRotor.CurrentAngle + hOffset + gimbalAngleOffset);

                // 缺陷环向角宽（横向像素 → 角度）
                float range = Mathf.Max(4f, d.w / (float)texW * cameraFov);

                // 同一缺陷（环向角度接近）跨帧合并；轴向用里程计截面累积
                DefectRecord rec = FindDefect(angle);
                if (rec == null)
                {
                    rec = new DefectRecord
                    {
                        angleDeg = angle,
                        rangeDeg = range,
                        startSection = currentSection
                    };
                    defects.Add(rec);
                    AddDefectButton(rec);
                }

                // 限制每个缺陷的标红长度，避免持续多帧累积过长
                if (rec.markCount < maxMarkSections)
                {
                    pointCloudRebuilder.MarkDefectAt(currentSection, angle, range);
                    rec.markCount++;
                }

                RecordDefectFrame(rec, tex);
            }
        }

        private void RecordDefectFrame(DefectRecord rec, Texture tex)
        {
            if (tex == null)
            {
                return;
            }

            if (rec.frames.Count < maxDefectFrames)
            {
                Texture2D clone = CloneTexture(tex);
                if (clone != null)
                {
                    rec.frames.Add(clone);
                }
            }
        }

        private DefectRecord FindDefect(float angle)
        {
            foreach (DefectRecord rec in defects)
            {
                if (Mathf.Abs(Mathf.DeltaAngle(rec.angleDeg, angle)) < 10f)
                {
                    return rec;
                }
            }
            return null;
        }

        /// <summary>把角度归一化到 0~360。</summary>
        private static float NormalizeAngle(float a)
        {
            return Mathf.Repeat(a, 360f);
        }

        private Texture2D CloneTexture(Texture tex)
        {
            Texture2D src = tex as Texture2D;
            if (src == null)
            {
                return null;
            }
            Texture2D clone = new Texture2D(src.width, src.height, src.format, false);
            clone.SetPixels32(src.GetPixels32());
            clone.Apply();
            return clone;
        }

        /// <summary>在摄像头画面上绘制检测框与类别标签（对应 Maix 的 draw_rect / draw_string）。</summary>
        private void DrawOverlay(List<Detection> dets)
        {
            for (int i = 0; i < overlayBoxes.Count; i++)
            {
                if (overlayBoxes[i] != null) Destroy(overlayBoxes[i]);
            }
            overlayBoxes.Clear();

            if (cameraOverlay == null || cameraImage == null || dets == null || dets.Count == 0)
            {
                return;
            }

            Texture tex = cameraImage.texture;
            if (tex == null || tex.width <= 0 || tex.height <= 0)
            {
                return;
            }

            RectTransform camRT = cameraImage.rectTransform;
            Vector2 size = camRT.rect.size;
            if (size.x <= 0 || size.y <= 0)
            {
                return;
            }

            int texW = tex.width;
            int texH = tex.height;

            foreach (Detection d in dets)
            {
                // 像素坐标 → overlay 本地坐标（UI Y 轴向上，图像 Y 轴向下，需翻转）
                float left = d.x / (float)texW * size.x;
                float bottom = size.y - (d.y + d.h) / (float)texH * size.y;
                float w = d.w / (float)texW * size.x;
                float h = d.h / (float)texH * size.y;

                CreateOverlayBox(left, bottom, w, h, d);
            }
        }

        private void CreateOverlayBox(float left, float bottom, float w, float h, Detection d)
        {
            GameObject box = new GameObject("DetectionBox", typeof(RectTransform));
            box.transform.SetParent(cameraOverlay.transform, false);
            RectTransform rt = box.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(left, bottom);
            rt.sizeDelta = new Vector2(w, h);
            overlayBoxes.Add(box);

            string name = string.IsNullOrEmpty(d.class_name) ? ("类别" + d.class_id) : d.class_name;

            // 标签背景
            GameObject bgGO = new GameObject("LabelBG", typeof(RectTransform), typeof(Image));
            bgGO.transform.SetParent(box.transform, false);
            Image bg = bgGO.GetComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.65f);
            bg.raycastTarget = false;
            RectTransform bgRT = bgGO.GetComponent<RectTransform>();
            bgRT.anchorMin = new Vector2(0, 1);
            bgRT.anchorMax = new Vector2(0, 1);
            bgRT.pivot = new Vector2(0, 1);
            bgRT.anchoredPosition = new Vector2(-2, 2);
            bgRT.sizeDelta = new Vector2(120, 20);

            // 标签文字
            Text label = UIFactory.CreateText(box.transform, "Label", $"{name} {d.score:0.00}", 12, Color.white, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform lRT = label.GetComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0, 1);
            lRT.anchorMax = new Vector2(0, 1);
            lRT.pivot = new Vector2(0, 1);
            lRT.anchoredPosition = new Vector2(0, 2);
            lRT.sizeDelta = new Vector2(120, 20);
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 四边边框（红色，突出缺陷）
            Color border = new Color(1f, 0.3f, 0.3f, 0.95f);
            float t = 2f;
            AddOverlayBar(box.transform, "Top", new Vector2(0, h - t), new Vector2(w, t), border);
            AddOverlayBar(box.transform, "Bottom", new Vector2(0, 0), new Vector2(w, t), border);
            AddOverlayBar(box.transform, "Left", new Vector2(0, 0), new Vector2(t, h), border);
            AddOverlayBar(box.transform, "Right", new Vector2(w - t, 0), new Vector2(t, h), border);
        }

        private void AddOverlayBar(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        // ===================== 小车数据接入 =====================
        private void WireRobotData()
        {
            if (robotDataReader != null)
            {
                robotDataReader.OnImageReceived -= OnRobotImage;
                robotDataReader.OnImageReceived += OnRobotImage;
                robotDataReader.OnPointCloudReceived -= OnPointCloud;
                robotDataReader.OnPointCloudReceived += OnPointCloud;
            }
        }

        private void OnRobotImage(Texture2D tex)
        {
            if (cameraImage == null || tex == null)
            {
                return;
            }

            cameraImage.texture = tex;
            cameraImage.color = Color.white;

            AspectRatioFitter f = cameraImage.GetComponent<AspectRatioFitter>();
            if (f != null && tex.width > 0)
            {
                f.aspectRatio = (float)tex.width / tex.height;
            }
        }

        private void OnPointCloud(StatusPacket pkt)
        {
            if (radar == null || pkt == null || pkt.scan == null)
            {
                return;
            }

            pointCloudActive = true;
            radar.FeedPolarCloud(pkt.scan.angles, pkt.scan.distances);
        }

        // ===================== 刷新 =====================
        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            if (timeText != null)
            {
                timeText.text = $"{now:HH:mm:ss}";
            }
            if (dateText != null)
            {
                dateText.text = $"{now:yyyy-MM-dd}  {GetWeekday(now.DayOfWeek)}";
            }
        }

        private string GetWeekday(DayOfWeek d)
        {
            string[] names = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            return names[(int)d];
        }

        private void UpdateTelemetry()
        {
            if (car != null)
            {
                if (speedText != null) speedText.text = $"速度 {car.Speed:0.0} cm/s";
                if (distanceText != null) distanceText.text = $"里程 {car.TotalDistance:0.0} cm";
                if (speedChart != null) speedChart.AddValue(car.Speed);
                if (distanceChart != null) distanceChart.AddValue(car.TotalDistance);

                if (travelStateText != null)
                {
                    bool moving = car.Speed > 0.05f;
                    travelStateText.text = moving ? "行驶中" : "静止";
                    travelStateText.color = moving ? UIFactory.AccentGreen : UIFactory.TextDim;
                }
            }

            if (carTransform != null && positionText != null)
            {
                Vector3 p = carTransform.position;
                positionText.text = $"X {p.x:0.0}    Y {p.y:0.0}    Z {p.z:0.0}    朝向 {carTransform.eulerAngles.y:0.0}°";
            }

            if (cameraAngleText != null && cameraRotor != null)
            {
                cameraAngleText.text = cameraRotor.IsAutoRotating
                    ? $"云台 {cameraRotor.CurrentAngle:0}°（自动环扫）"
                    : $"云台 {cameraRotor.CurrentAngle:0}°";
            }

            if (stageModeText != null && modeController != null)
            {
                stageModeText.text = modeController.IsAutoCruise ? "自动巡航" : "手动模式";
            }
        }

        private void UpdateStatus()
        {
            bool connected = (robotDataReader != null && robotDataReader.IsConnected) ||
                             (client != null && client.IsConnected);

            if (statusText != null)
            {
                statusText.text = connected ? "已连接" : "未连接";
                statusText.color = connected ? UIFactory.AccentGreen : UIFactory.AccentOrange;
            }
            if (statusChipImg != null)
            {
                statusChipImg.color = connected
                    ? new Color(0.149f, 0.204f, 0.169f, 1f)
                    : new Color(0.188f, 0.165f, 0.137f, 1f);
            }
            if (footerText != null)
            {
                footerText.text = connected ? "● 设备已连接 · 数据接收中" : "● 本地模式 · 未接入设备";
                footerText.color = connected ? UIFactory.AccentGreen : UIFactory.TextDim;
            }
        }

        /// <summary>刷新小车 WebSocket 下发的管道数据、检测结果与连接状态。</summary>
        private void UpdateRobotData()
        {
            if (robotDataReader == null)
            {
                return;
            }

            // 连接状态
            if (pipeStatusText != null)
            {
                bool connected = robotDataReader.IsConnected;
                pipeStatusText.text = connected ? "连接：已连接" : "连接：未连接";
                pipeStatusText.color = connected ? UIFactory.AccentGreen : UIFactory.AccentOrange;
            }

            // 记录检测数据到曲线图（随时间变化）
            PipeData p = robotDataReader.LatestPipe;
            if (p != null)
            {
                deviationChart?.AddValue((float)p.deviation);
                ellipticityChart?.AddValue((float)p.ellipticity);
                confidenceChart?.AddValue((float)p.confidence);
                radiusChart?.AddValue((float)p.radius);

                if (deviationValueText != null) deviationValueText.text = p.deviation.ToString("0.00");
                if (ellipticityValueText != null) ellipticityValueText.text = p.ellipticity.ToString("0.00");
                if (confidenceValueText != null) confidenceValueText.text = p.confidence.ToString("0.00");
                if (radiusValueText != null) radiusValueText.text = p.radius.ToString("0.00");

                // 主界面数据卡片同步
                if (mainDeviationText != null) mainDeviationText.text = p.deviation.ToString("0.00");
                if (mainEllipticityText != null) mainEllipticityText.text = p.ellipticity.ToString("0.00");
                if (mainConfidenceText != null) mainConfidenceText.text = p.confidence.ToString("0.00");
                if (mainRadiusText != null) mainRadiusText.text = p.radius.ToString("0.00");
            }

            // 检测结果
            if (recognitionText != null)
            {
                List<Detection> dets = robotDataReader.LatestDetections;
                if (dets != null && dets.Count > 0)
                {
                    string result = $"检测结果：{dets.Count} 个目标";
                    int n = Mathf.Min(dets.Count, 5);
                    for (int i = 0; i < n; i++)
                    {
                        result += $"\n  {dets[i].class_name}  {dets[i].score:0.00}";
                    }
                    recognitionText.text = result;
                }
                else if (robotDataReader.IsConnected)
                {
                }
            }
        }

        /// <summary>点击「连接」按钮：读取 IP/端口并重新连接小车。</summary>
        private void OnConnectClicked()
        {
            if (robotDataReader == null)
            {
                return;
            }

            string ip = ipInput != null ? ipInput.text.Trim() : "";
            if (string.IsNullOrEmpty(ip))
            {
                ip = "10.130.38.131";
                if (ipInput != null) ipInput.text = ip;
            }

            int port = 8888;
            if (portInput != null && int.TryParse(portInput.text, out int p))
            {
                port = p;
            }
            else if (portInput != null)
            {
                portInput.text = "8888";
            }

            robotDataReader.ServerIp = ip;
            robotDataReader.Port = port;
            robotDataReader.Reconnect();
        }

        /// <summary>切换动态管道模型的显示/隐藏。</summary>
        private void TogglePipeModel()
        {
            pipeModelVisible = !pipeModelVisible;
            if (dynamicPipeModel != null)
            {
                dynamicPipeModel.gameObject.SetActive(pipeModelVisible);
            }
            UpdatePipeModelBtn();
        }

        private void UpdatePipeModelBtn()
        {
            if (pipeModelBtn == null)
            {
                return;
            }

            Image img = pipeModelBtn.GetComponent<Image>();
            Text t = pipeModelBtn.GetComponentInChildren<Text>();
            if (pipeModelVisible)
            {
                img.color = UIFactory.AccentCyan;
                if (t != null) t.color = Color.black;
            }
            else
            {
                img.color = new Color(0.05f, 0.12f, 0.16f, 0.9f);
                if (t != null) t.color = UIFactory.TextPrimary;
            }
        }

        // ===================== 窗口标题栏（Windows 风格）=====================
        /// <summary>
        /// 给弹窗加一条 Windows 风格的标题栏：左侧标题、右上角 ✕ 关闭按钮、顶部高光与底部分隔线。
        /// 同时把该窗口注册为「可拖标题栏移动 + 可拖边框缩放」。返回标题文本，便于动态修改。
        /// </summary>
        private Text AddWindowTitleBar(RectTransform panel, string title,
            UnityEngine.Events.UnityAction onClose, float minWidth = 320f, float minHeight = 200f, float barHeight = 34f)
        {
            GameObject barGO = new GameObject("TitleBar", typeof(RectTransform), typeof(Image));
            barGO.transform.SetParent(panel, false);

            Image barImg = barGO.GetComponent<Image>();
            barImg.color = new Color(0.1098f, 0.1216f, 0.1451f, 1f); // #1c1f25
            barImg.raycastTarget = false;
            UIFactory.ApplyRounded(barImg);

            RectTransform barRT = barGO.GetComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0f, 1f);
            barRT.anchorMax = new Vector2(1f, 1f);
            barRT.pivot = new Vector2(0.5f, 1f);
            barRT.anchoredPosition = Vector2.zero;
            barRT.sizeDelta = new Vector2(0f, barHeight);

            RegisterWindow(panel, barRT, minWidth, minHeight);

            // 标题栏顶部高光
            AddChromeLine(barGO.transform, "TopHL", new Vector2(0f, 1f), new Vector2(1f, 1f), 1.5f,
                new Color(0.4314f, 0.6627f, 0.9529f, 0.32f), 8f);
            // 标题栏与内容区的分隔线
            AddChromeLine(barGO.transform, "BottomLine", new Vector2(0f, 0f), new Vector2(1f, 0f), 1.5f,
                UIFactory.BorderCyan);

            Text t = UIFactory.CreateText(barGO.transform, "Title", title, 16, UIFactory.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRT = t.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = new Vector2(12f, 0f);
            tRT.offsetMax = new Vector2(-42f, 0f);

            // 右上角 ✕
            Button close = UIFactory.CreateButton(barGO.transform, "CloseBtn", "×", 15);
            RectTransform cRT = close.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(1f, 0.5f);
            cRT.anchorMax = new Vector2(1f, 0.5f);
            cRT.pivot = new Vector2(1f, 0.5f);
            cRT.anchoredPosition = new Vector2(-6f, 0f);
            cRT.sizeDelta = new Vector2(26f, 22f);

            Image cImg = close.GetComponent<Image>();
            cImg.color = Color.white;

            ColorBlock cb = close.colors;
            cb.normalColor = new Color(0.1647f, 0.1882f, 0.2157f, 1f);
            cb.highlightedColor = new Color(0.55f, 0.22f, 0.22f, 1f);
            cb.pressedColor = new Color(0.70f, 0.25f, 0.25f, 1f);
            cb.selectedColor = new Color(0.1647f, 0.1882f, 0.2157f, 1f);
            close.colors = cb;

            Text cl = close.GetComponentInChildren<Text>();
            if (cl != null)
            {
                cl.color = UIFactory.TextPrimary;
                cl.fontStyle = FontStyle.Bold;
            }

            if (onClose != null)
            {
                close.onClick.AddListener(onClose);
            }

            return t;
        }

        /// <summary>标题栏上的横向细线（高光 / 分隔线）。</summary>
        private static void AddChromeLine(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            float thickness, Color color, float insetX = 0f)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            Image img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, (anchorMin.y + anchorMax.y) * 0.5f < 0.5f ? thickness * 0.5f : -thickness * 0.5f);
            rt.sizeDelta = new Vector2(-2f * insetX, thickness);
        }

        // ===================== 检测数据曲线 =====================
        private void BuildChartPanel()
        {
            GameObject overlayGO = new GameObject("ChartOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(canvas.transform, false);
            overlayGO.transform.SetAsLastSibling();

            Image overlayImg = overlayGO.GetComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.55f);

            RectTransform overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            RectTransform panel = UIFactory.CreatePanel(overlayGO.transform, "ChartPanel", UIFactory.BgPanel, UIFactory.BorderCyan);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(900, 560);

            AddWindowTitleBar(panel, "检测数据曲线", HideChartPanel, 520f, 360f);

            // 2x2 四张曲线图
            deviationChart = CreateChartBlock(panel, "偏差", 0, 1, UIFactory.AccentCyan, out deviationValueText);
            ellipticityChart = CreateChartBlock(panel, "椭圆度", 1, 1, UIFactory.AccentGreen, out ellipticityValueText);
            confidenceChart = CreateChartBlock(panel, "置信度", 0, 0, UIFactory.AccentOrange, out confidenceValueText);
            radiusChart = CreateChartBlock(panel, "半径", 1, 0, new Color(0.7f, 0.5f, 1f), out radiusValueText);

            chartPanel = overlayGO;
            chartPanel.SetActive(false);
        }

        private DataChart CreateChartBlock(RectTransform panel, string label, int col, int row, Color color, out Text valueText)
        {
            GameObject blockGO = new GameObject("Block_" + label, typeof(RectTransform));
            blockGO.transform.SetParent(panel, false);
            RectTransform blockRT = blockGO.GetComponent<RectTransform>();
            blockRT.anchorMin = new Vector2(col * 0.5f, row * 0.5f);
            blockRT.anchorMax = new Vector2((col + 1) * 0.5f, (row + 1) * 0.5f);
            blockRT.offsetMin = new Vector2(24, 22);
            blockRT.offsetMax = new Vector2(-24, -46);

            // 标签（顶部）
            Text lab = UIFactory.CreateText(blockGO.transform, "Label", label, 14, UIFactory.TextPrimary, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform lRT = lab.GetComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0, 1);
            lRT.anchorMax = new Vector2(1, 1);
            lRT.pivot = new Vector2(0.5f, 1);
            lRT.anchoredPosition = Vector2.zero;
            lRT.sizeDelta = new Vector2(0, 22);

            // 当前值（底部）
            valueText = UIFactory.CreateText(blockGO.transform, "Value", "--", 13, color, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform vRT = valueText.GetComponent<RectTransform>();
            vRT.anchorMin = new Vector2(0, 0);
            vRT.anchorMax = new Vector2(1, 0);
            vRT.pivot = new Vector2(0.5f, 0);
            vRT.anchoredPosition = Vector2.zero;
            vRT.sizeDelta = new Vector2(0, 22);

            // 图表（中间）
            GameObject chartContainer = new GameObject("ChartContainer", typeof(RectTransform));
            chartContainer.transform.SetParent(blockGO.transform, false);
            RectTransform ccRT = chartContainer.GetComponent<RectTransform>();
            ccRT.anchorMin = Vector2.zero;
            ccRT.anchorMax = Vector2.one;
            ccRT.offsetMin = new Vector2(0, 24);
            ccRT.offsetMax = new Vector2(0, -26);

            DataChart chart = gameObject.AddComponent<DataChart>();
            chart.Build(ccRT, color, -1f);
            return chart;
        }

        private void ShowChartPanel()
        {
            if (chartPanel != null)
            {
                chartPanel.SetActive(true);
            }
        }

        private void HideChartPanel()
        {
            if (chartPanel != null)
            {
                chartPanel.SetActive(false);
            }
        }

        // ===================== 点云重建 =====================
        private void BuildRebuildPanel()
        {
            GameObject overlayGO = new GameObject("RebuildOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(canvas.transform, false);
            overlayGO.transform.SetAsLastSibling();

            Image overlayImg = overlayGO.GetComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.6f);

            RectTransform overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            RectTransform panel = UIFactory.CreatePanel(overlayGO.transform, "RebuildPanel", UIFactory.BgPanel, UIFactory.BorderCyan);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(900, 620);

            AddWindowTitleBar(panel, "点云重建", CloseRebuildPanel, 520f, 400f);

            // 预览画面
            GameObject previewGO = new GameObject("Preview", typeof(RectTransform), typeof(RawImage));
            previewGO.transform.SetParent(panel, false);
            rebuildPreview = previewGO.GetComponent<RawImage>();
            rebuildPreview.color = Color.white;
            rebuildPreview.raycastTarget = false;
            RectTransform pRT = previewGO.GetComponent<RectTransform>();
            pRT.anchorMin = new Vector2(0, 0.24f);
            pRT.anchorMax = new Vector2(1, 1);
            pRT.offsetMin = new Vector2(16, 8);
            pRT.offsetMax = new Vector2(-16, -46);

            // 缺陷按钮列表（预览下方，横向可滚动，避免缺陷多时溢出屏幕）
            GameObject scrollGO = new GameObject("DefectScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGO.transform.SetParent(panel, false);
            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0.14f);
            scrollRT.anchorMax = new Vector2(1, 0.24f);
            scrollRT.offsetMin = new Vector2(16, 0);
            scrollRT.offsetMax = new Vector2(-16, 0);

            ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
            sr.horizontal = true;
            sr.vertical = false;
            sr.scrollSensitivity = 20f;

            // 视口（裁剪超出部分）
            GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            RectTransform vpRT = viewportGO.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;

            // 内容（自动横向排列）
            GameObject contentGO = new GameObject("Content", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            contentGO.transform.SetParent(viewportGO.transform, false);
            defectButtonContainer = contentGO.GetComponent<RectTransform>();
            defectButtonContainer.anchorMin = new Vector2(0, 0.5f);
            defectButtonContainer.anchorMax = new Vector2(0, 0.5f);
            defectButtonContainer.pivot = new Vector2(0, 0.5f);
            defectButtonContainer.anchoredPosition = Vector2.zero;
            defectButtonContainer.sizeDelta = new Vector2(0f, 34f);

            HorizontalLayoutGroup hlg = contentGO.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            ContentSizeFitter csf = contentGO.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            csf.verticalFit = ContentSizeFitter.FitMode.Unconstrained;

            sr.viewport = vpRT;
            sr.content = defectButtonContainer;

            // 状态文字
            rebuildStatusText = UIFactory.CreateText(panel, "Status", "等待雷达数据…将自动重建", 13, UIFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform sRT = rebuildStatusText.GetComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0, 0);
            sRT.anchorMax = new Vector2(0.28f, 0.14f);
            sRT.offsetMin = new Vector2(20, 8);
            sRT.offsetMax = new Vector2(-8, -4);
            rebuildStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            rebuildStatusText.verticalOverflow = VerticalWrapMode.Truncate;

            // 保存到本地
            Button saveBtn = UIFactory.CreateButton(panel, "SaveBtn", "保存建模", 14);
            RectTransform svRT = saveBtn.GetComponent<RectTransform>();
            svRT.anchorMin = new Vector2(0.30f, 0);
            svRT.anchorMax = new Vector2(0.52f, 0.14f);
            svRT.offsetMin = new Vector2(4, 8);
            svRT.offsetMax = new Vector2(-4, -4);
            saveBtn.onClick.AddListener(OnSaveArchiveClicked);

            // 读取本地存档
            Button loadBtn = UIFactory.CreateButton(panel, "LoadBtn", "读取建模", 14);
            RectTransform ldRT = loadBtn.GetComponent<RectTransform>();
            ldRT.anchorMin = new Vector2(0.54f, 0);
            ldRT.anchorMax = new Vector2(0.76f, 0.14f);
            ldRT.offsetMin = new Vector2(4, 8);
            ldRT.offsetMax = new Vector2(-4, -4);
            loadBtn.onClick.AddListener(OpenArchivePanel);

            // 回到实时建模（查看本地存档后，清空并恢复实时采集）
            backToLiveBtn = UIFactory.CreateButton(panel, "BackToLiveBtn", "回到实时建模", 14);
            RectTransform btRT = backToLiveBtn.GetComponent<RectTransform>();
            btRT.anchorMin = new Vector2(0.78f, 0);
            btRT.anchorMax = new Vector2(1f, 0.14f);
            btRT.offsetMin = new Vector2(4, 8);
            btRT.offsetMax = new Vector2(-20, -4);
            backToLiveBtn.onClick.AddListener(OnBackToLiveClicked);
            backToLiveBtn.interactable = false;

            // 顶部临时提示（保存/读取结果，自动消失）
            noticeText = UIFactory.CreateText(panel, "Notice", "", 14, UIFactory.AccentGreen, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform nRT = noticeText.GetComponent<RectTransform>();
            nRT.anchorMin = new Vector2(0, 1);
            nRT.anchorMax = new Vector2(1, 1);
            nRT.pivot = new Vector2(0.5f, 1);
            nRT.anchoredPosition = new Vector2(0, -44);
            nRT.sizeDelta = new Vector2(-40, 26);
            noticeText.gameObject.SetActive(false);

            rebuildPanel = overlayGO;
            rebuildPanel.SetActive(false);

            CreatePreviewCamera();
        }

        private void CreatePreviewCamera()
        {
            GameObject camGO = new GameObject("RebuildPreviewCamera");
            camGO.transform.SetParent(transform, false);
            previewCamera = camGO.AddComponent<Camera>();
            previewCamera.clearFlags = CameraClearFlags.SolidColor;
            previewCamera.backgroundColor = new Color(0.02f, 0.05f, 0.08f);
            previewCamera.cullingMask = 1 << rebuildLayer;
            previewCamera.fieldOfView = 60f;
            previewCamera.nearClipPlane = 0.01f;
            previewCamera.farClipPlane = 1000f;
            previewCamera.enabled = false;

            previewRT = new RenderTexture(800, 500, 16);
            previewRT.Create();
            previewCamera.targetTexture = previewRT;
            if (rebuildPreview != null)
            {
                rebuildPreview.texture = previewRT;
            }

            // 主场景相机不渲染重建层，避免场景中出现悬浮网格
            foreach (Camera cam in Camera.allCameras)
            {
                if (cam != previewCamera)
                {
                    cam.cullingMask &= ~(1 << rebuildLayer);
                }
            }
        }

        /// <summary>在重建预览层创建一个黄色小球，表示「相机此刻看向管道的哪个 (截面, 角度)」，用于校准角度偏移。</summary>
        private void CreateAimMarker()
        {
            aimMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            aimMarker.name = "CameraAimMarker";
            aimMarker.layer = rebuildLayer;
            Collider col = aimMarker.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
            Renderer r = aimMarker.GetComponent<Renderer>();
            if (r != null)
            {
                Shader shader = Shader.Find("Unlit/Color");
                r.material = new Material(shader != null ? shader : Shader.Find("Standard"));
                r.material.color = new Color(1f, 0.9f, 0f, 1f);
            }
            aimMarker.SetActive(false);
        }

        /// <summary>把调试小球放到「相机当前看向」的管道壁上，用于校准 gimbalAngleOffset。</summary>
        private void UpdateAimMarker()
        {
            if (aimMarker == null || pointCloudRebuilder == null || cameraRotor == null)
            {
                return;
            }
            bool show = rebuildPanel != null && rebuildPanel.activeSelf && pointCloudRebuilder.SectionCount > 0;
            aimMarker.SetActive(show);
            if (!show)
            {
                return;
            }

            int section = pointCloudRebuilder.CurrentSectionIndex;
            float angle = NormalizeAngle(cameraRotor.CurrentAngle + gimbalAngleOffset);
            if (pointCloudRebuilder.TryGetWorldPoint(section, angle, out Vector3 p))
            {
                aimMarker.transform.position = p;
                float r = pointCloudRebuilder.AverageRadius;
                aimMarker.transform.localScale = Vector3.one * Mathf.Max(0.05f, r * 0.06f);
            }
        }

        private void FocusPreviewCamera()
        {
            if (previewCamera == null || pointCloudRebuilder == null)
            {
                return;
            }

            MeshFilter mf = pointCloudRebuilder.ResultMeshFilter;
            if (mf == null || mf.sharedMesh == null)
            {
                return;
            }

            // 顶点已是世界坐标，resultObject 固定在世界原点
            Bounds b = mf.sharedMesh.bounds;
            previewTarget = b.center;
            float radius = b.extents.magnitude;
            if (radius < 0.01f)
            {
                radius = 1f;
            }

            previewDistance = radius * 2.5f;
            previewRadius = radius;
            previewYaw = 45f;
            previewPitch = 25f;
            previewInited = true;

            // 根据网格尺寸动态调整裁剪面
            previewCamera.nearClipPlane = Mathf.Max(0.01f, radius * 0.01f);
            previewCamera.farClipPlane = Mathf.Max(100f, radius * 50f);

            ApplyPreviewCamera();
        }

        /// <summary>根据 orbit 参数更新预览相机位置与朝向。</summary>
        private void ApplyPreviewCamera()
        {
            if (previewCamera == null)
            {
                return;
            }

            float yaw = previewYaw * Mathf.Deg2Rad;
            float pitch = previewPitch * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(
                Mathf.Sin(yaw) * Mathf.Cos(pitch),
                Mathf.Sin(pitch),
                Mathf.Cos(yaw) * Mathf.Cos(pitch));

            previewCamera.transform.position = previewTarget + dir * previewDistance;
            previewCamera.transform.LookAt(previewTarget, Vector3.up);
        }

        /// <summary>
        /// 弹窗打开时更新预览相机：
        /// 左键拖拽旋转视角、右键拖拽平移位置、滚轮缩放、WASD 前后左右移动、Q/E 水平旋转。
        /// </summary>
        private void UpdatePreviewCamera()
        {
            if (previewCamera == null || !previewInited)
            {
                return;
            }
            if (rebuildPanel == null || !rebuildPanel.activeSelf)
            {
                return;
            }
            // 有其它窗口盖在上面时，不响应预览操作
            if ((archivePanel != null && archivePanel.activeSelf) ||
                (imagePanel != null && imagePanel.activeSelf))
            {
                return;
            }

            bool changed = false;
            Transform cam = previewCamera.transform;

            // ---- 鼠标操作（需悬停在预览画面上）----
            if (IsMouseOverPreview())
            {
                // 左键拖拽：旋转摄像机角度
                if (Input.GetMouseButton(0))
                {
                    previewYaw += Input.GetAxis("Mouse X") * 1.5f;
                    previewPitch -= Input.GetAxis("Mouse Y") * 1.5f;
                    previewPitch = Mathf.Clamp(previewPitch, -89f, 89f);
                    changed = true;
                }

                // 右键拖拽：平移摄像机位置（沿摄像机自身的右向/上向）
                if (Input.GetMouseButton(1))
                {
                    float panScale = previewDistance * 0.0018f;
                    previewTarget -= (cam.right * Input.GetAxis("Mouse X") +
                                      cam.up * Input.GetAxis("Mouse Y")) * panScale;
                    changed = true;
                }

                // 滚轮缩放
                float scroll = Input.mouseScrollDelta.y;
                if (Mathf.Abs(scroll) > 0.001f)
                {
                    previewDistance = Mathf.Clamp(
                        previewDistance * Mathf.Pow(0.85f, scroll),
                        previewRadius * 0.1f,
                        previewRadius * 50f);
                    changed = true;
                }
            }

            // ---- 键盘操作 ----
            // WASD：沿摄像机水平朝向前后 / 左右移动
            Vector3 forward = cam.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();

            Vector3 right = cam.right;
            right.y = 0f;
            if (right.sqrMagnitude < 1e-6f)
            {
                right = Vector3.right;
            }
            right.Normalize();

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += forward;
            if (Input.GetKey(KeyCode.S)) move -= forward;
            if (Input.GetKey(KeyCode.D)) move += right;
            if (Input.GetKey(KeyCode.A)) move -= right;
            if (move.sqrMagnitude > 0f)
            {
                previewTarget += move.normalized * (previewDistance * 0.6f) * Time.unscaledDeltaTime;
                changed = true;
            }

            // Q / E：绕目标水平旋转摄像机
            if (Input.GetKey(KeyCode.Q))
            {
                previewYaw -= 70f * Time.unscaledDeltaTime;
                changed = true;
            }
            if (Input.GetKey(KeyCode.E))
            {
                previewYaw += 70f * Time.unscaledDeltaTime;
                changed = true;
            }

            if (changed)
            {
                ApplyPreviewCamera();
            }
        }

        private bool IsMouseOverPreview()
        {
            if (rebuildPreview == null)
            {
                return false;
            }
            return RectTransformUtility.RectangleContainsScreenPoint(
                rebuildPreview.rectTransform, Input.mousePosition, null);
        }

        private void OpenRebuildPanel()
        {
            if (rebuildPanel != null)
            {
                rebuildPanel.SetActive(true);
            }
            if (previewCamera != null)
            {
                previewCamera.enabled = true;
            }
            RebuildPanelOpen = true;
            // 打开时直接展示当前已重建的模型
            if (pointCloudRebuilder != null && pointCloudRebuilder.HasResult)
            {
                FocusPreviewCamera();
            }
        }

        private void CloseRebuildPanel()
        {
            if (rebuildPanel != null)
            {
                rebuildPanel.SetActive(false);
            }
            if (previewCamera != null)
            {
                previewCamera.enabled = false;
            }
            RebuildPanelOpen = false;
        }

        // ===================== 本地存档（保存 / 读取建模） =====================
        /// <summary>顶部临时提示，3 秒后自动消失（避免与每帧刷新的状态文字冲突）。</summary>
        private void ShowNotice(string msg, bool error = false)
        {
            if (noticeText == null)
            {
                return;
            }
            noticeText.text = msg;
            noticeText.color = error ? new Color(1f, 0.45f, 0.4f) : UIFactory.AccentGreen;
            noticeText.gameObject.SetActive(true);
            noticeTimer = 3f;
        }

        private void UpdateNotice()
        {
            if (noticeText == null || !noticeText.gameObject.activeSelf)
            {
                return;
            }
            noticeTimer -= Time.deltaTime;
            if (noticeTimer <= 0f)
            {
                noticeText.gameObject.SetActive(false);
            }
        }

        /// <summary>点击「保存建模」：把当前点云截面与缺陷（含图片）保存到本地。</summary>
        private void OnSaveArchiveClicked()
        {
            if (pointCloudRebuilder == null || pointCloudRebuilder.SectionCount == 0)
            {
                ShowNotice("没有可保存的点云数据。", true);
                return;
            }

            Vector3[][] sections = pointCloudRebuilder.GetSections();

            List<DefectSaveData> saveDefects = new List<DefectSaveData>();
            for (int i = 0; i < defects.Count; i++)
            {
                DefectRecord rec = defects[i];
                DefectSaveData d = new DefectSaveData
                {
                    angleDeg = rec.angleDeg,
                    rangeDeg = rec.rangeDeg,
                    markCount = rec.markCount,
                    startSection = rec.startSection
                };
                d.frames.AddRange(rec.frames);
                saveDefects.Add(d);
            }

            string dir = PointCloudArchive.Save(sections, pointCloudRebuilder.AverageRadius, saveDefects);
            if (string.IsNullOrEmpty(dir))
            {
                ShowNotice("保存失败，请查看控制台。", true);
                return;
            }

            ShowNotice($"已保存到本地：{Path.GetFileName(dir)}（截面 {sections.Length} · 缺陷 {saveDefects.Count}）");
        }

        /// <summary>点击「回到实时建模」：清空当前（存档）模型与缺陷，恢复实时雷达采集。</summary>
        private void OnBackToLiveClicked()
        {
            if (pointCloudRebuilder == null)
            {
                return;
            }

            pointCloudRebuilder.ResumeLive();
            ClearDefects();
            if (backToLiveBtn != null)
            {
                backToLiveBtn.interactable = false;
            }
            ShowNotice("已回到实时建模，正在重新采集截面…");
        }

        private void BuildArchivePanel()
        {
            GameObject overlayGO = new GameObject("ArchiveOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(canvas.transform, false);
            overlayGO.transform.SetAsLastSibling();

            Image overlayImg = overlayGO.GetComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.65f);

            RectTransform overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            RectTransform panel = UIFactory.CreatePanel(overlayGO.transform, "ArchivePanel", UIFactory.BgPanel, UIFactory.BorderCyan);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(700, 520);

            AddWindowTitleBar(panel, "本地存档 · 读取点云建模", CloseArchivePanel, 420f, 320f);

            // 纵向滚动列表
            GameObject scrollGO = new GameObject("ArchiveScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGO.transform.SetParent(panel, false);
            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            scrollRT.offsetMin = new Vector2(18, 20);
            scrollRT.offsetMax = new Vector2(-18, -46);

            ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 24f;

            GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            RectTransform vpRT = viewportGO.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;

            GameObject contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGO.transform.SetParent(viewportGO.transform, false);
            archiveListContainer = contentGO.GetComponent<RectTransform>();
            archiveListContainer.anchorMin = new Vector2(0, 1);
            archiveListContainer.anchorMax = new Vector2(1, 1);
            archiveListContainer.pivot = new Vector2(0.5f, 1);
            archiveListContainer.anchoredPosition = Vector2.zero;
            archiveListContainer.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup vlg = contentGO.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            ContentSizeFitter csf = contentGO.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vpRT;
            sr.content = archiveListContainer;

            archivePanel = overlayGO;
            archivePanel.SetActive(false);
        }

        private void OpenArchivePanel()
        {
            if (archivePanel == null)
            {
                return;
            }
            RefreshArchiveList();
            archivePanel.SetActive(true);
        }

        private void CloseArchivePanel()
        {
            if (archivePanel != null)
            {
                archivePanel.SetActive(false);
            }
        }

        /// <summary>重建存档列表（每次打开时刷新，保证读取到最新保存的记录）。</summary>
        private void RefreshArchiveList()
        {
            if (archiveListContainer == null)
            {
                return;
            }

            for (int i = archiveListContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = archiveListContainer.GetChild(i).gameObject;
                child.SetActive(false); // 立即移出布局，避免刷新时与旧行短暂重叠
                Destroy(child);
            }

            List<string> list = PointCloudArchive.ListArchives();
            if (list.Count == 0)
            {
                Text empty = UIFactory.CreateText(archiveListContainer, "Empty",
                    "暂无本地存档。\n请先在「点云重建」面板中点击「保存建模」。", 14, UIFactory.TextDim, TextAnchor.MiddleCenter);
                LayoutElement le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 80f;
                return;
            }

            foreach (string dir in list)
            {
                AddArchiveRow(dir);
            }
        }

        private void AddArchiveRow(string dir)
        {
            ArchiveMeta meta = PointCloudArchive.ReadMeta(dir);
            if (meta == null)
            {
                return;
            }

            // 行容器：横向排列 [读取按钮][删除按钮]
            GameObject rowGO = new GameObject("ArchiveRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGO.transform.SetParent(archiveListContainer, false);

            LayoutElement rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 48f;
            rowLE.minHeight = 48f;

            HorizontalLayoutGroup hlg = rowGO.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            string label = $"{meta.createdAt}    截面 {meta.sectionCount}    缺陷 {meta.defects.Count}";
            Button btn = UIFactory.CreateButton(rowGO.transform, "Archive_" + meta.name, label, 14);
            LayoutElement loadLE = btn.gameObject.AddComponent<LayoutElement>();
            loadLE.flexibleWidth = 1f;
            loadLE.minHeight = 48f;

            Text t = btn.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleLeft;
                RectTransform tRT = t.rectTransform;
                tRT.offsetMin = new Vector2(14, 0);
                tRT.offsetMax = new Vector2(-14, 0);
            }

            string captured = dir;
            btn.onClick.AddListener(() => LoadArchive(captured));

            // 删除按钮（红色调，避免误点）
            Button del = UIFactory.CreateButton(rowGO.transform, "Delete_" + meta.name, "删除", 14);
            LayoutElement delLE = del.gameObject.AddComponent<LayoutElement>();
            delLE.preferredWidth = 72f;
            delLE.minWidth = 72f;
            delLE.minHeight = 48f;

            Image delImg = del.GetComponent<Image>();
            delImg.color = Color.white; // 颜色交给 ColorBlock
            ColorBlock dcb = del.colors;
            dcb.normalColor = new Color(0.35f, 0.1f, 0.12f, 0.9f);
            dcb.highlightedColor = new Color(0.5f, 0.15f, 0.18f, 0.95f);
            dcb.pressedColor = new Color(0.7f, 0.2f, 0.22f, 1f);
            dcb.selectedColor = new Color(0.6f, 0.18f, 0.2f, 1f);
            del.colors = dcb;

            del.onClick.AddListener(() => DeleteArchive(captured));
        }

        /// <summary>删除一个本地存档并刷新列表。</summary>
        private void DeleteArchive(string dir)
        {
            ArchiveMeta meta = PointCloudArchive.ReadMeta(dir);
            string name = meta != null ? meta.name : Path.GetFileName(dir);

            if (!PointCloudArchive.Delete(dir))
            {
                ShowNotice("删除失败，请查看控制台。", true);
                return;
            }

            ShowNotice($"已删除本地建模：{name}");
            RefreshArchiveList();
        }

        /// <summary>读取指定存档并应用到场景与重建面板。</summary>
        private void LoadArchive(string dir)
        {
            ArchiveData data = PointCloudArchive.Load(dir);
            if (data == null)
            {
                ShowNotice("读取失败，请查看控制台。", true);
                return;
            }

            ApplyArchive(data);
            CloseArchivePanel();
            OpenRebuildPanel();
            ShowNotice($"已读取：{data.meta.name}（截面 {data.meta.sectionCount} · 缺陷 {data.meta.defects.Count}）");
        }

        /// <summary>把存档数据应用到点云重建器与缺陷列表。</summary>
        private void ApplyArchive(ArchiveData data)
        {
            if (pointCloudRebuilder == null)
            {
                return;
            }

            ClearDefects();
            pointCloudRebuilder.LoadSections(data.sections, data.meta.avgRadius);

            foreach (DefectSaveData d in data.defects)
            {
                DefectRecord rec = new DefectRecord
                {
                    angleDeg = d.angleDeg,
                    rangeDeg = d.rangeDeg,
                    markCount = d.markCount,
                    startSection = d.startSection
                };
                rec.frames.AddRange(d.frames);
                defects.Add(rec);

                // 按存档的定位参数重新标红
                int start = Mathf.Max(0, d.startSection);
                int count = Mathf.Max(1, d.markCount);
                for (int s = start; s < start + count; s++)
                {
                    pointCloudRebuilder.MarkDefectAt(s, d.angleDeg, d.rangeDeg);
                }

                AddDefectButton(rec);
            }
        }

        /// <summary>清空当前缺陷记录、按钮与图片纹理。</summary>
        private void ClearDefects()
        {
            if (defectButtonContainer != null)
            {
                for (int i = defectButtonContainer.childCount - 1; i >= 0; i--)
                {
                    Destroy(defectButtonContainer.GetChild(i).gameObject);
                }
            }

            for (int i = 0; i < defects.Count; i++)
            {
                List<Texture2D> frames = defects[i].frames;
                for (int f = 0; f < frames.Count; f++)
                {
                    if (frames[f] != null)
                    {
                        Destroy(frames[f]);
                    }
                }
            }
            defects.Clear();

            if (imagePreview != null)
            {
                imagePreview.texture = null;
            }
            if (imagePanel != null)
            {
                imagePanel.SetActive(false);
            }
        }

        // ===================== 缺陷图片查看 =====================
        private void BuildImagePanel()
        {
            GameObject overlayGO = new GameObject("ImageOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(canvas.transform, false);
            overlayGO.transform.SetAsLastSibling();

            Image overlayImg = overlayGO.GetComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.6f);

            RectTransform overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            RectTransform panel = UIFactory.CreatePanel(overlayGO.transform, "ImagePanel", UIFactory.BgPanel, UIFactory.BorderCyan);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(640, 560);

            imageTitle = AddWindowTitleBar(panel, "缺陷图片", CloseImagePanel, 360f, 320f);

            GameObject imgGO = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
            imgGO.transform.SetParent(panel, false);
            imagePreview = imgGO.GetComponent<RawImage>();
            imagePreview.color = Color.white;
            imagePreview.raycastTarget = false;
            RectTransform iRT = imgGO.GetComponent<RectTransform>();
            iRT.anchorMin = new Vector2(0, 0);
            iRT.anchorMax = new Vector2(1, 1);
            iRT.offsetMin = new Vector2(12, 20);
            iRT.offsetMax = new Vector2(-12, -46);

            imagePanel = overlayGO;
            imagePanel.SetActive(false);
        }

        private void ShowDefectImage(DefectRecord rec)
        {
            if (rec == null || rec.frames.Count == 0)
            {
                return;
            }
            Texture2D mid = rec.frames[rec.frames.Count / 2];
            if (imagePreview != null)
            {
                imagePreview.texture = mid;
            }
            if (imageTitle != null)
            {
                imageTitle.text = $"缺陷图片（角度 {rec.angleDeg:0.0}°）";
            }
            if (imagePanel != null)
            {
                imagePanel.SetActive(true);
            }
        }

        private void CloseImagePanel()
        {
            if (imagePanel != null)
            {
                imagePanel.SetActive(false);
            }
        }

        /// <summary>为缺陷创建一个按钮，点击打开对应缺陷的中间帧图片。</summary>
        private void AddDefectButton(DefectRecord rec)
        {
            if (defectButtonContainer == null)
            {
                return;
            }

            int index = defects.Count; // 1 起
            Button btn = UIFactory.CreateButton(defectButtonContainer, $"DefectBtn{index}",
                $"缺陷{index}  {rec.angleDeg:0}°", 14);

            // 固定按钮尺寸，由 HorizontalLayoutGroup 自动排列
            LayoutElement le = btn.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 150f;
            le.preferredHeight = 34f;

            btn.onClick.AddListener(() => ShowDefectImage(rec));
        }

        // ===================== 脚本编写 =====================
        private void BuildScriptPanel()
        {
            GameObject overlayGO = new GameObject("ScriptOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(canvas.transform, false);
            overlayGO.transform.SetAsLastSibling();

            Image overlayImg = overlayGO.GetComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.6f);

            RectTransform overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            RectTransform panel = UIFactory.CreatePanel(overlayGO.transform, "ScriptPanel", UIFactory.BgPanel, UIFactory.BorderCyan);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(1060, 640);

            AddWindowTitleBar(panel, "脚本编写", CloseScriptPanel, 760f, 460f);

            // ---- 左侧：脚本编辑区（多行、可滚动）----
            GameObject scrollGO = new GameObject("EditorScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGO.transform.SetParent(panel, false);
            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0f, 0f);
            scrollRT.anchorMax = new Vector2(0.66f, 1f);
            scrollRT.offsetMin = new Vector2(18f, 18f);
            scrollRT.offsetMax = new Vector2(-10f, -46f);

            ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 30f;
            sr.movementType = ScrollRect.MovementType.Clamped;
            scriptScroll = sr;

            GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            RectTransform vpRT = viewportGO.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;
            scriptViewportRect = vpRT;

            GameObject editorBgGO = new GameObject("EditorBg", typeof(RectTransform), typeof(Image));
            editorBgGO.transform.SetParent(viewportGO.transform, false);
            Image editorBg = editorBgGO.GetComponent<Image>();
            editorBg.color = new Color(0.0784f, 0.0941f, 0.1137f, 1f);
            editorBg.raycastTarget = false;
            RectTransform editorBgRT = editorBgGO.GetComponent<RectTransform>();
            editorBgRT.anchorMin = Vector2.zero;
            editorBgRT.anchorMax = Vector2.one;
            editorBgRT.offsetMin = Vector2.zero;
            editorBgRT.offsetMax = Vector2.zero;

            scriptInput = UIFactory.CreateInputField(viewportGO.transform, "ScriptInput", "", scriptFontSize);
            scriptInput.lineType = InputField.LineType.MultiLineNewline;
            scriptInput.textComponent.alignment = TextAnchor.UpperLeft;
            scriptInput.textComponent.horizontalOverflow = HorizontalWrapMode.Wrap;
            scriptInput.textComponent.verticalOverflow = VerticalWrapMode.Overflow;
            scriptInput.textComponent.supportRichText = false;
            scriptInput.textComponent.color = new Color(0.85f, 0.9f, 0.98f, 1f);
            if (scriptInput.placeholder is Text ph)
            {
                ph.text = "在此编写脚本（每个 tick 自动执行一次）…\n\n示例：\n// 读取只读数据，控制本地变量\nif (car_speed > 3) {\n    print(\"速度过快：\" + car_speed);\n    throttle = 0;\n}\ncamera_angle = (camera_angle + 1) % 360;";
                ph.alignment = TextAnchor.UpperLeft;
                ph.color = new Color(0.45f, 0.52f, 0.6f, 0.85f);
            }

            // 输入框锚定在视口顶部，宽度铺满；高度每帧按内容自适应（不小于视口高度，保证可点击）
            RectTransform inRT = scriptInput.GetComponent<RectTransform>();
            inRT.anchorMin = new Vector2(0f, 1f);
            inRT.anchorMax = new Vector2(1f, 1f);
            inRT.pivot = new Vector2(0.5f, 1f);
            inRT.anchoredPosition = new Vector2(0f, 0f);
            inRT.sizeDelta = new Vector2(0f, 0f);

            sr.viewport = vpRT;
            sr.content = inRT;
            scriptInput.onValueChanged.AddListener(_ =>
            {
                // 在末尾输入时自动滚到底部，便于继续编写；编辑中间内容时不打扰
                if (scriptScroll != null && scriptInput.caretPosition >= scriptInput.text.Length)
                {
                    Canvas.ForceUpdateCanvases();
                    scriptScroll.verticalNormalizedPosition = 0f;
                }
            });

            // ---- 左侧下半：运行输出（print 的结果，只读、可滚动）----
            // 运行输出已移到左栏「脚本编写」按钮下方常驻显示，见 BuildLeftSidebar。
            // 此处不再重复创建弹窗内输出区。

            // ---- 右侧：状态 + 四个操作按钮 + 变量说明 ----
            GameObject sideGO = new GameObject("SideColumn", typeof(RectTransform));
            sideGO.transform.SetParent(panel, false);
            RectTransform sideRT = sideGO.GetComponent<RectTransform>();
            sideRT.anchorMin = new Vector2(0.67f, 0f);
            sideRT.anchorMax = new Vector2(1f, 1f);
            sideRT.offsetMin = new Vector2(10f, 18f);
            sideRT.offsetMax = new Vector2(-18f, -46f);

            scriptStatusText = UIFactory.CreateText(sideRT, "ScriptStatus", "状态：未注入", 13, UIFactory.TextDim, TextAnchor.UpperLeft);
            RectTransform stRT = scriptStatusText.GetComponent<RectTransform>();
            stRT.anchorMin = new Vector2(0f, 1f);
            stRT.anchorMax = new Vector2(1f, 1f);
            stRT.pivot = new Vector2(0.5f, 1f);
            stRT.anchoredPosition = new Vector2(0f, 0f);
            stRT.sizeDelta = new Vector2(0f, 58f);
            scriptStatusText.horizontalOverflow = HorizontalWrapMode.Wrap;
            scriptStatusText.verticalOverflow = VerticalWrapMode.Truncate;

            Button injectBtn = UIFactory.CreateButton(sideRT, "InjectBtn", "注入脚本", 15);
            PlaceStacked(injectBtn, 0f, 40f, 66f);
            injectBtn.onClick.AddListener(OnInjectScriptClicked);

            Button clearBtn = UIFactory.CreateButton(sideRT, "ClearBtn", "清除脚本", 15);
            PlaceStacked(clearBtn, 0f, 40f, 114f);
            clearBtn.onClick.AddListener(OnClearScriptClicked);

            Button listBtn = UIFactory.CreateButton(sideRT, "MyScriptBtn", "我的脚本", 15);
            PlaceStacked(listBtn, 0f, 40f, 162f);
            listBtn.onClick.AddListener(OpenScriptListPanel);

            Button saveBtn = UIFactory.CreateButton(sideRT, "SaveScriptBtn", "保存脚本", 15);
            PlaceStacked(saveBtn, 0f, 40f, 210f);
            saveBtn.onClick.AddListener(OnSaveScriptClicked);

            Text hint = UIFactory.CreateText(sideRT, "VarHint",
                "可用变量\n" +
                "【可读写】\n" +
                "speed, throttle, drive_mode(\"auto\"/\"manual\")\n" +
                "cruise_gear, camera_speed, camera_manual_speed\n" +
                "camera_auto_rotate, camera_angle\n" +
                "pipe_model, pointcloud_rebuild\n" +
                "【只读】\n" +
                "is_connected, car_speed, distance, speed_limit\n" +
                "pos_x, pos_y, pos_z, heading, gimbal_angle\n" +
                "radius, deviation, ellipticity, confidence\n" +
                "detection_count, section_count, avg_radius\n" +
                "time, delta_time, frame, PI, E",
                12, UIFactory.TextDim, TextAnchor.UpperLeft);
            RectTransform hRT = hint.GetComponent<RectTransform>();
            hRT.anchorMin = new Vector2(0f, 0f);
            hRT.anchorMax = new Vector2(1f, 1f);
            hRT.offsetMin = new Vector2(0f, 0f);
            hRT.offsetMax = new Vector2(0f, -262f);
            hint.horizontalOverflow = HorizontalWrapMode.Wrap;
            hint.verticalOverflow = VerticalWrapMode.Overflow;

            scriptPanel = overlayGO;
            scriptPanel.SetActive(false);
        }

        /// <summary>把控件锚定在父容器顶部，左侧留出（left）宽度，距顶（top）像素，高 h。</summary>
        private static void PlaceStacked(Component comp, float left, float h, float top)
        {
            RectTransform rt = comp.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(left * 0.5f, -top);
            rt.sizeDelta = new Vector2(-left, h);
        }

        private void BuildScriptListPanel()
        {
            GameObject overlayGO = new GameObject("ScriptListOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(canvas.transform, false);
            overlayGO.transform.SetAsLastSibling();

            Image overlayImg = overlayGO.GetComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.7f);

            RectTransform overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            RectTransform panel = UIFactory.CreatePanel(overlayGO.transform, "ScriptListPanel", UIFactory.BgPanel, UIFactory.BorderCyan);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(760, 520);

            AddWindowTitleBar(panel, "我的脚本 · 读取本地脚本", CloseScriptListPanel, 480f, 320f);

            GameObject scrollGO = new GameObject("ScriptScroll", typeof(RectTransform), typeof(ScrollRect));
            scrollGO.transform.SetParent(panel, false);
            RectTransform scrollRT = scrollGO.GetComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0, 0);
            scrollRT.anchorMax = new Vector2(1, 1);
            scrollRT.offsetMin = new Vector2(18, 20);
            scrollRT.offsetMax = new Vector2(-18, -46);

            ScrollRect sr = scrollGO.GetComponent<ScrollRect>();
            sr.horizontal = false;
            sr.vertical = true;
            sr.scrollSensitivity = 24f;

            GameObject viewportGO = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            viewportGO.transform.SetParent(scrollGO.transform, false);
            RectTransform vpRT = viewportGO.GetComponent<RectTransform>();
            vpRT.anchorMin = Vector2.zero;
            vpRT.anchorMax = Vector2.one;
            vpRT.offsetMin = Vector2.zero;
            vpRT.offsetMax = Vector2.zero;

            GameObject contentGO = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            contentGO.transform.SetParent(viewportGO.transform, false);
            scriptListContainer = contentGO.GetComponent<RectTransform>();
            scriptListContainer.anchorMin = new Vector2(0, 1);
            scriptListContainer.anchorMax = new Vector2(1, 1);
            scriptListContainer.pivot = new Vector2(0.5f, 1);
            scriptListContainer.anchoredPosition = Vector2.zero;
            scriptListContainer.sizeDelta = new Vector2(0, 0);

            VerticalLayoutGroup vlg = contentGO.GetComponent<VerticalLayoutGroup>();
            vlg.spacing = 8f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.padding = new RectOffset(6, 6, 6, 6);

            ContentSizeFitter csf = contentGO.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            sr.viewport = vpRT;
            sr.content = scriptListContainer;

            scriptListPanel = overlayGO;
            scriptListPanel.SetActive(false);
        }

        private void BuildScriptNamePanel()
        {
            GameObject overlayGO = new GameObject("ScriptNameOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(canvas.transform, false);
            overlayGO.transform.SetAsLastSibling();

            Image overlayImg = overlayGO.GetComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.7f);

            RectTransform overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            RectTransform panel = UIFactory.CreatePanel(overlayGO.transform, "ScriptNamePanel", UIFactory.BgPanel, UIFactory.BorderCyan);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(460, 210);

            AddWindowTitleBar(panel, "保存脚本", CloseScriptNamePanel, 360f, 170f);

            Text lab = UIFactory.CreateText(panel, "NameLabel", "脚本名称", 14, UIFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform labRT = lab.GetComponent<RectTransform>();
            labRT.anchorMin = new Vector2(0f, 1f);
            labRT.anchorMax = new Vector2(1f, 1f);
            labRT.pivot = new Vector2(0.5f, 1f);
            labRT.anchoredPosition = new Vector2(0f, -46f);
            labRT.sizeDelta = new Vector2(-40f, 20f);

            scriptNameInput = UIFactory.CreateInputField(panel, "ScriptNameInput", "", 14);
            RectTransform inRT = scriptNameInput.GetComponent<RectTransform>();
            inRT.anchorMin = new Vector2(0f, 1f);
            inRT.anchorMax = new Vector2(1f, 1f);
            inRT.pivot = new Vector2(0.5f, 1f);
            inRT.anchoredPosition = new Vector2(0f, -72f);
            inRT.sizeDelta = new Vector2(-40f, 34f);
            scriptNameInput.characterLimit = 40;
            scriptNameInput.onSubmit.AddListener(_ => OnConfirmScriptName());

            Button cancelBtn = UIFactory.CreateButton(panel, "NameCancelBtn", "取消", 15);
            RectTransform caRT = cancelBtn.GetComponent<RectTransform>();
            caRT.anchorMin = new Vector2(0f, 0f);
            caRT.anchorMax = new Vector2(0.5f, 0f);
            caRT.offsetMin = new Vector2(24f, 18f);
            caRT.offsetMax = new Vector2(-10f, 52f);
            cancelBtn.onClick.AddListener(CloseScriptNamePanel);

            Button okBtn = UIFactory.CreateButton(panel, "NameOkBtn", "保存", 15);
            RectTransform okRT = okBtn.GetComponent<RectTransform>();
            okRT.anchorMin = new Vector2(0.5f, 0f);
            okRT.anchorMax = new Vector2(1f, 0f);
            okRT.offsetMin = new Vector2(10f, 18f);
            okRT.offsetMax = new Vector2(-24f, 52f);
            okBtn.onClick.AddListener(OnConfirmScriptName);

            scriptNamePanel = overlayGO;
            scriptNamePanel.SetActive(false);
        }

        private void OpenScriptNamePanel()
        {
            if (scriptNamePanel == null)
            {
                return;
            }
            if (scriptNameInput != null)
            {
                // 预填一个默认名称，用户可直接改
                scriptNameInput.text = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                scriptNameInput.ActivateInputField();
                scriptNameInput.Select();
            }
            scriptNamePanel.SetActive(true);
        }

        private void CloseScriptNamePanel()
        {
            if (scriptNamePanel != null)
            {
                scriptNamePanel.SetActive(false);
            }
        }

        private void OnConfirmScriptName()
        {
            string name = scriptNameInput != null ? scriptNameInput.text.Trim() : "";
            if (string.IsNullOrEmpty(name))
            {
                name = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            }

            string src = scriptInput != null ? scriptInput.text : "";
            string path = ScriptStorage.Save(name, src);
            CloseScriptNamePanel();

            if (string.IsNullOrEmpty(path))
            {
                ShowScriptMsg("保存失败，请查看控制台。", true);
                return;
            }
            ShowScriptMsg("已保存脚本：" + name, false);
        }

        private void OpenScriptPanel()
        {
            if (scriptPanel == null)
            {
                return;
            }
            // 若当前有已注入脚本且编辑框为空，载入已注入内容，方便继续修改
            if (scriptInput != null && string.IsNullOrEmpty(scriptInput.text) && scriptRunner != null && scriptRunner.IsActive)
            {
                scriptInput.text = scriptRunner.Source;
            }
            scriptPanel.SetActive(true);
        }

        private void CloseScriptPanel()
        {
            if (scriptPanel != null)
            {
                scriptPanel.SetActive(false);
            }
        }

        private void OpenScriptListPanel()
        {
            if (scriptListPanel == null)
            {
                return;
            }
            RefreshScriptList();
            scriptListPanel.SetActive(true);
        }

        private void CloseScriptListPanel()
        {
            if (scriptListPanel != null)
            {
                scriptListPanel.SetActive(false);
            }
        }

        private void OnInjectScriptClicked()
        {
            if (scriptRunner == null)
            {
                ShowScriptMsg("脚本运行器未就绪。", true);
                return;
            }
            string src = scriptInput != null ? scriptInput.text : "";
            if (string.IsNullOrEmpty(src))
            {
                ShowScriptMsg("脚本为空，请先编写内容。", true);
                return;
            }

            if (scriptRunner.Inject(src))
            {
                ShowScriptMsg("注入成功，脚本将每个 tick 自动执行。", false);
            }
            else
            {
                ShowScriptMsg("注入失败：" + scriptRunner.LastError, true);
            }
        }

        private void OnClearScriptClicked()
        {
            if (scriptRunner == null)
            {
                return;
            }
            scriptRunner.Clear();
            ShowScriptMsg("已清除脚本，变量与输出恢复初始状态。", false);
        }

        private void OnSaveScriptClicked()
        {
            string src = scriptInput != null ? scriptInput.text : "";
            if (string.IsNullOrEmpty(src))
            {
                ShowScriptMsg("脚本为空，无需保存。", true);
                return;
            }
            OpenScriptNamePanel();
        }

        private void ShowScriptMsg(string msg, bool error)
        {
            scriptMsg = msg;
            scriptMsgError = error;
            scriptMsgTimer = 4f;
        }

        private void UpdateScriptStatus()
        {
            if (scriptStatusText == null)
            {
                return;
            }

            if (scriptMsgTimer > 0f)
            {
                scriptMsgTimer -= Time.deltaTime;
                scriptStatusText.text = scriptMsg;
                scriptStatusText.color = scriptMsgError ? new Color(1f, 0.45f, 0.4f) : UIFactory.AccentGreen;
                return;
            }

            if (scriptRunner == null)
            {
                scriptStatusText.text = "状态：脚本运行器未就绪";
                scriptStatusText.color = UIFactory.TextDim;
                return;
            }

            if (!scriptRunner.IsActive)
            {
                scriptStatusText.text = "状态：未注入\n点击「注入脚本」后生效";
                scriptStatusText.color = UIFactory.TextDim;
            }
            else if (!string.IsNullOrEmpty(scriptRunner.LastError))
            {
                scriptStatusText.text = "状态：已注入 · 运行出错（详见下方运行输出）";
                scriptStatusText.color = new Color(1f, 0.45f, 0.4f);
            }
            else
            {
                scriptStatusText.text = "状态：运行中（每 tick 执行）";
                scriptStatusText.color = UIFactory.AccentGreen;
            }
        }

        /// <summary>把脚本 print 的输出刷新到左栏「运行输出」区（未被注入时显示提示），并自动滚到最新。</summary>
        private void UpdateScriptOutput()
        {
            if (scriptOutputText == null)
            {
                return;
            }

            bool active = scriptRunner != null && scriptRunner.IsActive;
            string output = active ? scriptRunner.LastOutput : "";

            string show;
            bool scrollToBottom = false;
            if (!active)
            {
                // 未注入：显示提示并清空缓存
                scriptOutputCache = null;
                show = "（未注入脚本）";
            }
            else if (!string.IsNullOrEmpty(output))
            {
                // 有新输出：缓存并显示（避免被占位文本覆盖）
                scriptOutputCache = output;
                show = output;
                scrollToBottom = true;
            }
            else
            {
                // 运行中但本帧无输出：保留上次输出，避免闪回占位文本
                show = string.IsNullOrEmpty(scriptOutputCache) ? "（运行中，暂无 print 输出）" : scriptOutputCache;
            }

            if (scriptOutputText.text == show)
            {
                return;
            }

            scriptOutputText.text = show;
            if (scrollToBottom && scriptOutputScroll != null)
            {
                Canvas.ForceUpdateCanvases();
                scriptOutputScroll.verticalNormalizedPosition = 0f;
            }
        }

        /// <summary>脚本编辑框高度自适应：不小于视口高度（保证整块可点击），内容更长时随之增长以便滚动。</summary>
        private void UpdateScriptEditorLayout()
        {
            if (scriptInput == null || scriptViewportRect == null || !scriptInput.gameObject.activeInHierarchy)
            {
                return;
            }

            RectTransform inRT = scriptInput.GetComponent<RectTransform>();
            float viewportH = scriptViewportRect.rect.height;
            float preferred = LayoutUtility.GetPreferredHeight(inRT);
            float target = Mathf.Max(viewportH, preferred);
            if (Mathf.Abs(inRT.sizeDelta.y - target) > 0.5f)
            {
                inRT.sizeDelta = new Vector2(inRT.sizeDelta.x, target);
            }
        }

        /// <summary>按住 Ctrl 并在编辑区内滚动滚轮：放大 / 缩小脚本字号。</summary>
        private void UpdateScriptZoom()
        {
            if (scriptScroll != null)
            {
                scriptScroll.vertical = true;
            }

            if (scriptPanel == null || !scriptPanel.activeSelf || scriptInput == null || scriptViewportRect == null)
            {
                return;
            }

            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (!ctrl)
            {
                return;
            }

            if (!RectTransformUtility.RectangleContainsScreenPoint(scriptViewportRect, Input.mousePosition, null))
            {
                return;
            }

            // Ctrl 缩放期间暂停滚动，避免同时滚动
            if (scriptScroll != null)
            {
                scriptScroll.vertical = false;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) < 0.01f)
            {
                return;
            }

            ApplyScriptFontSize(scriptFontSize + (scroll > 0f ? 1 : -1));
        }

        private void ApplyScriptFontSize(int size)
        {
            int clamped = Mathf.Clamp(size, 8, 40);
            if (clamped == scriptFontSize)
            {
                return;
            }
            scriptFontSize = clamped;

            if (scriptInput != null)
            {
                scriptInput.textComponent.fontSize = clamped;
                if (scriptInput.placeholder is Text ph)
                {
                    ph.fontSize = clamped;
                }
            }
        }

        private void RefreshScriptList()
        {
            if (scriptListContainer == null)
            {
                return;
            }

            for (int i = scriptListContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = scriptListContainer.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            List<string> list = ScriptStorage.List();
            if (list.Count == 0)
            {
                Text empty = UIFactory.CreateText(scriptListContainer, "Empty",
                    "暂无本地脚本。\n请先在「脚本编写」面板中点击「保存脚本」。", 14, UIFactory.TextDim, TextAnchor.MiddleCenter);
                LayoutElement le = empty.gameObject.AddComponent<LayoutElement>();
                le.preferredHeight = 80f;
                return;
            }

            foreach (string path in list)
            {
                AddScriptRow(path);
            }
        }

        private void AddScriptRow(string path)
        {
            ScriptFile file = ScriptStorage.Read(path);
            if (file == null)
            {
                return;
            }

            GameObject rowGO = new GameObject("ScriptRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            rowGO.transform.SetParent(scriptListContainer, false);

            LayoutElement rowLE = rowGO.AddComponent<LayoutElement>();
            rowLE.preferredHeight = 48f;
            rowLE.minHeight = 48f;

            HorizontalLayoutGroup hlg = rowGO.GetComponent<HorizontalLayoutGroup>();
            hlg.spacing = 8f;
            hlg.childAlignment = TextAnchor.MiddleLeft;
            hlg.childControlWidth = true;
            hlg.childControlHeight = true;
            hlg.childForceExpandWidth = false;
            hlg.childForceExpandHeight = true;

            string label = $"{file.name}    {file.createdAt}    行数 {CountLines(file.source)}";
            Button btn = UIFactory.CreateButton(rowGO.transform, "Script_" + Path.GetFileNameWithoutExtension(path), label, 14);
            LayoutElement loadLE = btn.gameObject.AddComponent<LayoutElement>();
            loadLE.flexibleWidth = 1f;
            loadLE.minHeight = 48f;

            Text t = btn.GetComponentInChildren<Text>();
            if (t != null)
            {
                t.alignment = TextAnchor.MiddleLeft;
                RectTransform tRT = t.rectTransform;
                tRT.offsetMin = new Vector2(14, 0);
                tRT.offsetMax = new Vector2(-14, 0);
            }

            string captured = path;
            btn.onClick.AddListener(() => LoadScriptFile(captured));

            Button del = UIFactory.CreateButton(rowGO.transform, "Delete_" + Path.GetFileNameWithoutExtension(path), "删除", 14);
            LayoutElement delLE = del.gameObject.AddComponent<LayoutElement>();
            delLE.preferredWidth = 72f;
            delLE.minWidth = 72f;
            delLE.minHeight = 48f;

            Image delImg = del.GetComponent<Image>();
            delImg.color = Color.white;
            ColorBlock dcb = del.colors;
            dcb.normalColor = new Color(0.35f, 0.1f, 0.12f, 0.9f);
            dcb.highlightedColor = new Color(0.5f, 0.15f, 0.18f, 0.95f);
            dcb.pressedColor = new Color(0.7f, 0.2f, 0.22f, 1f);
            dcb.selectedColor = new Color(0.6f, 0.18f, 0.2f, 1f);
            del.colors = dcb;

            del.onClick.AddListener(() => DeleteScriptFile(captured));
        }

        private void LoadScriptFile(string path)
        {
            ScriptFile file = ScriptStorage.Read(path);
            if (file == null)
            {
                ShowScriptMsg("读取失败，请查看控制台。", true);
                return;
            }
            if (scriptInput != null)
            {
                scriptInput.text = file.source;
            }
            CloseScriptListPanel();
            ShowScriptMsg("已读取脚本：" + file.name, false);
        }

        private void DeleteScriptFile(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (!ScriptStorage.Delete(path))
            {
                ShowScriptMsg("删除失败，请查看控制台。", true);
                return;
            }
            ShowScriptMsg("已删除本地脚本：" + name, false);
            RefreshScriptList();
        }

        private static int CountLines(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }
            int n = 1;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\n')
                {
                    n++;
                }
            }
            return n;
        }

        // ===================== 窗口拖拽 / 边框缩放 =====================
        private void RegisterWindow(RectTransform panel, RectTransform titleBar, float minWidth, float minHeight)
        {
            windows.Add(new WindowState
            {
                panel = panel,
                titleBar = titleBar,
                minWidth = minWidth,
                minHeight = minHeight
            });
        }

        /// <summary>
        /// 所有弹窗统一行为：拖标题栏移动窗口，拖四边 / 四角缩放（Windows 窗口模式）。
        /// </summary>
        private void UpdateWindows()
        {
            float scale = canvas != null ? canvas.scaleFactor : 1f;
            if (scale <= 0f)
            {
                scale = 1f;
            }

            if (Input.GetMouseButtonDown(0))
            {
                activeWindow = null;
                draggingWindow = false;
                resizeEdge = 0;

                // 后加入的窗口在上层，优先响应
                for (int i = windows.Count - 1; i >= 0; i--)
                {
                    WindowState w = windows[i];
                    if (w.panel == null)
                    {
                        continue;
                    }

                    // 窗口所在的 overlay 根节点不可见时跳过
                    Transform root = w.panel.parent != null ? w.panel.parent : w.panel;
                    if (!root.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    Vector2 local;
                    if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            w.panel, Input.mousePosition, null, out local))
                    {
                        continue;
                    }

                    Rect r = w.panel.rect;
                    if (local.x < r.xMin || local.x > r.xMax || local.y < r.yMin || local.y > r.yMax)
                    {
                        continue;
                    }

                    // 边缘 → 缩放（把屏幕像素换算成 canvas 单位）
                    float edge = WindowEdgeSize / scale;
                    int e = 0;
                    if (local.x <= r.xMin + edge) e |= 1;   // 左
                    if (local.x >= r.xMax - edge) e |= 2;   // 右
                    if (local.y <= r.yMin + edge) e |= 4;   // 下
                    if (local.y >= r.yMax - edge) e |= 8;   // 上

                    bool onTitle = false;
                    if (e == 0 && w.titleBar != null)
                    {
                        Vector2 tl;
                        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                                w.titleBar, Input.mousePosition, null, out tl))
                        {
                            onTitle = w.titleBar.rect.Contains(tl);
                        }
                    }

                    if (e == 0 && !onTitle)
                    {
                        // 点在内容区：最上层窗口已接管该次点击，不再向下层窗口查找
                        break;
                    }

                    activeWindow = w;
                    resizeEdge = e;
                    draggingWindow = onTitle;
                    winDragStartMouse = Input.mousePosition;
                    winDragStartAnchored = w.panel.anchoredPosition;
                    winDragStartSize = w.panel.sizeDelta;
                    break;
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                activeWindow = null;
                draggingWindow = false;
                resizeEdge = 0;
            }

            if (activeWindow == null || activeWindow.panel == null || !Input.GetMouseButton(0))
            {
                return;
            }

            RectTransform panel = activeWindow.panel;

            if (draggingWindow)
            {
                Vector2 d = ((Vector2)Input.mousePosition - winDragStartMouse) / scale;
                panel.anchoredPosition = winDragStartAnchored + d;
                return;
            }

            if (resizeEdge == 0)
            {
                return;
            }

            Vector2 delta = ((Vector2)Input.mousePosition - winDragStartMouse) / scale;
            float w0 = winDragStartSize.x;
            float h0 = winDragStartSize.y;
            float cx = winDragStartAnchored.x;
            float cy = winDragStartAnchored.y;

            float left = cx - w0 * 0.5f;
            float right = cx + w0 * 0.5f;
            float bottom = cy - h0 * 0.5f;
            float top = cy + h0 * 0.5f;

            float newW = w0;
            float newH = h0;

            // 拖动某条边时，保持对边不动
            if ((resizeEdge & 2) != 0)
            {
                newW = Mathf.Max(activeWindow.minWidth, w0 + delta.x);
                cx = left + newW * 0.5f;
            }
            else if ((resizeEdge & 1) != 0)
            {
                newW = Mathf.Max(activeWindow.minWidth, w0 - delta.x);
                cx = right - newW * 0.5f;
            }

            if ((resizeEdge & 8) != 0)
            {
                newH = Mathf.Max(activeWindow.minHeight, h0 + delta.y);
                cy = bottom + newH * 0.5f;
            }
            else if ((resizeEdge & 4) != 0)
            {
                newH = Mathf.Max(activeWindow.minHeight, h0 - delta.y);
                cy = top - newH * 0.5f;
            }

            panel.sizeDelta = new Vector2(newW, newH);
            panel.anchoredPosition = new Vector2(cx, cy);
        }

        private void UpdateRebuildStatus()
        {
            if (rebuildStatusText == null || pointCloudRebuilder == null)
            {
                return;
            }

            if (rebuildPanel == null || !rebuildPanel.activeSelf)
            {
                return;
            }

            // 「回到实时建模」仅在查看本地存档时可用，避免实时模式下误清空
            if (backToLiveBtn != null)
            {
                backToLiveBtn.interactable = !pointCloudRebuilder.LiveEnabled;
            }

            if (pointCloudRebuilder.HasResult)
            {
                MeshFilter mf = pointCloudRebuilder.ResultMeshFilter;
                if (mf != null && mf.sharedMesh != null)
                {
                    rebuildStatusText.text = $"截面 {pointCloudRebuilder.SectionCount} 环 · 网格 {mf.sharedMesh.vertexCount} 顶点 / {mf.sharedMesh.triangles.Length / 3} 三角形";
                }
                else
                {
                    rebuildStatusText.text = $"已累积 {pointCloudRebuilder.SectionCount} 环截面";
                }
            }
            else if (!pointCloudRebuilder.LiveEnabled)
            {
                rebuildStatusText.text = "正在查看本地存档…";
            }
            else
            {
                rebuildStatusText.text = $"正在采集截面… 当前 {pointCloudRebuilder.SectionCount} 环";
            }
        }

        // ===================== 退出确认 =====================
        private void BuildQuitConfirm()
        {
            // 全屏半透明遮罩（拦截下层点击）
            GameObject overlayGO = new GameObject("QuitOverlay", typeof(RectTransform), typeof(Image));
            overlayGO.transform.SetParent(canvas.transform, false);
            overlayGO.transform.SetAsLastSibling();

            Image overlayImg = overlayGO.GetComponent<Image>();
            overlayImg.color = new Color(0f, 0f, 0f, 0.55f);

            RectTransform overlayRT = overlayGO.GetComponent<RectTransform>();
            overlayRT.anchorMin = Vector2.zero;
            overlayRT.anchorMax = Vector2.one;
            overlayRT.offsetMin = Vector2.zero;
            overlayRT.offsetMax = Vector2.zero;

            // 确认面板
            RectTransform panel = UIFactory.CreatePanel(overlayGO.transform, "QuitPanel", UIFactory.BgPanel, UIFactory.BorderCyan);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = Vector2.zero;
            panel.sizeDelta = new Vector2(380, 190);

            AddWindowTitleBar(panel, "退出确认", HideQuitConfirm, 340f, 170f);

            Text msg = UIFactory.CreateText(panel, "Msg", "确认退出程序？", 18, UIFactory.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform mRT = msg.GetComponent<RectTransform>();
            mRT.anchorMin = new Vector2(0, 0.45f);
            mRT.anchorMax = new Vector2(1, 1);
            mRT.offsetMin = new Vector2(16, 0);
            mRT.offsetMax = new Vector2(-16, -42);

            Button confirmBtn = UIFactory.CreateButton(panel, "ConfirmBtn", "确定退出", 15);
            RectTransform cRT = confirmBtn.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0, 0);
            cRT.anchorMax = new Vector2(0.5f, 0.5f);
            cRT.offsetMin = new Vector2(24, 18);
            cRT.offsetMax = new Vector2(-10, -24);
            confirmBtn.onClick.AddListener(QuitApplication);

            Button cancelBtn = UIFactory.CreateButton(panel, "CancelBtn", "取消", 15);
            RectTransform caRT = cancelBtn.GetComponent<RectTransform>();
            caRT.anchorMin = new Vector2(0.5f, 0);
            caRT.anchorMax = new Vector2(1, 0.5f);
            caRT.offsetMin = new Vector2(10, 18);
            caRT.offsetMax = new Vector2(-24, -24);
            cancelBtn.onClick.AddListener(HideQuitConfirm);

            quitConfirmPanel = overlayGO;
            quitConfirmPanel.SetActive(false);
        }

        private void ShowQuitConfirm()
        {
            if (quitConfirmPanel != null)
            {
                quitConfirmPanel.SetActive(true);
            }
        }

        private void HideQuitConfirm()
        {
            if (quitConfirmPanel != null)
            {
                quitConfirmPanel.SetActive(false);
            }
        }

        /// <summary>按 ESC：优先关闭当前模态面板，否则弹出退出确认。</summary>
        private void HandleQuitInput()
        {
            if (!Input.GetKeyDown(KeyCode.Escape))
            {
                return;
            }

            if (chartPanel != null && chartPanel.activeSelf)
            {
                HideChartPanel();
            }
            else if (imagePanel != null && imagePanel.activeSelf)
            {
                CloseImagePanel();
            }
            else if (archivePanel != null && archivePanel.activeSelf)
            {
                CloseArchivePanel();
            }
            else if (scriptNamePanel != null && scriptNamePanel.activeSelf)
            {
                CloseScriptNamePanel();
            }
            else if (scriptListPanel != null && scriptListPanel.activeSelf)
            {
                CloseScriptListPanel();
            }
            else if (scriptPanel != null && scriptPanel.activeSelf)
            {
                CloseScriptPanel();
            }
            else if (rebuildPanel != null && rebuildPanel.activeSelf)
            {
                CloseRebuildPanel();
            }
            else if (quitConfirmPanel != null && quitConfirmPanel.activeSelf)
            {
                HideQuitConfirm();
            }
            else
            {
                ShowQuitConfirm();
            }
        }

        private void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
