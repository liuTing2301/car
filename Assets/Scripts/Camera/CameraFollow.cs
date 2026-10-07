using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 第三人称环绕相机：鼠标右键拖动旋转视角，滚轮缩放远近。
    /// 环绕中心为小车根节点（target.position），相机始终看向小车。
    /// </summary>
    public class CameraFollow : MonoBehaviour
    {
        [Header("目标")]
        [SerializeField] private Transform target;

        [Header("视角控制")]
        [SerializeField] private float rotateSpeed = 3f;
        [SerializeField] private float zoomSpeed = 5f;
        [SerializeField] private float minDistance = 1f;
        [SerializeField] private float maxDistance = 1000f;
        [SerializeField] private float pitchMin = -80f;
        [SerializeField] private float pitchMax = 80f;

        [Header("初始距离")]
        [Tooltip("根据模型尺寸自动计算初始跟随距离。")]
        [SerializeField] private bool autoDistance = true;
        [SerializeField] private float distanceFactor = 1.6f;
        [SerializeField] private float defaultDistance = 10f;
        [SerializeField] private float initialPitch = 20f;

        private float yaw;
        private float pitch;
        private float distance;
        private bool initialized;

        public void Setup(Transform target)
        {
            this.target = target;
            initialized = false;
        }

        private void Initialize()
        {
            distance = defaultDistance;
            yaw = 0f;
            pitch = initialPitch;

            if (target != null)
            {
                Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    for (int i = 1; i < renderers.Length; i++)
                    {
                        bounds.Encapsulate(renderers[i].bounds);
                    }

                    if (autoDistance)
                    {
                        distance = Mathf.Max(1f, bounds.size.magnitude * distanceFactor);
                    }
                }
            }

            initialized = true;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            // 点云重建弹窗打开时禁用视角旋转缩放
            if (DigitalTwinUI.RebuildPanelOpen)
            {
                return;
            }

            if (!initialized)
            {
                Initialize();
            }

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * rotateSpeed;
                pitch -= Input.GetAxis("Mouse Y") * rotateSpeed;
                pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
            }

            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.0001f)
            {
                distance -= scroll * zoomSpeed * distance;
                distance = Mathf.Clamp(distance, minDistance, maxDistance);
            }

            Vector3 visualCenter = target.position;
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = visualCenter - rotation * Vector3.forward * distance;
            transform.rotation = Quaternion.LookRotation(visualCenter - transform.position);
        }
    }
}
