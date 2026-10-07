using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 摄像头接收器：作为 TCP 服务器监听端口，半双工接收摄像头客户端
    /// 发来的 JPEG 图像（类型1）与识别结果 JSON（类型2）。
    /// 包格式：8 字节包头（大端 4 字节类型 + 4 字节长度）+ 数据体。
    /// 收到图像时通过 OnFrameReceived 事件抛出 Texture2D，由 DigitalTwinUI 显示。
    /// </summary>
    public class CameraReceiver : MonoBehaviour
    {
        [Header("监听配置")]
        [SerializeField] private int port = 8888;
        [SerializeField] private int maxImageBytes = 50 * 1024 * 1024;

        private Thread serverThread;
        private volatile bool running;
        private TcpListener listener;

        private readonly ConcurrentQueue<byte[]> jpegQueue = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<string> resultQueue = new ConcurrentQueue<string>();

        private Texture2D frameTexture;

        /// <summary>收到类型1图像时触发（主线程）。</summary>
        public event Action<Texture2D> OnFrameReceived;
        /// <summary>收到类型2识别结果（JSON 字符串）时触发（主线程）。</summary>
        public event Action<string> OnResultReceived;
        /// <summary>收到类型2识别结果并解析为检测列表时触发（主线程）。</summary>
        public event Action<List<Detection>> OnDetectionsReceived;

        public int Port => port;
        public bool IsRunning => running;

        private void Start()
        {
            StartServer();
        }

        // ===================== 服务器 =====================
        public void StartServer()
        {
            if (serverThread != null && serverThread.IsAlive)
            {
                return;
            }

            running = true;
            serverThread = new Thread(ServerLoop);
            serverThread.IsBackground = true;
            serverThread.Start();
        }

        private void ServerLoop()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Debug.Log($"[CameraReceiver] 监听端口 {port}，等待摄像头连接...");

                while (running)
                {
                    if (!listener.Pending())
                    {
                        Thread.Sleep(20);
                        continue;
                    }

                    TcpClient client = listener.AcceptTcpClient();
                    Debug.Log("[CameraReceiver] 摄像头已连接");
                    HandleClient(client);
                }
            }
            catch (Exception e)
            {
                if (running)
                {
                    Debug.LogError($"[CameraReceiver] 服务器错误：{e.Message}");
                }
            }
            finally
            {
                try { listener?.Stop(); } catch { }
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (NetworkStream stream = client.GetStream())
                {
                    while (running)
                    {
                        if (!ReadExact(stream, 8, out byte[] header))
                        {
                            break;
                        }

                        uint type = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];
                        uint length = ((uint)header[4] << 24) | ((uint)header[5] << 16) | ((uint)header[6] << 8) | header[7];

                        if (length > (uint)maxImageBytes)
                        {
                            Debug.LogWarning($"[CameraReceiver] 数据长度异常：{length} 字节，跳过");
                            break;
                        }

                        if (!ReadExact(stream, (int)length, out byte[] body))
                        {
                            break;
                        }

                        if (type == 1)
                        {
                            jpegQueue.Enqueue(body);
                        }
                        else if (type == 2)
                        {
                            resultQueue.Enqueue(Encoding.UTF8.GetString(body));
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log($"[CameraReceiver] 连接结束：{e.Message}");
            }
        }

        private bool ReadExact(NetworkStream stream, int length, out byte[] buffer)
        {
            buffer = new byte[length];
            int read = 0;
            while (read < length)
            {
                int n = stream.Read(buffer, read, length - read);
                if (n <= 0)
                {
                    return false;
                }
                read += n;
            }
            return true;
        }

        // ===================== 主线程处理 =====================
        private void Update()
        {
            while (jpegQueue.TryDequeue(out byte[] jpeg))
            {
                if (frameTexture == null)
                {
                    frameTexture = new Texture2D(2, 2);
                }

                if (frameTexture.LoadImage(jpeg))
                {
                    OnFrameReceived?.Invoke(frameTexture);
                }
            }

            while (resultQueue.TryDequeue(out string json))
            {
                Debug.Log($"[CameraReceiver] 识别结果：{json}");
                OnResultReceived?.Invoke(json);

                List<Detection> dets = ParseDetections(json);
                OnDetectionsReceived?.Invoke(dets);
            }
        }

        /// <summary>将 Maix 发来的顶层 JSON 数组解析为检测列表。</summary>
        private List<Detection> ParseDetections(string json)
        {
            List<Detection> result = new List<Detection>();
            if (string.IsNullOrEmpty(json))
            {
                return result;
            }

            try
            {
                // Maix 发送的是顶层 JSON 数组，JsonUtility 无法直接解析，
                // 因此包一层 {"items": [...]} 再反序列化。
                DetectionList list = JsonUtility.FromJson<DetectionList>("{\"items\":" + json + "}");
                if (list != null && list.items != null)
                {
                    result = list.items;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CameraReceiver] 解析识别结果失败：{e.Message}");
            }

            return result;
        }

        private void OnDestroy()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            if (frameTexture != null)
            {
                Destroy(frameTexture);
            }
        }
    }
}
