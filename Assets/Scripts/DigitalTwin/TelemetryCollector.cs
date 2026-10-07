using System;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 按固定频率采集小车遥测数据，并通过事件向外抛出 JSON 字符串。
    /// </summary>
    public class TelemetryCollector : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Rigidbody targetRigidbody;
        [SerializeField] private string deviceId = "car-001";
        [SerializeField] private float interval = 0.1f;

        /// <summary>采集到一条遥测数据（JSON 字符串）时触发。</summary>
        public event Action<string> OnTelemetryReady;

        private float timer;

        public string DeviceId
        {
            get => deviceId;
            set => deviceId = value;
        }

        public float Interval
        {
            get => interval;
            set => interval = Mathf.Max(0.05f, value);
        }

        /// <summary>绑定采集目标。</summary>
        public void Setup(Transform target, Rigidbody rigidbody)
        {
            this.target = target;
            this.targetRigidbody = rigidbody;
        }

        private void Update()
        {
            if (target == null)
            {
                return;
            }

            timer += Time.deltaTime;
            if (timer >= interval)
            {
                timer = 0f;
                Collect();
            }
        }

        /// <summary>立即采集一次。</summary>
        public void CollectNow()
        {
            if (target != null)
            {
                Collect();
            }
        }

        private void Collect()
        {
            TelemetryData data = new TelemetryData
            {
                deviceId = deviceId,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            data.Fill(target, targetRigidbody);
            OnTelemetryReady?.Invoke(data.ToJson());
        }
    }
}
