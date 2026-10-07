using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 数字孪生全局管理器（单例），负责在运行时自动装配小车、相机、遥测采集、HUD 与 WebSocket 客户端。
    /// 无需手动挂载到场景：若场景中不存在，会在加载场景后自动创建。
    /// </summary>
    public class DigitalTwinManager : MonoBehaviour
    {
        public static DigitalTwinManager Instance { get; private set; }

        [Header("后端接入（暂缓，默认关闭）")]
        [Tooltip("开启后启用遥测采集与 WebSocket 连接。")]
        [SerializeField] private bool enableBackend = false;

        [Header("小车连接（WebSocket 接收）")]
        [Tooltip("小车 WebSocket 服务地址（IP）。")]
        [SerializeField] private string robotServerIp = "192.168.1.50";
        [Tooltip("小车 WebSocket 服务端口。")]
        [SerializeField] private int robotServerPort = 8000;

        [Header("Targets（留空则自动查找）")]
        [SerializeField] private Transform carTransform;
        [SerializeField] private Rigidbody carRigidbody;
        [SerializeField] private Transform cameraTransform;

        [Header("Components")]
        [SerializeField] private CarController carController;
        [SerializeField] private TelemetryCollector telemetry;
        [SerializeField] private DigitalTwinClient client;
        [SerializeField] private DigitalTwinUI ui;
        [SerializeField] private CameraReceiver cameraReceiver;
        [SerializeField] private CameraRotor cameraRotor;
        [SerializeField] private RobotDataReader robotDataReader;
        [SerializeField] private RobotControlSender robotControlSender;
        [SerializeField] private DynamicPipeModel dynamicPipeModel;
        [SerializeField] private PointCloudRebuilder pointCloudRebuilder;

        public CarController Car => carController;
        public TelemetryCollector Telemetry => telemetry;
        public DigitalTwinClient Client => client;
        public DigitalTwinUI UI => ui;
        public CameraRotor CameraRotor => cameraRotor;
        public RobotDataReader RobotData => robotDataReader;
        public RobotControlSender RobotControl => robotControlSender;
        public DynamicPipeModel DynamicPipe => dynamicPipeModel;
        public PointCloudRebuilder PointCloudRebuilder => pointCloudRebuilder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance == null && FindObjectOfType<DigitalTwinManager>() == null)
            {
                new GameObject("DigitalTwinManager").AddComponent<DigitalTwinManager>();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            AutoResolve();
            Wire();
        }

        private void AutoResolve()
        {
            if (carTransform == null)
            {
                GameObject car = GameObject.FindWithTag("Player");
                if (car == null)
                {
                    car = GameObject.Find("ModelCar");
                }

                if (car != null)
                {
                    carTransform = car.transform;
                }
            }

            if (cameraTransform == null)
            {
                Camera cam = Camera.main;
                if (cam == null)
                {
                    cam = FindObjectOfType<Camera>();
                }
                if (cam != null)
                {
                    cameraTransform = cam.transform;
                }
            }

            if (carTransform != null && carRigidbody == null)
            {
                carRigidbody = carTransform.GetComponent<Rigidbody>();
            }
        }

        private void Wire()
        {
            if (carTransform != null)
            {
                carController = carTransform.GetComponent<CarController>();
                if (carController == null)
                {
                    carController = carTransform.gameObject.AddComponent<CarController>();
                }
            }

            if (cameraTransform != null)
            {
                CameraFollow follow = cameraTransform.GetComponent<CameraFollow>();
                if (follow == null)
                {
                    follow = cameraTransform.gameObject.AddComponent<CameraFollow>();
                }
                follow.Setup(carTransform);
            }

            telemetry = GetComponent<TelemetryCollector>();
            if (telemetry == null)
            {
                telemetry = gameObject.AddComponent<TelemetryCollector>();
            }
            telemetry.Setup(carTransform, carRigidbody);

            client = GetComponent<DigitalTwinClient>();
            if (client == null)
            {
                client = gameObject.AddComponent<DigitalTwinClient>();
            }
            client.AutoConnect = enableBackend;

            cameraReceiver = GetComponent<CameraReceiver>();
            if (cameraReceiver == null)
            {
                cameraReceiver = gameObject.AddComponent<CameraReceiver>();
            }

            robotDataReader = GetComponent<RobotDataReader>();
            if (robotDataReader == null)
            {
                robotDataReader = gameObject.AddComponent<RobotDataReader>();
            }
            robotDataReader.ServerIp = robotServerIp;
            robotDataReader.Port = robotServerPort;

            if (carTransform != null)
            {
                cameraRotor = carTransform.GetComponent<CameraRotor>();
                if (cameraRotor == null)
                {
                    cameraRotor = carTransform.gameObject.AddComponent<CameraRotor>();
                }
            }

            robotControlSender = GetComponent<RobotControlSender>();
            if (robotControlSender == null)
            {
                robotControlSender = gameObject.AddComponent<RobotControlSender>();
            }
            robotControlSender.Setup(robotDataReader, carController, cameraRotor);

            GameObject pipeModelGO = new GameObject("DynamicPipeModel");
            dynamicPipeModel = pipeModelGO.AddComponent<DynamicPipeModel>();
            dynamicPipeModel.Setup(robotDataReader, carTransform);
            pipeModelGO.SetActive(false); // 默认隐藏，点「管道模型」查看

            pointCloudRebuilder = GetComponent<PointCloudRebuilder>();
            if (pointCloudRebuilder == null)
            {
                pointCloudRebuilder = gameObject.AddComponent<PointCloudRebuilder>();
            }
            pointCloudRebuilder.Setup(robotDataReader, carTransform);

            ui = GetComponent<DigitalTwinUI>();
            if (ui == null)
            {
                ui = gameObject.AddComponent<DigitalTwinUI>();
            }
            ui.Setup(carController, carTransform, client, cameraReceiver, cameraRotor, robotDataReader, dynamicPipeModel, pointCloudRebuilder);

            GameObject cube = GameObject.Find("Cube");
            if (cube != null && cube.GetComponent<RulerGenerator>() == null)
            {
                cube.AddComponent<RulerGenerator>();
            }

            // 后端暂缓：默认关闭遥测采集与 WebSocket，仅保留本地移动与 UI。
            telemetry.enabled = enableBackend;
            client.enabled = enableBackend;
            telemetry.OnTelemetryReady -= OnTelemetry;
            if (enableBackend)
            {
                telemetry.OnTelemetryReady += OnTelemetry;
            }
        }

        private void OnTelemetry(string json)
        {
            if (client != null)
            {
                client.Send(json);
            }
        }
    }
}
