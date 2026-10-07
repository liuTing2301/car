using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 小车运动控制：W / S（或上下方向键）前后移动，并根据车速旋转车轮。
    /// 默认使用 Transform 直接移动（更稳定），可切换为 Rigidbody 物理驱动。
    /// </summary>
    public class CarController : MonoBehaviour
    {
        [Header("驱动参数")]
        [SerializeField] private float maxSpeed = 10f;
        [SerializeField] private float acceleration = 20f;

        [Header("自适应")]
        [Tooltip("根据模型实际尺寸自动调整 maxSpeed，使速度感与模型大小匹配。")]
        [SerializeField] private bool autoScaleSpeed = true;

        [Header("自动巡航")]
        [Tooltip("自动巡航时的行驶速度（m/s），与手动模式的 maxSpeed 独立。")]
        [SerializeField] private float cruiseSpeed = 2f;

        [Header("移动模式")]
        [Tooltip("使用 Rigidbody 物理驱动；关闭则用 Transform 直接移动（更稳定）。")]
        [SerializeField] private bool usePhysics = false;
        [Tooltip("锁定初始高度，避免小车掉落。")]
        [SerializeField] private bool keepHeight = true;

        [Header("车轮动画")]
        [SerializeField] private bool rotateWheels = true;
        [Tooltip("每前进 1 米车轮旋转的角度（度/米），按车轮半径调整。")]
        [SerializeField] private float wheelSpinSpeed = 100f;
        [SerializeField] private Vector3 wheelAxis = Vector3.right;
        [Tooltip("自动查找名字含这些关键字的子物体作为车轮。")]
        [SerializeField] private string[] wheelKeywords = { "wheel", "tire", "tyre", "轮" };
        [Tooltip("手动指定车轮；非空时优先使用，忽略关键字自动查找。")]
        [SerializeField] private List<Transform> manualWheels = new List<Transform>();

        private Rigidbody rb;
        private float currentSpeed;
        private float initialHeight;
        private float totalDistance;
        private bool autoCruise;
        private readonly List<Transform> wheels = new List<Transform>();

        public float Speed => Mathf.Abs(currentSpeed);
        /// <summary>油门：-1（后退）~ 1（前进）。</summary>
        public float Throttle { get; private set; }
        public float MaxSpeed => maxSpeed;
        public float TotalDistance => totalDistance;
        public bool IsAutoCruise => autoCruise;

        /// <summary>自动巡航行驶速度（m/s）。</summary>
        public float CruiseSpeed
        {
            get => cruiseSpeed;
            set => cruiseSpeed = Mathf.Max(0f, value);
        }

        /// <summary>开启自动巡航时，小车自动以前进油门行驶。</summary>
        public void SetAutoCruise(bool enabled)
        {
            autoCruise = enabled;
        }

        /// <summary>设定自动巡航行驶速度（m/s）。</summary>
        public void SetCruiseSpeed(float speed)
        {
            cruiseSpeed = Mathf.Max(0f, speed);
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            initialHeight = transform.position.y;

            if (autoScaleSpeed)
            {
                AutoScaleSpeed();
            }

            ApplyRigidbodyMode();
            FindWheels();
        }

        private void FindWheels()
        {
            wheels.Clear();

            if (manualWheels.Count > 0)
            {
                foreach (Transform w in manualWheels)
                {
                    if (w != null)
                    {
                        wheels.Add(w);
                    }
                }
                return;
            }

            Transform[] all = GetComponentsInChildren<Transform>(true);
            foreach (Transform t in all)
            {
                string name = t.name.ToLower();
                foreach (string keyword in wheelKeywords)
                {
                    if (name.Contains(keyword.ToLower()))
                    {
                        wheels.Add(t);
                        break;
                    }
                }
            }

            if (wheels.Count == 0)
            {
                Debug.LogWarning("[CarController] 未找到车轮，请在 Inspector 的 manualWheels 中手动指定。");
            }
        }

        private void AutoScaleSpeed()
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            float size = Mathf.Max(0.1f, bounds.size.magnitude);
            maxSpeed = size * 0.5f;
            acceleration = maxSpeed * 2f;
        }

        private void ApplyRigidbodyMode()
        {
            if (rb == null)
            {
                return;
            }

            if (usePhysics)
            {
                rb.isKinematic = false;
                rb.useGravity = !keepHeight;
                if (keepHeight)
                {
                    rb.constraints = RigidbodyConstraints.FreezeRotationX |
                                     RigidbodyConstraints.FreezeRotationZ |
                                     RigidbodyConstraints.FreezePositionY;
                }
            }
            else
            {
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        private void Update()
        {
            Throttle = autoCruise ? 1f : Input.GetAxisRaw("Vertical");

            if (!usePhysics)
            {
                MoveTransform(Time.deltaTime);
            }

            RotateWheels(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (usePhysics)
            {
                MovePhysics();
            }
        }

        private void MoveTransform(float dt)
        {
            float targetSpeed = Throttle * SpeedLimit;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * dt);

            transform.position += transform.forward * currentSpeed * dt;
            totalDistance += Mathf.Abs(currentSpeed) * dt;

            if (keepHeight)
            {
                Vector3 p = transform.position;
                p.y = initialHeight;
                transform.position = p;
            }
        }

        private void MovePhysics()
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            forward.Normalize();

            float targetSpeed = Throttle * SpeedLimit;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.fixedDeltaTime);

            Vector3 velocity = forward * currentSpeed;
            rb.velocity = new Vector3(velocity.x, rb.velocity.y, velocity.z);
            totalDistance += Mathf.Abs(currentSpeed) * Time.fixedDeltaTime;
        }

        /// <summary>当前模式下的速度上限：自动巡航用巡航速度，手动用最大速度。</summary>
        private float SpeedLimit => autoCruise ? cruiseSpeed : maxSpeed;

        private void RotateWheels(float dt)
        {
            if (!rotateWheels || wheels.Count == 0)
            {
                return;
            }

            float angle = currentSpeed * wheelSpinSpeed * dt;
            foreach (Transform w in wheels)
            {
                if (w != null)
                {
                    w.Rotate(wheelAxis, angle, Space.Self);
                }
            }
        }
    }
}
