using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 摄像头云台旋转控制器：挂在小车根节点上。
    /// 按住 E 键持续旋转摄像头云台（带平滑动画），并记录当前旋转角度。
    /// 自动巡航时可按设定速度自动旋转，用于管道环视扫描。
    /// 优先查找小车下已存在的摄像头/云台子物体；找不到时自动创建一个可视化云台。
    /// </summary>
    public class CameraRotor : MonoBehaviour
    {
        [Header("手动旋转")]
        [Tooltip("按住 E 键时云台旋转速度（度/秒）。")]
        [SerializeField] private float manualRotateSpeed = 90f;

        [Header("自动巡航旋转")]
        [Tooltip("自动巡航时云台旋转速度（度/秒），转满一圈后自动反向往复。")]
        [SerializeField] private float autoRotateSpeed = 30f;

        [Header("自动往复")]
        [Tooltip("自动巡航时，云台转过该角度（度）后自动反向，默认一圈 360°。")]
        [SerializeField] private float reverseAngle = 360f;

        [Header("旋转轴")]
        [Tooltip("摄像头云台旋转轴（默认绕 X 轴）。")]
        [SerializeField] private Vector3 rotateAxis = Vector3.right;
        [Tooltip("跟随部件（如圆角5）旋转轴（默认绕 Z 轴）。")]
        [SerializeField] private Vector3 followerAxis = Vector3.forward;

        [Header("动画")]
        [Tooltip("旋转平滑系数，越大越快收敛到目标角度。")]
        [SerializeField] private float smoothSpeed = 8f;

        [Header("云台")]
        [Tooltip("按名称关键字在小车下查找已有摄像头/云台子物体（避免误匹配观察相机）。")]
        [SerializeField] private string[] gimbalKeywords = { "摄像头", "云台", "gimbal", "镜头" };
        [Tooltip("找不到时自动创建一个可视化云台。")]
        [SerializeField] private bool autoCreateGimbal = true;

        [Header("跟随部件")]
        [Tooltip("需要跟随摄像头一起旋转的部件名关键字（如镜头部件「圆角5」）。这些部件会与云台同步绕 rotateAxis 旋转。")]
        [SerializeField] private string[] followerKeywords = { "圆角5" };

        private Transform gimbal;
        private Quaternion baseRotation = Quaternion.identity;
        private readonly System.Collections.Generic.List<Follower> followers =
            new System.Collections.Generic.List<Follower>();
        private bool autoRotate;
        private float targetAngle;
        private float currentAngle;

        // 自动往复控制：方向（1 正向 / -1 反向）、自上次反转以来累积的旋转角度、上一帧角度
        private float autoRotateDirection = 1f;
        private float autoRotateAccumulated;
        private float lastCurrentAngle;

        /// <summary>需要跟随摄像头同步旋转的部件。</summary>
        private class Follower
        {
            public Transform transform;
            public Quaternion baseRotation;
        }

        /// <summary>当前云台旋转角度（0 ~ 360，归一化）。</summary>
        public float CurrentAngle => Normalize(currentAngle);
        /// <summary>自动旋转速度（度/秒）。</summary>
        public float AutoRotateSpeed
        {
            get => autoRotateSpeed;
            set => autoRotateSpeed = Mathf.Max(0f, value);
        }
        /// <summary>手动旋转速度（度/秒）。</summary>
        public float ManualRotateSpeed
        {
            get => manualRotateSpeed;
            set => manualRotateSpeed = Mathf.Max(0f, value);
        }
        /// <summary>当前自动旋转的实际速度（带方向，正/负），用于下发给下位机。</summary>
        public float CurrentAutoRotateSpeed => autoRotateSpeed * autoRotateDirection;
        /// <summary>当前是否正在自动旋转（自动巡航且未手动干预）。</summary>
        public bool IsAutoRotating => autoRotate && !Input.GetKey(KeyCode.E);
        /// <summary>云台物体（供外部访问，可为 null）。</summary>
        public Transform Gimbal => gimbal;

        private void Awake()
        {
            ResolveGimbal();
            ResolveFollowers();
        }

        /// <summary>开启/关闭自动旋转（自动巡航时由模式控制调用）。</summary>
        public void SetAutoRotate(bool enabled)
        {
            autoRotate = enabled;
            if (enabled)
            {
                // 重新开启自动旋转时，从正方向开始并清空累积
                autoRotateDirection = 1f;
                autoRotateAccumulated = 0f;
                lastCurrentAngle = currentAngle;
            }
        }

        private void Update()
        {
            if (gimbal == null)
            {
                return;
            }

            // 点云重建弹窗打开时禁用云台旋转
            if (DigitalTwinUI.RebuildPanelOpen)
            {
                lastCurrentAngle = currentAngle;
                return;
            }

            bool isAutoRotating = autoRotate && !Input.GetKey(KeyCode.E);

            // 按住 E 键手动旋转优先；否则自动巡航时按方向自动旋转
            float speed = 0f;
            if (Input.GetKey(KeyCode.E))
            {
                speed = manualRotateSpeed;
            }
            else if (autoRotate)
            {
                speed = autoRotateSpeed * autoRotateDirection;
            }

            if (Mathf.Abs(speed) > 0.01f)
            {
                targetAngle += speed * Time.deltaTime;
            }

            // 平滑动画：向目标角度插值
            currentAngle = Mathf.Lerp(currentAngle, targetAngle, smoothSpeed * Time.deltaTime);

            // 自动往复：监测 Unity 实际旋转角度，转满 reverseAngle 后自动反向
            if (isAutoRotating)
            {
                float delta = currentAngle - lastCurrentAngle;
                autoRotateAccumulated += delta;
                if (Mathf.Abs(autoRotateAccumulated) >= reverseAngle)
                {
                    autoRotateDirection *= -1f;
                    autoRotateAccumulated = 0f;
                }
            }
            else
            {
                // 手动或停止时清空累积，避免重新进入自动时误判
                autoRotateAccumulated = 0f;
            }
            lastCurrentAngle = currentAngle;

            // 摄像头云台绕 rotateAxis（X 轴）旋转，保持原本角度
            Quaternion gimbalDelta = Quaternion.Euler(rotateAxis * currentAngle);
            gimbal.localRotation = baseRotation * gimbalDelta;

            // 跟随部件（如圆角5）绕 followerAxis（Z 轴）旋转，保持原本角度
            Quaternion followerDelta = Quaternion.Euler(followerAxis * currentAngle);
            foreach (Follower follower in followers)
            {
                follower.transform.localRotation = follower.baseRotation * followerDelta;
            }
        }

        /// <summary>查找或创建云台物体。</summary>
        private void ResolveGimbal()
        {
            Transform[] all = GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                if (t == transform)
                {
                    continue;
                }

                string name = t.name.ToLower();
                foreach (string keyword in gimbalKeywords)
                {
                    if (name.Contains(keyword.ToLower()))
                    {
                        gimbal = t;
                        break;
                    }
                }
                if (gimbal != null)
                {
                    break;
                }
            }

            if (gimbal == null && autoCreateGimbal)
            {
                gimbal = CreateGimbal();
            }

            if (gimbal != null)
            {
                // 记录部件原本的初始角度，旋转时在其基础上叠加，避免覆盖原始朝向
                baseRotation = gimbal.localRotation;
            }
            else
            {
                Debug.LogWarning("[CameraRotor] 未找到摄像头云台子物体，请在 gimbalKeywords 中补充关键字或开启 autoCreateGimbal。");
            }
        }

        /// <summary>查找需要跟随摄像头一起旋转的部件（如镜头「圆角5」）。</summary>
        private void ResolveFollowers()
        {
            followers.Clear();
            if (followerKeywords == null || followerKeywords.Length == 0)
            {
                return;
            }

            Transform[] all = GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                if (t == transform)
                {
                    continue;
                }

                string name = t.name.ToLower();
                foreach (string keyword in followerKeywords)
                {
                    if (name.Contains(keyword.ToLower()))
                    {
                        followers.Add(new Follower { transform = t, baseRotation = t.localRotation });
                        break;
                    }
                }
            }

            if (followers.Count == 0)
            {
                Debug.LogWarning("[CameraRotor] 未找到跟随部件（关键字：" + string.Join(", ", followerKeywords) + "），请检查模型中的部件名。");
            }
        }

        /// <summary>在小车顶部创建一个简单的可视化云台（底座 + 镜头）。</summary>
        private Transform CreateGimbal()
        {
            // 计算车顶位置
            Vector3 top = transform.position;
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
                top = bounds.center + Vector3.up * bounds.extents.y;
            }

            GameObject root = new GameObject("CameraGimbal");
            root.transform.SetParent(transform, false);
            root.transform.position = top;

            // 镜头（朝前的小球）
            GameObject lens = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lens.name = "Lens";
            lens.transform.SetParent(root.transform, false);
            lens.transform.localPosition = new Vector3(0f, 0f, 0.22f);
            lens.transform.localScale = Vector3.one * 0.16f;
            RemoveCollider(lens);
            SetColor(lens, new Color(0.1f, 0.8f, 0.95f));

            // 镜筒（圆柱默认沿 Y 轴，旋转 90° 让其水平朝前）
            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = "Barrel";
            barrel.transform.SetParent(root.transform, false);
            barrel.transform.localPosition = new Vector3(0f, 0f, 0.06f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.09f, 0.16f, 0.09f);
            RemoveCollider(barrel);
            SetColor(barrel, new Color(0.2f, 0.26f, 0.32f));

            return root.transform;
        }

        private void RemoveCollider(GameObject go)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null)
            {
                Destroy(col);
            }
        }

        private void SetColor(GameObject go, Color color)
        {
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.material.color = color;
            }
        }

        private static float Normalize(float angle)
        {
            angle %= 360f;
            if (angle < 0f)
            {
                angle += 360f;
            }
            return angle;
        }
    }
}
