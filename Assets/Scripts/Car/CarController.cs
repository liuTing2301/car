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
        [Tooltip("手动模式速度上限（cm/s）。")]
        [SerializeField] private float manualMaxSpeed = 10f;
        [SerializeField] private float acceleration = 20f;

        [Header("占空比换算")]
        [Tooltip("占空比 → 线速度的换算系数（cm/s 每占空比）。0.7 占空比对应 10.4 cm/s，则约 14.86。")]
        [SerializeField] private float dutySpeedFactor = 14.86f;

        [Header("虚拟速度")]
        [Tooltip("虚拟（软件内）速度 / 实际速度 的比例。大于 1 时软件中小车跑得更快；遥测显示与下发的速度仍为实际值。")]
        [SerializeField] private float virtualSpeedRatio = 10f;

        [Header("自动巡航挡位")]
        [Tooltip("自动巡航挡位 1~5，占空比依次 0.3 / 0.4 / 0.5 / 0.6 / 0.7。")]
        [Range(1, 5)]
        [SerializeField] private int cruiseGear = 3;

        [Header("移动模式")]
        [Tooltip("使用 Rigidbody 物理驱动；关闭则用 Transform 直接移动（更稳定）。")]
        [SerializeField] private bool usePhysics = false;
        [Tooltip("锁定初始高度，避免小车掉落。")]
        [SerializeField] private bool keepHeight = true;

        [Header("车轮动画")]
        [SerializeField] private bool rotateWheels = true;
        [Tooltip("每前进 1 cm 车轮旋转的角度（度/cm），按车轮半径调整。直径 48mm 约 23.9。")]
        [SerializeField] private float wheelSpinSpeed = 24f;
        [SerializeField] private Vector3 wheelAxis = Vector3.right;
        [Tooltip("自动查找名字含这些关键字的子物体作为车轮。")]
        [SerializeField] private string[] wheelKeywords = { "wheel", "tire", "tyre", "轮" };
        [Tooltip("手动指定车轮；非空时优先使用，忽略关键字自动查找。")]
        [SerializeField] private List<Transform> manualWheels = new List<Transform>();

        private Rigidbody rb;
        private float currentSpeed;   // cm/s（场景单位）
        private float initialHeight;
        private float totalDistance;  // cm
        private bool autoCruise;
        private float diagTimer;
        private readonly List<Transform> wheels = new List<Transform>();

        public float Speed => Mathf.Abs(currentSpeed);  // cm/s
        /// <summary>油门：-1（后退）~ 1（前进）。</summary>
        public float Throttle { get; private set; }
        /// <summary>当前轮电机占空比（0~1）。</summary>
        public float DutyCycle { get; private set; }
        /// <summary>手动模式速度上限（cm/s）。</summary>
        public float MaxSpeed => manualMaxSpeed;
        /// <summary>占空比 → 速度换算系数（cm/s 每占空比）。</summary>
        public float DutySpeedFactor
        {
            get => dutySpeedFactor;
            set => dutySpeedFactor = Mathf.Max(0.01f, value);
        }
        /// <summary>虚拟速度 / 实际速度 的比例。</summary>
        public float VirtualSpeedRatio
        {
            get => virtualSpeedRatio;
            set => virtualSpeedRatio = Mathf.Max(0.1f, value);
        }
        /// <summary>自动巡航挡位（1~5）。</summary>
        public int CruiseGear
        {
            get => cruiseGear;
            set => cruiseGear = Mathf.Clamp(value, 1, 5);
        }
        public float TotalDistance => totalDistance;
        public bool IsAutoCruise => autoCruise;
        /// <summary>车轮 Transform 列表（只读），供外部获取车轮位置（如绘制轮胎痕迹）。</summary>
        public IReadOnlyList<Transform> Wheels => wheels;

        /// <summary>开启自动巡航时，小车自动以前进油门行驶。</summary>
        public void SetAutoCruise(bool enabled)
        {
            autoCruise = enabled;
        }

        /// <summary>设定自动巡航挡位（1~5）。</summary>
        public void SetCruiseGear(int gear)
        {
            cruiseGear = Mathf.Clamp(gear, 1, 5);
        }

        /// <summary>挡位 → 占空比（1 挡 0.3，2 挡 0.4，…，5 挡 0.7）。</summary>
        public static float GearToDuty(int gear)
        {
            return 0.2f + Mathf.Clamp(gear, 1, 5) * 0.1f;
        }

        private void Awake()
        {
            rb = GetComponent<Rigidbody>();
            initialHeight = transform.position.y;

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

            // 当前占空比：自动巡航 = 挡位占空比；手动 = 油门 × 上限占空比
            DutyCycle = autoCruise
                ? GearToDuty(cruiseGear)
                : Mathf.Abs(Throttle) * (manualMaxSpeed / dutySpeedFactor);

            if (!usePhysics)
            {
                MoveTransform(Time.deltaTime);
            }

            RotateWheels(Time.deltaTime);

            // 诊断日志：每 2 秒输出一次，用于定位「车轮转但小车不动」
            diagTimer += Time.deltaTime;
            if (diagTimer >= 2f)
            {
                diagTimer = 0f;
                Debug.Log($"[CarController] 模式={(usePhysics ? "物理" : "Transform")} 油门={Throttle:0.00} " +
                          $"实际速度={currentSpeed:0.00} 上限={SpeedLimit:0.00} 比例={virtualSpeedRatio} " +
                          $"位置={transform.position} 前进方向={GetMoveDirection()}");
            }
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

            // 位置按虚拟速度移动（实际速度 × 比例），让软件中小车跑得更快
            float virtualSpeed = currentSpeed * virtualSpeedRatio;
            transform.position += GetMoveDirection() * virtualSpeed * dt;
            totalDistance += Mathf.Abs(virtualSpeed) * dt;

            if (keepHeight)
            {
                Vector3 p = transform.position;
                p.y = initialHeight;
                transform.position = p;
            }
        }

        private void MovePhysics()
        {
            Vector3 dir = GetMoveDirection();

            float targetSpeed = Throttle * SpeedLimit;
            currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, acceleration * Time.fixedDeltaTime);

            float virtualSpeed = currentSpeed * virtualSpeedRatio;
            Vector3 velocity = dir * virtualSpeed;
            rb.velocity = new Vector3(velocity.x, rb.velocity.y, velocity.z);
            totalDistance += Mathf.Abs(virtualSpeed) * Time.fixedDeltaTime;
        }

        /// <summary>
        /// 取水平前进方向：在 forward / right 中选水平分量最大的轴。
        /// 避免模型轴向不正（forward 朝上/朝下）时水平投影为零导致小车完全不动。
        /// </summary>
        private Vector3 GetMoveDirection()
        {
            Vector3 f = transform.forward;
            f.y = 0f;
            Vector3 r = transform.right;
            r.y = 0f;

            Vector3 dir = f.sqrMagnitude >= r.sqrMagnitude ? f : r;
            return dir.sqrMagnitude < 1e-4f ? Vector3.forward : dir.normalized;
        }

        /// <summary>当前模式下的速度上限（cm/s）：自动巡航用挡位速度，手动用手动上限。</summary>
        private float SpeedLimit => autoCruise
            ? GearToDuty(cruiseGear) * dutySpeedFactor
            : manualMaxSpeed;

        private void RotateWheels(float dt)
        {
            if (!rotateWheels || wheels.Count == 0)
            {
                return;
            }

            // currentSpeed 为实际 cm/s，车轮转速需匹配虚拟移动速度
            float angle = currentSpeed * virtualSpeedRatio * wheelSpinSpeed * dt;
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
