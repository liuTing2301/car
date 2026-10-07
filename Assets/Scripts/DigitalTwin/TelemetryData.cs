using System;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 数字孪生遥测数据模型，用于序列化后通过 WebSocket 发送给后端孪生体。
    /// </summary>
    [Serializable]
    public class TelemetryData
    {
        public string type = "telemetry";
        public string deviceId = "car-001";
        public long timestamp;
        public float positionX;
        public float positionY;
        public float positionZ;
        public float rotationX;
        public float rotationY;
        public float rotationZ;
        public float velocityX;
        public float velocityY;
        public float velocityZ;
        public float speed;

        /// <summary>
        /// 从目标 Transform 与 Rigidbody 填充当前状态。
        /// </summary>
        public void Fill(Transform target, Rigidbody rigidbody)
        {
            positionX = target.position.x;
            positionY = target.position.y;
            positionZ = target.position.z;

            Vector3 euler = target.eulerAngles;
            rotationX = euler.x;
            rotationY = euler.y;
            rotationZ = euler.z;

            if (rigidbody != null)
            {
                velocityX = rigidbody.velocity.x;
                velocityY = rigidbody.velocity.y;
                velocityZ = rigidbody.velocity.z;
                speed = rigidbody.velocity.magnitude;
            }
        }

        public string ToJson()
        {
            return JsonUtility.ToJson(this);
        }
    }
}
