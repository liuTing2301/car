using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    /// <summary>
    /// 登录界面：全屏布局。登录时隐藏主界面，把 3D 主相机画面做两遍高斯模糊后铺满全屏作为背景。
    /// 账户 admin / 密码 000000。
    /// </summary>
    public class LoginScreen : MonoBehaviour
    {
        private const string ValidAccount = "admin";
        private const string ValidPassword = "000000";

        /// <summary>登录界面是否正在显示（其它输入 / UI 逻辑据此暂停）。</summary>
        public static bool IsOpen { get; private set; }

        private GameObject mainCanvasGO;
        private Camera sceneCamera;
        private RenderTexture cameraOriginalTarget;
        private System.Action onLoggedIn;

        // 模糊链路：主相机 → camRT →（横向）→ blurTmp →（纵向）→ blurOut
        private RenderTexture camRT;
        private RenderTexture blurTmp;
        private RenderTexture blurOut;
        private Material blurMaterial;
        private RawImage background;
        private Camera displayCamera;

        private InputField accountInput;
        private InputField passwordInput;
        private Text errorText;

        /// <summary>创建并显示登录界面；登录成功后恢复主界面并回调。</summary>
        public static LoginScreen Create(GameObject mainCanvas, Camera sceneCamera, System.Action onLoggedIn)
        {
            GameObject go = new GameObject("LoginScreen");
            LoginScreen screen = go.AddComponent<LoginScreen>();
            screen.mainCanvasGO = mainCanvas;
            screen.sceneCamera = sceneCamera;
            screen.onLoggedIn = onLoggedIn;
            screen.Build();
            return screen;
        }

        private void Build()
        {
            IsOpen = true;
            Time.timeScale = 0f;

            // 隐藏主界面
            if (mainCanvasGO != null)
            {
                mainCanvasGO.SetActive(false);
            }

            BuildBlurTextures();

            // 主相机改为渲染到 RT 后，屏幕上就没有相机输出了（Game 视图会提示 No cameras rendering）；
            // 这里补一个只清屏的相机，保证 Display 1 有相机在渲染。
            GameObject displayGO = new GameObject("LoginDisplayCamera");
            displayGO.transform.SetParent(transform, false);
            displayCamera = displayGO.AddComponent<Camera>();
            displayCamera.clearFlags = CameraClearFlags.SolidColor;
            displayCamera.backgroundColor = new Color(0.03f, 0.04f, 0.06f, 1f);
            displayCamera.cullingMask = 0;
            displayCamera.depth = -100f;
            displayCamera.targetTexture = null;
            displayCamera.allowHDR = false;
            displayCamera.allowMSAA = false;

            // ===== 全屏登录画布 =====
            GameObject canvasGO = new GameObject("LoginCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, false);

            Canvas canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;

            CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            // 背景：高斯模糊后的 3D 画面（铺满全屏）
            GameObject bgGO = new GameObject("BlurBackground", typeof(RectTransform), typeof(RawImage));
            bgGO.transform.SetParent(canvasGO.transform, false);
            background = bgGO.GetComponent<RawImage>();
            background.texture = (blurMaterial != null && blurOut != null) ? blurOut : camRT;
            background.color = background.texture != null ? Color.white : UIFactory.BgRoot;
            background.raycastTarget = true; // 挡住底下的鼠标操作
            Fill(bgGO);

            // 左侧压暗，右侧表单区更暗一点，保证文字清晰
            GameObject tintGO = new GameObject("Tint", typeof(RectTransform), typeof(Image));
            tintGO.transform.SetParent(canvasGO.transform, false);
            Image tint = tintGO.GetComponent<Image>();
            tint.color = new Color(0.03f, 0.04f, 0.06f, 0.52f);
            tint.raycastTarget = false;
            Fill(tintGO);

            // ===== 左侧：项目标题区 =====
            GameObject brandGO = new GameObject("Brand", typeof(RectTransform));
            brandGO.transform.SetParent(canvasGO.transform, false);
            RectTransform brand = brandGO.GetComponent<RectTransform>();
            brand.anchorMin = new Vector2(0.08f, 0.5f);
            brand.anchorMax = new Vector2(0.56f, 0.5f);
            brand.pivot = new Vector2(0.5f, 0.5f);
            brand.anchoredPosition = Vector2.zero;
            brand.sizeDelta = new Vector2(0f, 240f);

            GameObject accentGO = new GameObject("AccentBar", typeof(RectTransform), typeof(Image));
            accentGO.transform.SetParent(brand, false);
            Image accentImg = accentGO.GetComponent<Image>();
            accentImg.color = UIFactory.AccentCyan;
            accentImg.raycastTarget = false;
            RectTransform accentRT = accentGO.GetComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 0.5f);
            accentRT.anchorMax = new Vector2(0f, 0.5f);
            accentRT.pivot = new Vector2(0f, 0.5f);
            accentRT.anchoredPosition = new Vector2(0f, 16f);
            accentRT.sizeDelta = new Vector2(4f, 120f);

            Text title = UIFactory.CreateText(brand, "Title", "管道缺陷检测机器人", 44,
                UIFactory.TextPrimary, TextAnchor.LowerLeft, FontStyle.Bold);
            RectTransform titleRT = title.GetComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 0.5f);
            titleRT.anchorMax = new Vector2(1f, 0.5f);
            titleRT.pivot = new Vector2(0f, 0f);
            titleRT.anchoredPosition = new Vector2(26f, 8f);
            titleRT.sizeDelta = new Vector2(0f, 64f);

            Text subtitle = UIFactory.CreateText(brand, "Subtitle", "数字孪生系统", 18,
                UIFactory.TextDim, TextAnchor.UpperLeft);
            RectTransform subtitleRT = subtitle.GetComponent<RectTransform>();
            subtitleRT.anchorMin = new Vector2(0f, 0.5f);
            subtitleRT.anchorMax = new Vector2(1f, 0.5f);
            subtitleRT.pivot = new Vector2(0f, 1f);
            subtitleRT.anchoredPosition = new Vector2(26f, -6f);
            subtitleRT.sizeDelta = new Vector2(0f, 28f);

            // ===== 右侧：登录表单 =====
            GameObject formGO = new GameObject("Form", typeof(RectTransform));
            formGO.transform.SetParent(canvasGO.transform, false);
            RectTransform form = formGO.GetComponent<RectTransform>();
            form.anchorMin = new Vector2(0.67f, 0.5f);
            form.anchorMax = new Vector2(0.67f, 0.5f);
            form.pivot = new Vector2(0.5f, 0.5f);
            form.anchoredPosition = Vector2.zero;
            form.sizeDelta = new Vector2(420f, 360f);

            Text accLabel = UIFactory.CreateText(form, "AccountLabel", "账户", 14, UIFactory.TextDim, TextAnchor.MiddleLeft);
            Row(accLabel, 30f, 22f);

            accountInput = UIFactory.CreateInputField(form, "AccountInput", "", 16);
            Row(accountInput, 54f, 52f);
            SetPlaceholder(accountInput, "请输入账户");

            Text pwdLabel = UIFactory.CreateText(form, "PasswordLabel", "密码", 14, UIFactory.TextDim, TextAnchor.MiddleLeft);
            Row(pwdLabel, 122f, 22f);

            passwordInput = UIFactory.CreateInputField(form, "PasswordInput", "", 16, InputField.ContentType.Password);
            Row(passwordInput, 146f, 52f);
            SetPlaceholder(passwordInput, "请输入密码");

            errorText = UIFactory.CreateText(form, "Error", "", 13, UIFactory.Danger, TextAnchor.MiddleLeft);
            Row(errorText, 206f, 22f);
            errorText.gameObject.SetActive(false);

            Button loginBtn = UIFactory.CreateButton(form, "LoginBtn", "登 录", 18);
            Row(loginBtn, 238f, 54f);

            ColorBlock cb = loginBtn.colors;
            cb.normalColor = UIFactory.AccentBlue;
            cb.highlightedColor = Color.Lerp(UIFactory.AccentBlue, Color.white, 0.22f);
            cb.pressedColor = Color.Lerp(UIFactory.AccentBlue, Color.black, 0.22f);
            cb.selectedColor = UIFactory.AccentBlue;
            loginBtn.colors = cb;

            Text loginLabel = loginBtn.GetComponentInChildren<Text>();
            if (loginLabel != null)
            {
                loginLabel.color = Color.white;
                loginLabel.fontStyle = FontStyle.Bold;
            }
            loginBtn.onClick.AddListener(OnLoginClicked);

            if (accountInput != null)
            {
                accountInput.ActivateInputField();
            }
        }

        /// <summary>创建模糊用的渲染纹理与材质，并把主相机的输出接到 camRT。</summary>
        private void BuildBlurTextures()
        {
            if (sceneCamera == null)
            {
                return;
            }

            int w = Mathf.Max(64, Screen.width);
            int h = Mathf.Max(36, Screen.height);

            camRT = CreateRT(w, h, 16);
            blurTmp = CreateRT(w, h, 0);
            blurOut = CreateRT(w, h, 0);

            Shader shader = Shader.Find("DigitalTwin/UIGaussianBlur");
            if (shader != null)
            {
                blurMaterial = new Material(shader);
            }

            cameraOriginalTarget = sceneCamera.targetTexture;
            sceneCamera.targetTexture = camRT;
        }

        private static RenderTexture CreateRT(int w, int h, int depth)
        {
            RenderTexture rt = new RenderTexture(w, h, depth);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            rt.Create();
            return rt;
        }

        private void Update()
        {
            // 回车直接登录
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
            {
                OnLoginClicked();
                return;
            }

            // 横、竖各一遍高斯模糊（真正模糊，而不是靠降分辨率）
            if (blurMaterial != null && camRT != null && blurTmp != null && blurOut != null)
            {
                blurMaterial.SetFloat("_BlurSize", 12f);

                blurMaterial.SetVector("_Direction", new Vector4(1f, 0f, 0f, 0f));
                Graphics.Blit(camRT, blurTmp, blurMaterial);

                blurMaterial.SetVector("_Direction", new Vector4(0f, 1f, 0f, 0f));
                Graphics.Blit(blurTmp, blurOut, blurMaterial);
            }
        }

        private void OnLoginClicked()
        {
            string account = accountInput != null ? accountInput.text.Trim() : "";
            string password = passwordInput != null ? passwordInput.text : "";

            if (account == ValidAccount && password == ValidPassword)
            {
                Close(true);
                return;
            }

            if (errorText != null)
            {
                errorText.text = "账户或密码错误";
                errorText.gameObject.SetActive(true);
            }
        }

        private void Close(bool success)
        {
            IsOpen = false;
            Time.timeScale = 1f;

            if (sceneCamera != null)
            {
                sceneCamera.targetTexture = cameraOriginalTarget;
            }

            if (displayCamera != null)
            {
                Destroy(displayCamera.gameObject);
                displayCamera = null;
            }

            ReleaseRT(ref blurOut);
            ReleaseRT(ref blurTmp);
            ReleaseRT(ref camRT);

            if (blurMaterial != null)
            {
                Destroy(blurMaterial);
                blurMaterial = null;
            }

            if (mainCanvasGO != null)
            {
                mainCanvasGO.SetActive(true);
            }

            if (success)
            {
                onLoggedIn?.Invoke();
            }

            Destroy(gameObject);
        }

        private static void ReleaseRT(ref RenderTexture rt)
        {
            if (rt == null)
            {
                return;
            }
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        private static void Fill(GameObject go)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>把控件按「距表单顶部 top、全宽、指定高度」摆放。</summary>
        private static void Row(Component comp, float top, float height)
        {
            RectTransform rt = comp.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -top);
            rt.sizeDelta = new Vector2(0f, height);
        }

        private static void SetPlaceholder(InputField input, string text)
        {
            if (input != null && input.placeholder is Text ph)
            {
                ph.text = text;
            }
        }
    }
}
