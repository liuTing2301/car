using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    /// <summary>
    /// 数字孪生主界面：参考"智能工程装备数字孪生平台"风格，
    /// 构建顶部标题栏、左侧传感器/模式栏、右侧遥测+雷达+摄像头面板。
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
        private Text statusText;

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
        private bool draggingResize;
        private Vector2 resizeStartMouse;
        private Vector2 panelStartSize;
        private RectTransform rebuildPanelRect;
        private DataChart deviationChart;
        private DataChart ellipticityChart;
        private DataChart confidenceChart;
        private DataChart radiusChart;
        private Text deviationValueText;
        private Text ellipticityValueText;
        private Text confidenceValueText;
        private Text radiusValueText;

        private float radarMockTimer;
        private bool pointCloudActive;

        public void Setup(CarController car, Transform carTransform, DigitalTwinClient client, CameraReceiver cam, CameraRotor cameraRotor, RobotDataReader robotDataReader, DynamicPipeModel dynamicPipeModel, PointCloudRebuilder pointCloudRebuilder)
        {
            this.car = car;
            this.carTransform = carTransform;
            this.client = client;
            this.cameraReceiver = cam;
            this.cameraRotor = cameraRotor;
            this.robotDataReader = robotDataReader;
            this.dynamicPipeModel = dynamicPipeModel;
            this.pointCloudRebuilder = pointCloudRebuilder;
        }

        private void Start()
        {
            BuildCanvas();
            BuildTopBar();
            BuildLeftSidebar();
            BuildRightColumn();
            BuildChartPanel();
            BuildRebuildPanel();
            BuildImagePanel();
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
            HandlePanelResize();
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

        // ===================== 顶部栏 =====================
        private void BuildTopBar()
        {
            RectTransform bar = UIFactory.CreatePanel(canvas.transform, "TopBar", UIFactory.BgDark, UIFactory.BorderCyan);
            bar.anchorMin = new Vector2(0, 1);
            bar.anchorMax = new Vector2(1, 1);
            bar.pivot = new Vector2(0.5f, 1);
            bar.anchoredPosition = Vector2.zero;
            bar.sizeDelta = new Vector2(-20, 50);

            // 左侧 Logo + 标题
            Text logo = UIFactory.CreateText(bar, "Logo", "◉", 28, UIFactory.AccentCyan, TextAnchor.MiddleCenter);
            RectTransform lRT = logo.GetComponent<RectTransform>();
            lRT.anchorMin = new Vector2(0, 0);
            lRT.anchorMax = new Vector2(0, 1);
            lRT.pivot = new Vector2(0, 0.5f);
            lRT.anchoredPosition = new Vector2(16, 0);
            lRT.sizeDelta = new Vector2(40, 0);

            Text title = UIFactory.CreateText(bar, "Title", "管道探测小车 · 数字孪生平台", 22, UIFactory.TextPrimary,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform tRT = title.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0, 0);
            tRT.anchorMax = new Vector2(0.5f, 1);
            tRT.pivot = new Vector2(0, 0.5f);
            tRT.anchoredPosition = new Vector2(60, 0);
            tRT.sizeDelta = new Vector2(400, 0);

            // 右侧 状态 + 时间
            statusText = UIFactory.CreateText(bar, "Status", "● 系统就绪", 14, UIFactory.AccentGreen, TextAnchor.MiddleRight);
            RectTransform sRT = statusText.GetComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0.5f, 0);
            sRT.anchorMax = new Vector2(1, 1);
            sRT.pivot = new Vector2(1, 0.5f);
            sRT.anchoredPosition = new Vector2(-220, 0);
            sRT.sizeDelta = new Vector2(180, 0);

            timeText = UIFactory.CreateText(bar, "Time", "", 16, UIFactory.TextPrimary, TextAnchor.MiddleRight);
            RectTransform tiRT = timeText.GetComponent<RectTransform>();
            tiRT.anchorMin = new Vector2(1, 0);
            tiRT.anchorMax = new Vector2(1, 1);
            tiRT.pivot = new Vector2(1, 0.5f);
            tiRT.anchoredPosition = new Vector2(-88, 0);
            tiRT.sizeDelta = new Vector2(200, 0);

            // 退出按钮
            Button quitBtn = UIFactory.CreateButton(bar, "QuitBtn", "退出", 14);
            RectTransform qRT = quitBtn.GetComponent<RectTransform>();
            qRT.anchorMin = new Vector2(1, 0);
            qRT.anchorMax = new Vector2(1, 1);
            qRT.pivot = new Vector2(1, 0.5f);
            qRT.anchoredPosition = new Vector2(-12, 0);
            qRT.sizeDelta = new Vector2(56, 30);
            quitBtn.onClick.AddListener(ShowQuitConfirm);
        }

        // ===================== 左侧栏 =====================
        private void BuildLeftSidebar()
        {
            RectTransform sidebar = UIFactory.CreatePanel(canvas.transform, "LeftSidebar", UIFactory.BgPanel, UIFactory.BorderCyan);
            sidebar.anchorMin = new Vector2(0, 0);
            sidebar.anchorMax = new Vector2(0, 1);
            sidebar.pivot = new Vector2(0, 0.5f);
            sidebar.anchoredPosition = new Vector2(10, -30);
            sidebar.sizeDelta = new Vector2(230, -70);

            // 内容容器（避开边框和标题）
            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(sidebar, false);
            RectTransform content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(8, 8);
            content.offsetMax = new Vector2(-8, -8);

            float y = -6;

            // 连接设置（IP / 端口运行中可修改）
            Text secConn = UIFactory.CreateText(content, "SecConn", "▎ 连接设置", 16, UIFactory.AccentCyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            LayoutY(secConn, ref y, 20);

            Text ipLabel = UIFactory.CreateText(content, "IpLabel", "IP 地址", 12, UIFactory.TextDim, TextAnchor.MiddleLeft);
            LayoutY(ipLabel, ref y, 14);

            ipInput = UIFactory.CreateInputField(content, "IpInput", robotDataReader != null ? robotDataReader.ServerIp : "10.130.38.131", 13);
            Layout(ipInput, ref y, 24);

            Text portLabel = UIFactory.CreateText(content, "PortLabel", "端口", 12, UIFactory.TextDim, TextAnchor.MiddleLeft);
            LayoutY(portLabel, ref y, 14);

            portInput = UIFactory.CreateInputField(content, "PortInput", robotDataReader != null ? robotDataReader.Port.ToString() : "8888", 13, InputField.ContentType.IntegerNumber);
            Layout(portInput, ref y, 24);

            connectBtn = UIFactory.CreateButton(content, "ConnectBtn", "连接", 14);
            Layout(connectBtn, ref y, 26);
            connectBtn.onClick.AddListener(OnConnectClicked);

            pipeStatusText = UIFactory.CreateText(content, "PipeStatus", "连接：未连接", 12, UIFactory.AccentOrange, TextAnchor.MiddleLeft);
            LayoutY(pipeStatusText, ref y, 18);

            y -= 8;

            // 检测数据（坐标图）
            Text secData = UIFactory.CreateText(content, "SecData", "▎ 检测数据", 16, UIFactory.AccentCyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            LayoutY(secData, ref y, 22);

            Button chartBtn = UIFactory.CreateButton(content, "ChartBtn", "数据曲线", 14);
            Layout(chartBtn, ref y, 34);
            chartBtn.onClick.AddListener(ShowChartPanel);

            y -= 8;

            // 模式选择
            Text sec2 = UIFactory.CreateText(content, "Sec2", "▎ 驾驶模式", 16, UIFactory.AccentCyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            LayoutY(sec2, ref y, 22);

            modeController = gameObject.AddComponent<ModeController>();
            modeController.Setup(car, cameraRotor);
            // 给 modeController 一个容器
            GameObject modeContainer = new GameObject("ModeContainer", typeof(RectTransform));
            modeContainer.transform.SetParent(content, false);
            RectTransform mc = modeContainer.GetComponent<RectTransform>();
            mc.anchorMin = new Vector2(0, 1);
            mc.anchorMax = new Vector2(1, 1);
            mc.pivot = new Vector2(0.5f, 1);
            mc.anchoredPosition = new Vector2(0, y);
            mc.sizeDelta = new Vector2(0, 212);
            modeController.Build(mc);
            y -= 218;

            y -= 10;

            // 数字建模
            Text sec3 = UIFactory.CreateText(content, "Sec3", "▎ 数字建模", 16, UIFactory.AccentCyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            LayoutY(sec3, ref y, 22);

            // 管道模型（动态半透明管道，点击切换显示）
            pipeModelBtn = UIFactory.CreateButton(content, "PipeModelBtn", "管道模型", 14);
            RectTransform pmRT = pipeModelBtn.GetComponent<RectTransform>();
            pmRT.anchorMin = new Vector2(0, 1);
            pmRT.anchorMax = new Vector2(1, 1);
            pmRT.pivot = new Vector2(0.5f, 1);
            pmRT.anchoredPosition = new Vector2(0, y);
            pmRT.sizeDelta = new Vector2(0, 30);
            pipeModelBtn.onClick.AddListener(TogglePipeModel);
            y -= 34;

            // 点云重建（滚球法 3D 重建）
            Button rebuildBtn = UIFactory.CreateButton(content, "RebuildBtn", "点云重建", 14);
            RectTransform rbRT = rebuildBtn.GetComponent<RectTransform>();
            rbRT.anchorMin = new Vector2(0, 1);
            rbRT.anchorMax = new Vector2(1, 1);
            rbRT.pivot = new Vector2(0.5f, 1);
            rbRT.anchoredPosition = new Vector2(0, y);
            rbRT.sizeDelta = new Vector2(0, 30);
            rebuildBtn.onClick.AddListener(OpenRebuildPanel);
            y -= 34;
        }

        private void LayoutY(Text t, ref float y, float h)
        {
            RectTransform r = t.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(0, y);
            r.sizeDelta = new Vector2(0, h);
            y -= h + 4;
        }

        private void Layout(Component comp, ref float y, float h)
        {
            RectTransform r = comp.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1);
            r.anchoredPosition = new Vector2(0, y);
            r.sizeDelta = new Vector2(0, h);
            y -= h + 4;
        }

        // ===================== 右侧栏 =====================
        private void BuildRightColumn()
        {
            RectTransform column = UIFactory.CreatePanel(canvas.transform, "RightColumn", UIFactory.BgPanel, UIFactory.BorderCyan);
            column.anchorMin = new Vector2(1, 0);
            column.anchorMax = new Vector2(1, 1);
            column.pivot = new Vector2(1, 0.5f);
            column.anchoredPosition = new Vector2(-10, -30);
            column.sizeDelta = new Vector2(320, -70);

            // 内容容器
            GameObject contentGO = new GameObject("Content", typeof(RectTransform));
            contentGO.transform.SetParent(column, false);
            RectTransform content = contentGO.GetComponent<RectTransform>();
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = new Vector2(8, 8);
            content.offsetMax = new Vector2(-8, -8);

            // 三个面板按垂直比例排布（等分，留间隙）
            RectTransform telePanel = CreateSubPanel(content, "TelemetryPanel", "▎ 行驶状态 · 速度 / 距离",
                new Vector2(0, 0.672f), new Vector2(1, 1f), new Vector2(0, 0), new Vector2(0, -4));
            BuildTelemetryPanel(telePanel);

            RectTransform radarPanel = CreateSubPanel(content, "RadarPanel", "▎ 雷达扫描 · 点云",
                new Vector2(0, 0.336f), new Vector2(1, 0.664f), new Vector2(0, 4), new Vector2(0, -4));
            radar = gameObject.AddComponent<RadarScanner>();
            radar.Build(radarPanel, canvas);

            RectTransform camPanel = CreateSubPanel(content, "CameraPanel", "▎ 摄像头识别",
                new Vector2(0, 0f), new Vector2(1, 0.328f), new Vector2(0, 4), new Vector2(0, 0));
            BuildCameraPanel(camPanel);
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
            body.offsetMin = new Vector2(6, 6);
            body.offsetMax = new Vector2(-6, -34);

            return body;
        }

        private void BuildTelemetryPanel(RectTransform body)
        {
            // 上半：速度数值 + 图表
            speedText = UIFactory.CreateText(body, "Speed", "速度 0.00 m/s", 20, UIFactory.AccentCyan, TextAnchor.MiddleLeft, FontStyle.Bold);
            RectTransform sRT = speedText.GetComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0, 0.75f);
            sRT.anchorMax = new Vector2(0.5f, 1);
            sRT.offsetMin = new Vector2(8, 0);
            sRT.offsetMax = Vector2.zero;

            distanceText = UIFactory.CreateText(body, "Distance", "里程 0.00 m", 16, UIFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform dRT = distanceText.GetComponent<RectTransform>();
            dRT.anchorMin = new Vector2(0.5f, 0.75f);
            dRT.anchorMax = new Vector2(1, 1);
            dRT.offsetMin = new Vector2(8, 0);
            dRT.offsetMax = new Vector2(-8, 0);

            positionText = UIFactory.CreateText(body, "Position", "X 0.0  Y 0.0  Z 0.0", 12, UIFactory.TextDim, TextAnchor.MiddleLeft);
            RectTransform pRT = positionText.GetComponent<RectTransform>();
            pRT.anchorMin = new Vector2(0, 0.6f);
            pRT.anchorMax = new Vector2(1, 0.75f);
            pRT.offsetMin = new Vector2(8, 0);
            pRT.offsetMax = new Vector2(-8, 0);

            // 速度图表
            GameObject speedChartGO = new GameObject("SpeedChart", typeof(RectTransform), typeof(RawImage));
            speedChartGO.transform.SetParent(body, false);
            RectTransform scRT = speedChartGO.GetComponent<RectTransform>();
            scRT.anchorMin = new Vector2(0, 0);
            scRT.anchorMax = new Vector2(0.5f, 0.58f);
            scRT.offsetMin = new Vector2(4, 4);
            scRT.offsetMax = new Vector2(-2, -2);
            speedChart = gameObject.AddComponent<DataChart>();
            speedChart.Build(scRT, UIFactory.AccentCyan, car != null ? Mathf.Max(1f, car.MaxSpeed) : 10f);

            // 距离图表
            GameObject distChartGO = new GameObject("DistChart", typeof(RectTransform), typeof(RawImage));
            distChartGO.transform.SetParent(body, false);
            RectTransform dcRT = distChartGO.GetComponent<RectTransform>();
            dcRT.anchorMin = new Vector2(0.5f, 0);
            dcRT.anchorMax = new Vector2(1, 0.58f);
            dcRT.offsetMin = new Vector2(2, 4);
            dcRT.offsetMax = new Vector2(-4, -2);
            distanceChart = gameObject.AddComponent<DataChart>();
            distanceChart.Build(dcRT, UIFactory.AccentGreen, -1f);
        }

        private void BuildCameraPanel(RectTransform body)
        {
            // 摄像头画面
            GameObject camGO = new GameObject("CameraFeed", typeof(RectTransform), typeof(RawImage));
            camGO.transform.SetParent(body, false);
            cameraImage = camGO.GetComponent<RawImage>();
            cameraImage.color = new Color(0.1f, 0.1f, 0.1f, 1);
            cameraImage.raycastTarget = false;

            RectTransform camRT = camGO.GetComponent<RectTransform>();
            camRT.anchorMin = new Vector2(0, 0.25f);
            camRT.anchorMax = new Vector2(1, 1);
            camRT.offsetMin = new Vector2(4, 4);
            camRT.offsetMax = new Vector2(-4, -2);

            // AspectRatioFitter 保持比例
            AspectRatioFitter fitter = camGO.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 16f / 9f;

            // 检测框覆盖层（叠加在画面上方，用于绘制识别框与标签）
            GameObject overlayGO = new GameObject("DetectionOverlay", typeof(RectTransform));
            overlayGO.transform.SetParent(camGO.transform, false);
            cameraOverlay = overlayGO;
            RectTransform ovRT = overlayGO.GetComponent<RectTransform>();
            ovRT.anchorMin = Vector2.zero;
            ovRT.anchorMax = Vector2.one;
            ovRT.offsetMin = Vector2.zero;
            ovRT.offsetMax = Vector2.zero;

            // 识别结果
            recognitionText = UIFactory.CreateText(body, "Recognition", "识别结果：等待画面...", 13, UIFactory.AccentCyan,
                TextAnchor.UpperLeft, FontStyle.Normal);
            RectTransform rRT = recognitionText.GetComponent<RectTransform>();
            rRT.anchorMin = new Vector2(0, 0);
            rRT.anchorMax = new Vector2(1, 0.25f);
            rRT.offsetMin = new Vector2(8, 4);
            rRT.offsetMax = new Vector2(-8, -4);
            recognitionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            recognitionText.verticalOverflow = VerticalWrapMode.Truncate;

            // 云台角度（叠加在画面上方）
            cameraAngleText = UIFactory.CreateText(body, "GimbalAngle", "云台 0°", 14, UIFactory.AccentGreen,
                TextAnchor.UpperRight, FontStyle.Bold);
            RectTransform aRT = cameraAngleText.GetComponent<RectTransform>();
            aRT.anchorMin = new Vector2(0, 0.86f);
            aRT.anchorMax = new Vector2(1, 1);
            aRT.offsetMin = new Vector2(8, 0);
            aRT.offsetMax = new Vector2(-8, 0);
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
            if (timeText != null)
            {
                DateTime now = DateTime.Now;
                timeText.text = $"{now:HH:mm:ss}  {now:yyyy-MM-dd}  {GetWeekday(now.DayOfWeek)}";
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
                if (speedText != null) speedText.text = $"速度 {car.Speed:0.00} m/s";
                if (distanceText != null) distanceText.text = $"里程 {car.TotalDistance:0.00} m";
                if (speedChart != null) speedChart.AddValue(car.Speed);
                if (distanceChart != null) distanceChart.AddValue(car.TotalDistance);
            }

            if (carTransform != null && positionText != null)
            {
                Vector3 p = carTransform.position;
                positionText.text = $"X {p.x:0.0}  Y {p.y:0.0}  Z {p.z:0.0}  朝向 {carTransform.eulerAngles.y:0.0}°";
            }

            if (cameraAngleText != null && cameraRotor != null)
            {
                cameraAngleText.text = cameraRotor.IsAutoRotating
                    ? $"云台 {cameraRotor.CurrentAngle:0}°（自动环扫）"
                    : $"云台 {cameraRotor.CurrentAngle:0}°";
            }
        }

        private void UpdateStatus()
        {
            if (statusText == null) return;
            bool connected = client != null && client.IsConnected;
            statusText.text = connected ? "● 后端已连接" : "● 本地模式";
            statusText.color = connected ? UIFactory.AccentGreen : UIFactory.AccentOrange;
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

            Text title = UIFactory.CreateText(panel, "Title", "检测数据曲线", 20, UIFactory.AccentCyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform tRT = title.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0, 1);
            tRT.anchorMax = new Vector2(1, 1);
            tRT.pivot = new Vector2(0.5f, 1);
            tRT.anchoredPosition = Vector2.zero;
            tRT.sizeDelta = new Vector2(0, 40);

            // 2x2 四张曲线图
            deviationChart = CreateChartBlock(panel, "偏差", 0, 1, UIFactory.AccentCyan, out deviationValueText);
            ellipticityChart = CreateChartBlock(panel, "椭圆度", 1, 1, UIFactory.AccentGreen, out ellipticityValueText);
            confidenceChart = CreateChartBlock(panel, "置信度", 0, 0, UIFactory.AccentOrange, out confidenceValueText);
            radiusChart = CreateChartBlock(panel, "半径", 1, 0, new Color(0.7f, 0.5f, 1f), out radiusValueText);

            Button closeBtn = UIFactory.CreateButton(panel, "CloseBtn", "关闭", 15);
            RectTransform cRT = closeBtn.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(1, 0);
            cRT.anchorMax = new Vector2(1, 0);
            cRT.pivot = new Vector2(1, 0);
            cRT.anchoredPosition = new Vector2(-16, 12);
            cRT.sizeDelta = new Vector2(80, 34);
            closeBtn.onClick.AddListener(HideChartPanel);

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
            blockRT.offsetMin = new Vector2(24, 56);
            blockRT.offsetMax = new Vector2(-24, -60);

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
            rebuildPanelRect = panel;

            Text title = UIFactory.CreateText(panel, "Title", "点云重建（滚球法）", 20, UIFactory.AccentCyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform tRT = title.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0, 1);
            tRT.anchorMax = new Vector2(1, 1);
            tRT.pivot = new Vector2(0.5f, 1);
            tRT.anchoredPosition = Vector2.zero;
            tRT.sizeDelta = new Vector2(0, 40);

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
            rebuildStatusText = UIFactory.CreateText(panel, "Status", "等待雷达数据…将自动重建", 14, UIFactory.TextPrimary, TextAnchor.MiddleLeft);
            RectTransform sRT = rebuildStatusText.GetComponent<RectTransform>();
            sRT.anchorMin = new Vector2(0, 0);
            sRT.anchorMax = new Vector2(0.8f, 0.14f);
            sRT.offsetMin = new Vector2(20, 8);
            sRT.offsetMax = new Vector2(-8, -4);

            // 关闭按钮
            Button closeBtn = UIFactory.CreateButton(panel, "CloseBtn", "关闭", 15);
            RectTransform cRT = closeBtn.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(0.82f, 0);
            cRT.anchorMax = new Vector2(1, 0.14f);
            cRT.offsetMin = new Vector2(4, 8);
            cRT.offsetMax = new Vector2(-16, -4);
            closeBtn.onClick.AddListener(CloseRebuildPanel);

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

        /// <summary>弹窗打开时：鼠标左键拖拽旋转、滚轮缩放预览相机。</summary>
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
            if (!IsMouseOverPreview())
            {
                return;
            }

            bool changed = false;

            // 左键拖拽旋转
            if (Input.GetMouseButton(0))
            {
                previewYaw += Input.GetAxis("Mouse X") * 1.5f;
                previewPitch -= Input.GetAxis("Mouse Y") * 1.5f;
                previewPitch = Mathf.Clamp(previewPitch, -89f, 89f);
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

            imageTitle = UIFactory.CreateText(panel, "Title", "缺陷图片", 18, UIFactory.AccentCyan, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform tRT = imageTitle.GetComponent<RectTransform>();
            tRT.anchorMin = new Vector2(0, 1);
            tRT.anchorMax = new Vector2(1, 1);
            tRT.pivot = new Vector2(0.5f, 1);
            tRT.anchoredPosition = Vector2.zero;
            tRT.sizeDelta = new Vector2(0, 36);

            GameObject imgGO = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
            imgGO.transform.SetParent(panel, false);
            imagePreview = imgGO.GetComponent<RawImage>();
            imagePreview.color = Color.white;
            imagePreview.raycastTarget = false;
            RectTransform iRT = imgGO.GetComponent<RectTransform>();
            iRT.anchorMin = new Vector2(0, 0);
            iRT.anchorMax = new Vector2(1, 1);
            iRT.offsetMin = new Vector2(12, 48);
            iRT.offsetMax = new Vector2(-12, -40);

            Button closeBtn = UIFactory.CreateButton(panel, "CloseBtn", "关闭", 15);
            RectTransform cRT = closeBtn.GetComponent<RectTransform>();
            cRT.anchorMin = new Vector2(1, 0);
            cRT.anchorMax = new Vector2(1, 0);
            cRT.pivot = new Vector2(1, 0);
            cRT.anchoredPosition = new Vector2(-12, 12);
            cRT.sizeDelta = new Vector2(80, 32);
            closeBtn.onClick.AddListener(CloseImagePanel);

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

        /// <summary>拖拽弹窗右下角调整大小。</summary>
        private void HandlePanelResize()
        {
            if (rebuildPanelRect == null || rebuildPanel == null || !rebuildPanel.activeSelf)
            {
                return;
            }

            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(rebuildPanelRect, Input.mousePosition, null, out local);
            Rect rect = rebuildPanelRect.rect;
            const float handleSize = 40f;
            bool overHandle = local.x > rect.xMax - handleSize && local.y < rect.yMin + handleSize;

            if (overHandle && Input.GetMouseButtonDown(0))
            {
                draggingResize = true;
                resizeStartMouse = Input.mousePosition;
                panelStartSize = rebuildPanelRect.sizeDelta;
            }

            if (draggingResize && Input.GetMouseButton(0))
            {
                Vector2 delta = (Vector2)Input.mousePosition - resizeStartMouse;
                rebuildPanelRect.sizeDelta = new Vector2(
                    Mathf.Max(400f, panelStartSize.x + delta.x),
                    Mathf.Max(300f, panelStartSize.y - delta.y));
            }

            if (Input.GetMouseButtonUp(0))
            {
                draggingResize = false;
            }
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

            Text msg = UIFactory.CreateText(panel, "Msg", "确认退出程序？", 20, UIFactory.TextPrimary, TextAnchor.MiddleCenter, FontStyle.Bold);
            RectTransform mRT = msg.GetComponent<RectTransform>();
            mRT.anchorMin = new Vector2(0, 0.55f);
            mRT.anchorMax = new Vector2(1, 1);
            mRT.offsetMin = new Vector2(16, 0);
            mRT.offsetMax = new Vector2(-16, -10);

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
