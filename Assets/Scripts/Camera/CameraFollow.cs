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

        [Header("取景偏移")]
        [Tooltip("把小车在屏幕上整体上移的比例（0 = 居中）。给中栏底部「缺陷检测结果」预览框让出空间，避免遮挡小车。")]
        [SerializeField] private float framingOffsetUp = 0.10f;

        [Header("登录展示")]
        [Tooltip("登录界面展示时，先拉远到整车刚好入画的距离，再缓慢环绕。该值越小画面里的小车越大（1.2 = 留 20% 余量）。")]
        [SerializeField] private float showcaseDistanceFactor = 1.2f;
        [SerializeField] private float showcasePitch = 20f;
        [Tooltip("拉远过渡速度（越大越快）。")]
        [SerializeField] private float showcaseZoomLerp = 2.5f;
        [Tooltip("环绕角速度（度/秒）。")]
        [SerializeField] private float showcaseYawSpeed = 12f;

        private float yaw;
        private float pitch;
        private float distance;
        private bool initialized;
        private Camera cam;

        // 登录展示状态（进入时保存跟随参数，登录结束后恢复）
        private bool showcaseInited;
        private Vector3 showcaseCenter;
        private float showcaseDistance;
        private float savedYaw;
        private float savedPitch;
        private float savedDistance;

        public void Setup(Transform target)
        {
            this.target = target;
            initialized = false;
        }

        private void Initialize()
        {
            cam = GetComponent<Camera>();
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

            // 登录界面：相机拉远到整车入画，然后缓慢环绕小车
            if (LoginScreen.IsOpen)
            {
                UpdateLoginShowcase();
                return;
            }
            if (showcaseInited)
            {
                EndLoginShowcase();
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

            // 取景上移：让小车在屏幕上整体抬高，给底部「缺陷检测结果」预览框让出空间
            float offsetWorld = 0f;
            if (framingOffsetUp > 0.0001f)
            {
                float fov = cam != null ? cam.fieldOfView : 60f;
                float visibleHeight = 2f * distance * Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
                offsetWorld = visibleHeight * framingOffsetUp;
            }
            Vector3 lookPoint = visualCenter - rotation * Vector3.up * offsetWorld;

            transform.position = lookPoint - rotation * Vector3.forward * distance;
            transform.rotation = Quaternion.LookRotation(lookPoint - transform.position);
        }

        /// <summary>
        /// 登录界面展示：先把相机从当前跟随距离平滑拉远，再绕小车缓慢环绕。
        /// 拉远距离以「当前跟随距离的 1.5~4 倍」为限，既保证整车入画，又不会因为包围盒异常而跑到车外。
        /// 登录期间 Time.timeScale = 0，因此全部使用 unscaledDeltaTime。
        /// </summary>
        private void UpdateLoginShowcase()
        {
            if (!initialized)
            {
                Initialize();
            }

            if (!showcaseInited)
            {
                StartLoginShowcase();
            }

            float t = 1f - Mathf.Exp(-showcaseZoomLerp * Time.unscaledDeltaTime);

            // 先拉远（同时把俯视角过渡到展示角度）
            distance = Mathf.Lerp(distance, showcaseDistance, t);
            pitch = Mathf.Lerp(pitch, showcasePitch, t);

            // 拉远基本到位后再开始环绕，避免一开始就在远处快速转
            if (Mathf.Abs(distance - showcaseDistance) <= showcaseDistance * 0.03f)
            {
                distance = showcaseDistance;
                yaw += showcaseYawSpeed * Time.unscaledDeltaTime;
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = showcaseCenter - rotation * Vector3.forward * distance;
            transform.rotation = Quaternion.LookRotation(showcaseCenter - transform.position);
        }

        private void StartLoginShowcase()
        {
            // 保存跟随状态，登录结束后恢复，避免登录过场影响正常操控视角
            savedYaw = yaw;
            savedPitch = pitch;
            savedDistance = distance;

            // 以当前跟随距离（正常游玩时能看见车的距离）为基准；
            // 环绕中心直接用小车根节点（与正常跟随一致），避免个别渲染器把中心带偏导致看不见车
            float current = Mathf.Max(1f, distance);
            showcaseCenter = target.position;
            showcaseDistance = current * 1.8f;

            Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                // 分别用竖直 / 水平视场算出「刚好装下整车」的距离，取较大者；
                // 环绕时车的长边与宽边会互换，所以水平方向取 x/z 中较大的尺寸。
                float fov = cam != null ? cam.fieldOfView : 60f;
                float aspect = cam != null ? cam.aspect : 16f / 9f;
                float vFov = fov * Mathf.Deg2Rad;
                float hFov = 2f * Mathf.Atan(Mathf.Tan(vFov * 0.5f) * aspect);
                float hSize = Mathf.Max(bounds.size.x, bounds.size.z);

                float dV = bounds.size.y * 0.5f / Mathf.Max(0.01f, Mathf.Tan(vFov * 0.5f));
                float dH = hSize * 0.5f / Mathf.Max(0.01f, Mathf.Tan(hFov * 0.5f));
                float fit = Mathf.Max(dV, dH) * showcaseDistanceFactor;

                // 关键：只拉远不拉近（至少为当前距离的 1.5 倍，保证一开始就看得见车），
                // 同时限制在 4 倍以内，防止包围盒异常时镜头被拉飞
                showcaseDistance = Mathf.Clamp(fit, current * 1.5f, current * 4f);
            }

            showcaseInited = true;
        }

        private void EndLoginShowcase()
        {
            yaw = savedYaw;
            pitch = savedPitch;
            distance = savedDistance;
            showcaseInited = false;
        }
    }
}
