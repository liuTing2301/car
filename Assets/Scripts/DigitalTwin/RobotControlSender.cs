using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 控制指令发送器：定期从小车（轮电机）与摄像头云台（摄像头电机）读取状态，
    /// 组装成 ControlPacket 后通过 RobotDataReader 发送给树莓派小车。
    /// </summary>
    public class RobotControlSender : MonoBehaviour
    {
        [Header("引用")]
        [SerializeField] private RobotDataReader robotDataReader;
        [SerializeField] private CarController car;
        [SerializeField] private CameraRotor cameraRotor;

        [Header("发送")]
        [Tooltip("发送间隔（秒），默认 0.05s = 20Hz。")]
        [SerializeField] private float interval = 0.05f;

        [Tooltip("摄像头电机最大转速（度/秒），对应占空比 1。下发的 camera_motor_speed 为 转速/该值。")]
        [SerializeField] private float cameraMotorMaxSpeed = 120f;

        private float timer;

        public void Setup(RobotDataReader reader, CarController car, CameraRotor cameraRotor)
        {
            this.robotDataReader = reader;
            this.car = car;
            this.cameraRotor = cameraRotor;
        }

        private void Update()
        {
            timer += Time.deltaTime;
            if (timer < interval)
            {
                return;
            }
            timer = 0f;
            SendControl();
        }

        /// <summary>立即发送一帧控制指令。</summary>
        public void SendControl()
        {
            if (robotDataReader == null || !robotDataReader.IsConnected)
            {
                return;
            }

            ControlPacket pkt = new ControlPacket();

            // 轮电机：发送占空比（0~1），方向由油门决定
            if (car != null)
            {
                pkt.speed = car.Speed;      // cm/s
                pkt.throttle = car.Throttle;
                pkt.wheel_motor_duty = car.DutyCycle;
                pkt.direction = car.Throttle > 0.01f ? 1 : (car.Throttle < -0.01f ? -1 : 0);
                pkt.drive_mode = car.IsAutoCruise ? "auto" : "manual";
            }

            // 摄像头电机：手动按 Q（正转）/ E（反转）用手动转速，自动巡航环扫用自动转速，否则停转。
            // 下发的是占空比（转速 / 最大转速），正负号表示转向，范围 -1 ~ 1；120°/s 对应占空比 1。
            if (cameraRotor != null)
            {
                float speed = 0f;
                if (cameraRotor.IsAutoRotating)
                {
                    // 自动巡航：下发带方向的速度（正转/反转往复），此时忽略手动输入
                    speed = cameraRotor.CurrentAutoRotateSpeed;
                }
                else if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.E))
                {
                    // 手动旋转：Q 正转 / E 反转（带正负号）
                    speed = cameraRotor.CurrentManualRotateSpeed;
                }

                pkt.camera_motor_speed = Mathf.Clamp(speed / cameraMotorMaxSpeed, -1f, 1f);
            }

            robotDataReader.SendControl(pkt);
        }
    }
}