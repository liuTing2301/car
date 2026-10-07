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

        private const float SpeedStep = 0.1f;
        private const float SpeedMin = 0.1f;
        private const float SpeedMax = 20f;
        private const float RotorStep = 1f;
        private const float RotorMin = 0f;
        private const float RotorMax = 360f;

        private Mode currentMode = Mode.Manual;
        private Button manualBtn;
        private Button autoBtn;
        private Text statusText;
        private InputField cruiseSpeedInput;
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
            SetRect(title, new Vector2(0, -8), new Vector2(0, 22), true);

            // 手动模式按钮
            manualBtn = UIFactory.CreateButton(parent, "ManualBtn", "手动模式", 15);
            SetRect(manualBtn, new Vector2(0, -36), new Vector2(0, 34), true);

            // 自动巡航按钮
            autoBtn = UIFactory.CreateButton(parent, "AutoBtn", "自动巡航", 15);
            SetRect(autoBtn, new Vector2(0, -74), new Vector2(0, 34), true);

            manualBtn.onClick.AddListener(() => SetMode(Mode.Manual));
            autoBtn.onClick.AddListener(() => SetMode(Mode.AutoCruise));

            // 状态文字
            statusText = UIFactory.CreateText(parent, "ModeStatus", "当前：手动模式", 13,
                UIFactory.TextDim, TextAnchor.MiddleLeft);
            SetRect(statusText, new Vector2(0, -116), new Vector2(0, 18), true);

            // 巡航速度设定行（含输入框）
            cruiseSpeedInput = CreateInputRow(parent, "巡航速度", -146,
                () => AdjustCruiseSpeed(-SpeedStep),
                () => AdjustCruiseSpeed(SpeedStep),
                OnCruiseSpeedInput);

            // 云台转速设定行（含输入框）
            rotorSpeedInput = CreateInputRow(parent, "云台转速", -182,
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
            SetRect(minus, new Vector2(82, y), new Vector2(24, 24), false);
            minus.onClick.AddListener(onMinus);

            InputField input = UIFactory.CreateInputField(parent, label + "_Input", "", 13,
                InputField.ContentType.DecimalNumber);
            SetRect(input, new Vector2(110, y), new Vector2(60, 24), false);
            input.onEndEdit.AddListener(onEnd);

            Button plus = UIFactory.CreateButton(parent, label + "_Plus", "+", 14);
            SetRect(plus, new Vector2(174, y), new Vector2(24, 24), false);
            plus.onClick.AddListener(onPlus);

            return input;
        }

        private void OnCruiseSpeedInput(string text)
        {
            if (car == null)
            {
                return;
            }
            if (float.TryParse(text, out float v))
            {
                car.SetCruiseSpeed(Mathf.Clamp(v, SpeedMin, SpeedMax));
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

            if (statusText != null)
            {
                statusText.text = mode == Mode.Manual ? "当前：手动模式" : "当前：自动巡航";
                statusText.color = mode == Mode.Manual ? UIFactory.TextDim : UIFactory.AccentGreen;
            }
        }

        private void AdjustCruiseSpeed(float delta)
        {
            if (car == null) return;
            float v = Mathf.Clamp(car.CruiseSpeed + delta, SpeedMin, SpeedMax);
            car.SetCruiseSpeed(v);
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
            if (cruiseSpeedInput != null && car != null)
            {
                cruiseSpeedInput.text = $"{car.CruiseSpeed:0.0}";
            }
            if (rotorSpeedInput != null && cameraRotor != null)
            {
                rotorSpeedInput.text = $"{cameraRotor.AutoRotateSpeed:0}";
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
