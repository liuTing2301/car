using System;
using UnityEngine;
using UnityWebSocket;

namespace DigitalTwin
{
    /// <summary>
    /// 数字孪生 WebSocket 客户端，负责与后端建立连接、发送遥测、接收指令。
    /// 默认不自动连接（后端接入暂缓），由管理器通过 AutoConnect 开关控制。
    /// </summary>
    public class DigitalTwinClient : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string serverUrl = "ws://127.0.0.1:8080";
        [SerializeField] private bool autoConnect = false;
        [SerializeField] private float reconnectInterval = 3f;

        /// <summary>连接成功时触发。</summary>
        public event Action OnConnected;

        /// <summary>连接断开时触发，参数为断开原因。</summary>
        public event Action<string> OnDisconnected;

        /// <summary>收到文本消息时触发，参数为消息内容。</summary>
        public event Action<string> OnMessageReceived;

        private IWebSocket socket;
        private bool manuallyClosed;
        private float reconnectTimer;

        public bool IsConnected => socket != null && socket.ReadyState == WebSocketState.Open;
        public string ServerUrl
        {
            get => serverUrl;
            set => serverUrl = value;
        }

        public bool AutoConnect
        {
            get => autoConnect;
            set => autoConnect = value;
        }

        private void Start()
        {
            if (autoConnect)
            {
                Connect();
            }
        }

        private void Update()
        {
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

        public void Connect()
        {
            if (socket != null &&
                (socket.ReadyState == WebSocketState.Open || socket.ReadyState == WebSocketState.Connecting))
            {
                return;
            }

            manuallyClosed = false;
            socket = new WebSocket(serverUrl);
            socket.OnOpen += HandleOpen;
            socket.OnMessage += HandleMessage;
            socket.OnClose += HandleClose;
            socket.OnError += HandleError;
            socket.ConnectAsync();
        }

        public void Send(string message)
        {
            if (IsConnected && !string.IsNullOrEmpty(message))
            {
                socket.SendAsync(message);
            }
        }

        public void Close()
        {
            manuallyClosed = true;
            if (socket != null)
            {
                socket.CloseAsync();
            }
        }

        private void HandleOpen(object sender, OpenEventArgs e)
        {
            OnConnected?.Invoke();
        }

        private void HandleClose(object sender, CloseEventArgs e)
        {
            OnDisconnected?.Invoke(e.Reason);
        }

        private void HandleError(object sender, ErrorEventArgs e)
        {
            Debug.LogWarning($"[DigitalTwin] WebSocket 错误：{e.Message}");
        }

        private void HandleMessage(object sender, MessageEventArgs e)
        {
            if (e.IsText)
            {
                OnMessageReceived?.Invoke(e.Data);
            }
            else
            {
                Debug.Log($"[DigitalTwin] 收到二进制消息：{e.RawData?.Length ?? 0} 字节");
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
