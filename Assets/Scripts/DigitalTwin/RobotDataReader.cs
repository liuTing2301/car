using System;
using System.Collections.Generic;
using UnityEngine;
using UnityWebSocket;

namespace DigitalTwin
{
    /// <summary>
    /// 小车数据只读客户端：通过 WebSocket 连接小车（小车为 ws 服务端），
    /// 实时接收 status JSON 数据包（管道数据、检测结果、base64 图像），
    /// 解析后通过属性/事件对外暴露最新一包数据，供 UI、雷达点云等消费。
    /// 断线自动重连。
    /// </summary>
    public class RobotDataReader : MonoBehaviour
    {
        [Header("Connection")]
        [Tooltip("小车 WebSocket 服务地址（IP）。")]
        [SerializeField] private string serverIp = "10.130.38.131";
        [SerializeField] private int port = 8000;
        [Tooltip("WebSocket 路径，默认 /ws。")]
        [SerializeField] private string path = "/ws";
        [Tooltip("启动时自动连接。")]
        [SerializeField] private bool autoConnect = true;
        [SerializeField] private float reconnectInterval = 3f;

        /// <summary>收到一帧完整 status 包时触发（主线程）。</summary>
        public event Action<StatusPacket> OnStatusReceived;

        /// <summary>收到新图像时触发（主线程）。</summary>
        public event Action<Texture2D> OnImageReceived;

        /// <summary>status 包中包含点云数据时触发（主线程）。</summary>
        public event Action<StatusPacket> OnPointCloudReceived;

        public event Action OnConnected;
        public event Action<string> OnDisconnected;

        private IWebSocket socket;
        private bool manuallyClosed;
        private float reconnectTimer;

        /// <summary>最新管道数据（可能为 null）。</summary>
        public PipeData LatestPipe { get; private set; }

        /// <summary>最新检测结果列表。</summary>
        public List<Detection> LatestDetections { get; private set; } = new List<Detection>();

        /// <summary>最新图像。</summary>
        public Texture2D LatestImage { get; private set; }

        /// <summary>最近一次数据更新时间。</summary>
        public DateTime UpdateTime { get; private set; }

        public bool IsConnected => socket != null && socket.ReadyState == WebSocketState.Open;

        public string ServerIp { get => serverIp; set => serverIp = value; }
        public int Port { get => port; set => port = value; }
        public bool AutoConnect { get => autoConnect; set => autoConnect = value; }
        public string ServerUrl => $"ws://{serverIp}:{port}{path}";

        private void Start()
        {
            if (autoConnect)
            {
                Connect();
            }
        }

        private void Update()
        {
            // 断线自动重连
            if (manuallyClosed || socket == null)
            {
                return;
            }

            if (socket.ReadyState == WebSocketState.Closed)
            {
                reconnectTimer += Time.deltaTime;
                if (reconnectTimer >= reconnectInterval)
                {
                    reconnectTimer = 0f;
                    Connect();
                }
            }
        }

        /// <summary>连接小车 WebSocket 服务。</summary>
        public void Connect()
        {
            if (socket != null &&
                (socket.ReadyState == WebSocketState.Open || socket.ReadyState == WebSocketState.Connecting))
            {
                return;
            }

            manuallyClosed = false;
            socket = new WebSocket(ServerUrl);
            socket.OnOpen += HandleOpen;
            socket.OnMessage += HandleMessage;
            socket.OnClose += HandleClose;
            socket.OnError += HandleError;
            socket.ConnectAsync();
        }

        public void Close()
        {
            manuallyClosed = true;
            if (socket != null)
            {
                socket.CloseAsync();
            }
        }

        /// <summary>关闭当前连接，并用最新的 ServerIp/Port 重新连接（修改地址后调用）。</summary>
        public void Reconnect()
        {
            if (socket != null)
            {
                socket.CloseAsync();
            }

            manuallyClosed = false;
            socket = new WebSocket(ServerUrl);
            socket.OnOpen += HandleOpen;
            socket.OnMessage += HandleMessage;
            socket.OnClose += HandleClose;
            socket.OnError += HandleError;
            socket.ConnectAsync();
            Debug.Log($"[RobotDataReader] 重新连接 {ServerUrl}");
        }

        /// <summary>发送文本消息到小车（连接未建立时忽略）。</summary>
        public void Send(string message)
        {
            if (IsConnected && !string.IsNullOrEmpty(message))
            {
                socket.SendAsync(message);
            }
        }

        /// <summary>发送一帧控制指令（轮电机转速、摄像头电机转速等）。</summary>
        public void SendControl(ControlPacket packet)
        {
            if (packet == null)
            {
                return;
            }
            Send(JsonUtility.ToJson(packet));
        }

        private void HandleOpen(object sender, OpenEventArgs e)
        {
            Debug.Log($"[RobotDataReader] 已连接小车 {ServerUrl}");
            OnConnected?.Invoke();
        }

        private void HandleClose(object sender, CloseEventArgs e)
        {
            Debug.Log($"[RobotDataReader] 连接断开：{e.Reason}");
            OnDisconnected?.Invoke(e.Reason);
        }

        private void HandleError(object sender, ErrorEventArgs e)
        {
            Debug.LogWarning($"[RobotDataReader] WebSocket 错误：{e.Message}");
        }

        private void HandleMessage(object sender, MessageEventArgs e)
        {
            if (!e.IsText)
            {
                return;
            }

            try
            {
                StatusPacket pkt = JsonUtility.FromJson<StatusPacket>(e.Data);
                if (pkt == null || pkt.type != "status")
                {
                    return;
                }

                LatestPipe = pkt.pipe_data;
                LatestDetections = pkt.detections ?? new List<Detection>();
                UpdateTime = DateTime.Now;

                if (!string.IsNullOrEmpty(pkt.image_b64))
                {
                    DecodeImage(pkt.image_b64);
                }

                OnStatusReceived?.Invoke(pkt);

                // 点云与其他数据一起在 status 包的 scan 字段中下发
                if (pkt.scan != null && pkt.scan.angles != null && pkt.scan.distances != null)
                {
                    OnPointCloudReceived?.Invoke(pkt);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RobotDataReader] 解析数据失败：{ex.Message}");
            }
        }

        private void DecodeImage(string base64)
        {
            try
            {
                byte[] bytes = Convert.FromBase64String(base64);
                if (LatestImage == null)
                {
                    LatestImage = new Texture2D(2, 2);
                }

                if (LatestImage.LoadImage(bytes))
                {
                    OnImageReceived?.Invoke(LatestImage);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RobotDataReader] 图像解码失败：{ex.Message}");
            }
        }

        private void OnDestroy()
        {
            manuallyClosed = true;
            if (socket != null)
            {
                socket.CloseAsync();
            }
        }
    }
}
