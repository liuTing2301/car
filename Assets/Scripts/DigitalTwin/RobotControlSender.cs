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
        [Tooltip("线速度(m/s) → 轮电机转速(RPM) 的换算系数。轮半径约 5cm 时约 191。")]
        [SerializeField] private float rpmPerMeterPerSecond = 191f;

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

            // 轮电机：由小车线速度换算转速，方向由油门决定
            if (car != null)
            {
                pkt.speed = car.Speed;
                pkt.throttle = car.Throttle;
                pkt.wheel_motor_speed = car.Speed * rpmPerMeterPerSecond;
                pkt.direction = car.Throttle > 0.01f ? 1 : (car.Throttle < -0.01f ? -1 : 0);
                pkt.drive_mode = car.IsAutoCruise ? "auto" : "manual";
            }

            // 摄像头电机：手动按 E 键用手动转速，自动巡航环扫用自动转速，否则停转
            if (cameraRotor != null)
            {
                if (Input.GetKey(KeyCode.E))
                {
                    pkt.camera_motor_speed = cameraRotor.ManualRotateSpeed;
                }
                else if (cameraRotor.IsAutoRotating)
                {
                    // 自动巡航：下发带方向的速度（正转/反转往复）
                    pkt.camera_motor_speed = cameraRotor.CurrentAutoRotateSpeed;
                }
                else
                {
                    pkt.camera_motor_speed = 0;
                }
            }

            robotDataReader.SendControl(pkt);
        }
    }
}
