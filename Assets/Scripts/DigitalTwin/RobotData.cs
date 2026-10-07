using System;
using System.Collections.Generic;

namespace DigitalTwin
{
    /// <summary>
    /// 管道数据：小车通过 WebSocket 下发的最新检测参数。
    /// 字段名与小车 JSON 的 snake_case 键一一对应（JsonUtility 直接按字段名匹配）。
    /// </summary>
    [Serializable]
    public class PipeData
    {
        /// <summary>偏差。</summary>
        public double deviation;

        /// <summary>椭圆度。</summary>
        public double ellipticity;

        /// <summary>置信度。</summary>
        public double confidence;

        /// <summary>点云点数。</summary>
        public int point_num;

        /// <summary>半径。</summary>
        public double radius;
    }

    /// <summary>单个检测目标（目标检测结果，兼容 Maix 摄像头识别结果）。</summary>
    [Serializable]
    public class Detection
    {
        /// <summary>类别 ID。</summary>
        public int class_id;

        /// <summary>检测到的类别名。</summary>
        public string class_name = "";

        public int x;
        public int y;
        public int w;
        public int h;

        /// <summary>置信度得分。</summary>
        public double score;
    }

    /// <summary>Maix 摄像头识别结果数组的包装类（JsonUtility 无法直接解析顶层数组）。</summary>
    [Serializable]
    public class DetectionList
    {
        public List<Detection> items = new List<Detection>();
    }

    /// <summary>
    /// 小车下发的一帧状态包。
    /// 示例：{"type":"status","pipe_data":{...},"detections":[...],"image_b64":"..."}
    /// </summary>
    [Serializable]
    public class StatusPacket
    {
        public string type = "";
        public PipeData pipe_data;
        public List<Detection> detections = new List<Detection>();
        public string image_b64;

        /// <summary>激光雷达扫描点云（角度 + 距离）。</summary>
        public ScanData scan;
    }

    /// <summary>激光雷达单帧扫描数据。</summary>
    [Serializable]
    public class ScanData
    {
        /// <summary>角度（弧度，范围约 -π ~ +π）。</summary>
        public List<float> angles = new List<float>();
        /// <summary>距离（米），NaN 用 null 表示（反序列化为 0，解析时忽略）。</summary>
        public List<float> distances = new List<float>();
    }

    /// <summary>
    /// Unity 发送给小车（树莓派）的控制指令。
    /// 示例：{"type":"control","wheel_motor_speed":120.0,"camera_motor_speed":30.0,
    ///        "drive_mode":"manual","direction":1,"throttle":0.8,"speed":1.5}
    /// </summary>
    [Serializable]
    public class ControlPacket
    {
        public string type = "control";

        /// <summary>轮电机转速（RPM，转/分钟）。</summary>
        public double wheel_motor_speed;

        /// <summary>摄像头电机转速（度/秒）。</summary>
        public double camera_motor_speed;

        /// <summary>驾驶模式：manual（手动）/ auto（自动巡航）。</summary>
        public string drive_mode = "manual";

        /// <summary>方向：1 前进，-1 后退，0 停止。</summary>
        public int direction;

        /// <summary>油门：-1 ~ 1。</summary>
        public double throttle;

        /// <summary>线速度（m/s，正值）。</summary>
        public double speed;
    }
}
