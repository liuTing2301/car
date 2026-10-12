using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin.Scripting
{
    /// <summary>
    /// 脚本运行器：把用户脚本「注入」后，每个 tick 解释执行一遍，
    /// 脚本可读写本地变量（作用于软件），并可读取接收到的只读数据。
    /// </summary>
    public class ScriptRunner : MonoBehaviour
    {
        [Header("安全限制")]
        [Tooltip("单次执行的步数上限，防止死循环卡死。")]
        [SerializeField] private int stepLimit = 200000;

        // 引用
        private CarController car;
        private Transform carTransform;
        private CameraRotor cameraRotor;
        private ModeController modeController;
        private RobotDataReader robotDataReader;
        private PointCloudRebuilder pointCloudRebuilder;
        private DynamicPipeModel dynamicPipeModel;

        private ScriptProgram program;
        private ScriptContext ctx;
        private readonly Dictionary<string, object> state = new Dictionary<string, object>();

        /// <summary>是否有脚本处于注入（运行）状态。</summary>
        public bool IsActive => program != null;
        /// <summary>当前注入的脚本源码。</summary>
        public string Source { get; private set; } = "";
        /// <summary>最近一次错误信息（无错误为 null）。</summary>
        public string LastError { get; private set; }
        /// <summary>最近一次 print 输出。</summary>
        public string LastOutput { get; private set; } = "";
        /// <summary>是否已配置好引用。</summary>
        public bool Ready => ctx != null;

        public void Setup(CarController car, Transform carTransform, CameraRotor cameraRotor,
            RobotDataReader robotDataReader, PointCloudRebuilder pointCloudRebuilder, DynamicPipeModel dynamicPipeModel)
        {
            this.car = car;
            this.carTransform = carTransform;
            this.cameraRotor = cameraRotor;
            this.robotDataReader = robotDataReader;
            this.pointCloudRebuilder = pointCloudRebuilder;
            this.dynamicPipeModel = dynamicPipeModel;
            BuildContext();
        }

        /// <summary>模式控制器由 UI 在运行时创建，随后注入。</summary>
        public void SetModeController(ModeController mc) => modeController = mc;

        // ============================= 对外操作 =============================

        /// <summary>注入脚本：编译成功则开始每 tick 执行。返回 false 表示编译失败（见 LastError）。</summary>
        public bool Inject(string source)
        {
            LastError = null;
            LastOutput = "";
            try
            {
                ScriptProgram prog = ScriptEngine.Compile(source);
                program = prog;
                Source = source ?? "";
                state.Clear();
                ctx?.Log.Clear();
                return true;
            }
            catch (ScriptException e)
            {
                LastError = e.Message;
                program = null;
                return false;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                program = null;
                return false;
            }
        }

        /// <summary>清除脚本并复位脚本可写变量到初始状态。</summary>
        public void Clear()
        {
            program = null;
            Source = "";
            state.Clear();
            LastError = null;
            LastOutput = "";
            ctx?.Log.Clear();
            ResetOutputs();
        }

        private void ResetOutputs()
        {
            car?.SetScriptThrottle(null);
            modeController?.SetMode(ModeController.Mode.Manual);
            if (cameraRotor != null)
            {
                cameraRotor.SetAutoRotate(false);
                cameraRotor.SetAngle(0f);
            }
            if (dynamicPipeModel != null)
            {
                dynamicPipeModel.gameObject.SetActive(false);
            }
            pointCloudRebuilder?.SetLive(true);
        }

        private void Update()
        {
            if (program == null || ctx == null) return;

            // 每 tick 重新求值：脚本未写 throttle 时交还手动控制
            car?.SetScriptThrottle(null);

            try
            {
                ctx.Log.Clear();
                ScriptEngine.Execute(program, ctx, state, stepLimit);
                LastError = null;
                LastOutput = ctx.Log.Count > 0 ? string.Join("\n", ctx.Log) : "";
            }
            catch (ScriptException e)
            {
                LastError = e.Message;
                LastOutput = e.Message;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                LastOutput = e.Message;
            }
        }

        // ============================= 变量绑定 =============================

        private void BuildContext()
        {
            ctx = new ScriptContext();

            // ---- 可读写「本地变量」----
            ctx.Bind("drive_mode",
                () => (modeController != null && modeController.IsAutoCruise) ? "auto" : "manual",
                v => modeController?.SetMode(V.Str(v) == "auto" ? ModeController.Mode.AutoCruise : ModeController.Mode.Manual));

            ctx.Bind("speed",
                () => (double)(car != null ? car.Speed : 0f),
                v =>
                {
                    if (car == null) return;
                    float limit = Mathf.Max(0.01f, car.SpeedLimitValue);
                    car.SetScriptThrottle(Mathf.Clamp((float)V.Num(v, 0) / limit, -1f, 1f));
                });

            ctx.Bind("throttle",
                () => (double)(car != null ? car.Throttle : 0f),
                v => car?.SetScriptThrottle(Mathf.Clamp((float)V.Num(v, 0), -1f, 1f)));

            ctx.Bind("cruise_gear",
                () => (double)(car != null ? car.CruiseGear : 0),
                v => car?.SetCruiseGear((int)V.Num(v, 0)));

            ctx.Bind("camera_speed",
                () => (double)(cameraRotor != null ? cameraRotor.AutoRotateSpeed : 0f),
                v => { if (cameraRotor != null) cameraRotor.AutoRotateSpeed = Mathf.Max(0f, (float)V.Num(v, 0)); });

            ctx.Bind("camera_manual_speed",
                () => (double)(cameraRotor != null ? cameraRotor.ManualRotateSpeed : 0f),
                v => { if (cameraRotor != null) cameraRotor.ManualRotateSpeed = Mathf.Max(0f, (float)V.Num(v, 0)); });

            ctx.Bind("camera_auto_rotate",
                () => cameraRotor != null && cameraRotor.IsAutoRotating,
                v => cameraRotor?.SetAutoRotate(V.Bool(v)));

            ctx.Bind("camera_angle",
                () => (double)(cameraRotor != null ? cameraRotor.CurrentAngle : 0f),
                v => cameraRotor?.SetAngle((float)V.Num(v, 0)));

            ctx.Bind("pipe_model",
                () => dynamicPipeModel != null && dynamicPipeModel.gameObject.activeSelf,
                v => { if (dynamicPipeModel != null) dynamicPipeModel.gameObject.SetActive(V.Bool(v)); });

            ctx.Bind("pointcloud_rebuild",
                () => pointCloudRebuilder != null && pointCloudRebuilder.LiveEnabled,
                v => pointCloudRebuilder?.SetLive(V.Bool(v)));

            // ---- 只读「接收数据」----
            ctx.Bind("is_connected", () => robotDataReader != null && robotDataReader.IsConnected, null);
            ctx.Bind("car_speed", () => (double)(car != null ? car.Speed : 0f), null);
            ctx.Bind("distance", () => (double)(car != null ? car.TotalDistance : 0f), null);
            ctx.Bind("speed_limit", () => (double)(car != null ? car.SpeedLimitValue : 0f), null);
            ctx.Bind("pos_x", () => (double)(carTransform != null ? carTransform.position.x : 0f), null);
            ctx.Bind("pos_y", () => (double)(carTransform != null ? carTransform.position.y : 0f), null);
            ctx.Bind("pos_z", () => (double)(carTransform != null ? carTransform.position.z : 0f), null);
            ctx.Bind("heading", () => (double)(carTransform != null ? carTransform.eulerAngles.y : 0f), null);
            ctx.Bind("gimbal_angle", () => (double)(cameraRotor != null ? cameraRotor.CurrentAngle : 0f), null);

            ctx.Bind("detection_count", () => (double)(robotDataReader != null && robotDataReader.LatestDetections != null ? robotDataReader.LatestDetections.Count : 0), null);
            ctx.Bind("radius", () => PipeValue(p => p.radius), null);
            ctx.Bind("deviation", () => PipeValue(p => p.deviation), null);
            ctx.Bind("ellipticity", () => PipeValue(p => p.ellipticity), null);
            ctx.Bind("confidence", () => PipeValue(p => p.confidence), null);

            ctx.Bind("section_count", () => (double)(pointCloudRebuilder != null ? pointCloudRebuilder.SectionCount : 0), null);
            ctx.Bind("avg_radius", () => (double)(pointCloudRebuilder != null ? pointCloudRebuilder.AverageRadius : 0f), null);

            ctx.Bind("time", () => (double)Time.time, null);
            ctx.Bind("delta_time", () => (double)Time.deltaTime, null);
            ctx.Bind("frame", () => (double)Time.frameCount, null);

            // ---- 常量 ----
            ctx.BindConst("PI", Math.PI);
            ctx.BindConst("E", Math.E);
        }

        private double PipeValue(Func<PipeData, double> pick)
        {
            PipeData p = robotDataReader != null ? robotDataReader.LatestPipe : null;
            return p != null ? pick(p) : 0d;
        }
    }
}
