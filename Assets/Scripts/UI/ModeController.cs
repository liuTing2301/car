using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace DigitalTwin
{
    /// <summary>
    /// 模式选择：手动模式（W/S 控制）与自动巡航（自动前进）。
    /// 提供巡航行驶速度、摄像头云台旋转速度的精细设定（+/- 微调 + 直接输入数值）。
    /// </summary>
    public class ModeController : MonoBehaviour
    {
        public enum Mode { Manual, AutoCruise }

        [SerializeField] private CarController car;
        [SerializeField] private CameraRotor cameraRotor;

        private const int GearMin = 1;
        private const int GearMax = 5;
        private const float RotorStep = 1f;
        private const float RotorMin = 0f;
        private const float RotorMax = 360f;

        private Mode currentMode = Mode.Manual;
        private Button manualBtn;
        private Button autoBtn;
        private Text statusText;
        private InputField gearInput;
        private InputField rotorSpeedInput;

        public Mode Current => currentMode;
        public bool IsAutoCruise => currentMode == Mode.AutoCruise;

        public void Setup(CarController car, CameraRotor cameraRotor)
        {
            this.car = car;
            this.cameraRotor = cameraRotor;
        }

        public void Build(RectTransform parent)
        {
            // 标题
            Text title = UIFactory.CreateText(parent, "ModeTitle", "驾驶模式", 16, UIFactory.AccentCyan,
                TextAnchor.MiddleLeft, FontStyle.Bold);
            SetRect(title, new Vector2(0, -10), new Vector2(0, 24), true);

            // 手动模式按钮
            manualBtn = UIFactory.CreateButton(parent, "ManualBtn", "手动模式", 15);
            SetRect(manualBtn, new Vector2(0, -40), new Vector2(0, 36), true);

            // 自动巡航按钮
            autoBtn = UIFactory.CreateButton(parent, "AutoBtn", "自动巡航", 15);
            SetRect(autoBtn, new Vector2(0, -80), new Vector2(0, 36), true);

            manualBtn.onClick.AddListener(() => SetMode(Mode.Manual));
            autoBtn.onClick.AddListener(() => SetMode(Mode.AutoCruise));

            // 状态文字（两行）
            statusText = UIFactory.CreateText(parent, "ModeStatus", "当前：手动模式", 13,
                UIFactory.TextDim, TextAnchor.UpperLeft);
            SetRect(statusText, new Vector2(0, -122), new Vector2(0, 36), true);

            // 巡航挡位设定行（含输入框，1~5 挡）
            gearInput = CreateInputRow(parent, "巡航挡位", -166,
                () => AdjustGear(-1),
                () => AdjustGear(1),
                OnGearInput);

            // 云台转速设定行（含输入框）
            rotorSpeedInput = CreateInputRow(parent, "云台转速", -204,
                () => AdjustRotorSpeed(-RotorStep),
                () => AdjustRotorSpeed(RotorStep),
                OnRotorSpeedInput);

            RefreshValues();
            SetMode(Mode.Manual);
        }

        /// <summary>创建一行 [标签] [-] [输入框] [+]。</summary>
        private InputField CreateInputRow(RectTransform parent, string label, float y,
            UnityAction onMinus, UnityAction onPlus, UnityAction<string> onEnd)
        {
            Text lab = UIFactory.CreateText(parent, label + "_Label", label, 13, UIFactory.TextDim, TextAnchor.MiddleLeft);
            SetRect(lab, new Vector2(0, y), new Vector2(78, 24), false);

            Button minus = UIFactory.CreateButton(parent, label + "_Minus", "-", 14);
            SetRect(minus, new Vector2(82, y), new Vector2(26, 24), false);
            minus.onClick.AddListener(onMinus);

            InputField input = UIFactory.CreateInputField(parent, label + "_Input", "", 13,
                InputField.ContentType.DecimalNumber);
            SetRect(input, new Vector2(112, y), new Vector2(126, 24), false);
            input.onEndEdit.AddListener(onEnd);

            Button plus = UIFactory.CreateButton(parent, label + "_Plus", "+", 14);
            SetRect(plus, new Vector2(242, y), new Vector2(26, 24), false);
            plus.onClick.AddListener(onPlus);

            return input;
        }

        private void OnGearInput(string text)
        {
            if (car == null)
            {
                return;
            }
            if (int.TryParse(text, out int v))
            {
                car.SetCruiseGear(Mathf.Clamp(v, GearMin, GearMax));
            }
            RefreshValues();
        }

        private void OnRotorSpeedInput(string text)
        {
            if (cameraRotor == null)
            {
                return;
            }
            if (float.TryParse(text, out float v))
            {
                cameraRotor.AutoRotateSpeed = Mathf.Clamp(v, RotorMin, RotorMax);
            }
            RefreshValues();
        }

        private void SetRect(Component comp, Vector2 pos, Vector2 size, bool fullWidth)
        {
            RectTransform rt = comp.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0, 1);
            if (fullWidth)
            {
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(1, 1);
            }
            else
            {
                rt.anchorMin = new Vector2(0, 1);
                rt.anchorMax = new Vector2(0, 1);
            }
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        public void SetMode(Mode mode)
        {
            currentMode = mode;
            UpdateButtons();

            if (car != null)
            {
                car.SetAutoCruise(mode == Mode.AutoCruise);
            }
            if (cameraRotor != null)
            {
                cameraRotor.SetAutoRotate(mode == Mode.AutoCruise);
            }

            RefreshValues();
        }

        private void AdjustGear(int delta)
        {
            if (car == null) return;
            int v = Mathf.Clamp(car.CruiseGear + delta, GearMin, GearMax);
            car.SetCruiseGear(v);
            RefreshValues();
        }

        private void AdjustRotorSpeed(float delta)
        {
            if (cameraRotor == null) return;
            float v = Mathf.Clamp(cameraRotor.AutoRotateSpeed + delta, RotorMin, RotorMax);
            cameraRotor.AutoRotateSpeed = v;
            RefreshValues();
        }

        private void RefreshValues()
        {
            if (gearInput != null && car != null)
            {
                gearInput.text = $"{car.CruiseGear}";
            }
            if (rotorSpeedInput != null && cameraRotor != null)
            {
                rotorSpeedInput.text = $"{cameraRotor.AutoRotateSpeed:0}";
            }
            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            if (statusText == null)
            {
                return;
            }
            if (currentMode == Mode.AutoCruise && car != null)
            {
                int g = car.CruiseGear;
                float duty = CarController.GearToDuty(g);
                float speed = duty * car.DutySpeedFactor;
                statusText.text = $"自动巡航 · {g} 挡（占空比 {duty:0.0}）\n速度 {speed:0.0} cm/s";
                statusText.color = UIFactory.AccentGreen;
            }
            else
            {
                float max = car != null ? car.MaxSpeed : 0f;
                statusText.text = $"手动模式（上限 {max:0} cm/s）\nW/S 控制前后";
                statusText.color = UIFactory.TextDim;
            }
        }

        private void UpdateButtons()
        {
            if (manualBtn != null)
            {
                Color m = currentMode == Mode.Manual ? UIFactory.AccentCyan : new Color(0.05f, 0.12f, 0.16f, 0.9f);
                manualBtn.GetComponent<Image>().color = m;
                Text mt = manualBtn.GetComponentInChildren<Text>();
                if (mt != null) mt.color = currentMode == Mode.Manual ? Color.black : UIFactory.TextPrimary;
            }
            if (autoBtn != null)
            {
                Color a = currentMode == Mode.AutoCruise ? UIFactory.AccentGreen : new Color(0.05f, 0.12f, 0.16f, 0.9f);
                autoBtn.GetComponent<Image>().color = a;
                Text at = autoBtn.GetComponentInChildren<Text>();
                if (at != null) at.color = currentMode == Mode.AutoCruise ? Color.black : UIFactory.TextPrimary;
            }
        }
    }
}
