using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NPCSystem
{
    [DefaultExecutionOrder(-300)]
    public class AuthUIController : MonoBehaviour
    {
        [Header("Auth UI References")]
        [SerializeField]
        GameObject authPanel;

        [SerializeField]
        TMP_Text authTitle;

        [SerializeField]
        TMP_InputField usernameInput;

        [SerializeField]
        TMP_InputField passwordInput;

        [SerializeField]
        GameObject confirmPasswordGroup;

        [SerializeField]
        TMP_InputField confirmPasswordInput;

        [SerializeField]
        Toggle rememberToggle;

        [SerializeField]
        TMP_Text rememberLabel;

        [SerializeField]
        Button submitButton;

        [SerializeField]
        TMP_Text submitButtonText;

        [SerializeField]
        Button switchModeButton;

        [SerializeField]
        TMP_Text switchModeText;

        [SerializeField]
        TMP_Text errorText;

        [SerializeField]
        PlayerAuthService authService;

        [SerializeField]
        Canvas authCanvas;

        [SerializeField]
        GraphicRaycaster authRaycaster;

        [SerializeField]
        Canvas gameplayCanvas;

        [SerializeField]
        GraphicRaycaster gameplayRaycaster;

        [SerializeField]
        int authCanvasSortingOrder = 100;

        [Header("Validation")]
        [SerializeField]
        int minUsernameLength = 3;

        [SerializeField]
        int minPasswordLength = 6;

        [Header("Events")]
        public AuthEvents events = new AuthEvents();

        bool _initialized;
        bool _isRegisterMode;
        bool _eventsBound;
        bool _referencesResolved;

        [System.Serializable]
        public class AuthEvents
        {
            public AuthStringEvent onLoginSuccess = new AuthStringEvent();
            public AuthStringEvent onRegisterSuccess = new AuthStringEvent();
            public AuthStringEvent onError = new AuthStringEvent();
        }

        [System.Serializable]
        public class AuthStringEvent : UnityEngine.Events.UnityEvent<string> { }

        public bool IsRegisterMode => _isRegisterMode;
        public bool IsInitialized => _initialized;

        void Awake()
        {
            ResolveReferences();
            ConfigureInputs();
            NormalizeVisualLayout();
            ApplyMode(_isRegisterMode);
            HideError();
            ApplyCanvasFocus(isAuthVisible: true);
        }

        void OnEnable()
        {
            ResolveReferences();
            BindRuntimeEvents();
            ApplyCanvasFocus(isAuthVisible: true);
        }

        async void Start()
        {
            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Debug,
                    "AuthUIController starting.",
                    source: nameof(AuthUIController)
                );

            ResolveReferences();
            ConfigureInputs();
            NormalizeVisualLayout();
            ApplyMode(_isRegisterMode);
            HideError();

            await InitializeAsync();

            _initialized = true;
            SetInputEnabled(true);

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Debug,
                    "AuthUIController ready.",
                    source: nameof(AuthUIController)
                );
        }

        void OnValidate()
        {
            ResolveReferences(force: true);
            ConfigureInputs();
            NormalizeVisualLayout();
            ApplyMode(_isRegisterMode);
        }

        void OnDestroy()
        {
            UnbindRuntimeEvents();
        }

        void OnDisable()
        {
            UnbindRuntimeEvents();
            ApplyCanvasFocus(isAuthVisible: false);
        }

        async Task InitializeAsync()
        {
            if (authService == null)
                return;

            PlayerAuthSessionResponse restoredSession = await authService.InitializeAsync();
            if (restoredSession != null)
            {
                events.onLoginSuccess?.Invoke(restoredSession.username);
            }
        }

        void ResolveReferences()
        {
            ResolveReferences(force: false);
        }

        void ResolveReferences(bool force)
        {
            if (_referencesResolved && !force)
                return;

            if (authPanel == null)
            {
                authPanel = FindObject("AuthPanel");
                if (authPanel == null)
                {
                    authPanel = FindObject("Canvas/AuthPanel");
                }
            }
            Transform root = authPanel != null ? authPanel.transform : null;

            authTitle =
                authTitle != null
                    ? authTitle
                    : FindChildComponent<TMP_Text>(root, "AuthContent/AuthTitle");
            usernameInput =
                usernameInput != null
                    ? usernameInput
                    : FindChildComponent<TMP_InputField>(
                        root,
                        "AuthContent/UsernameGroup/UsernameInput"
                    );
            passwordInput =
                passwordInput != null
                    ? passwordInput
                    : FindChildComponent<TMP_InputField>(
                        root,
                        "AuthContent/PasswordGroup/PasswordInput"
                    );
            confirmPasswordGroup =
                confirmPasswordGroup != null
                    ? confirmPasswordGroup
                    : FindChild(root, "AuthContent/ConfirmPasswordGroup");
            confirmPasswordInput =
                confirmPasswordInput != null
                    ? confirmPasswordInput
                    : FindChildComponent<TMP_InputField>(
                        root,
                        "AuthContent/ConfirmPasswordGroup/ConfirmPasswordInput"
                    );
            rememberToggle =
                rememberToggle != null
                    ? rememberToggle
                    : FindChildComponent<Toggle>(root, "AuthContent/RememberToggle");
            rememberLabel =
                rememberLabel != null
                    ? rememberLabel
                    : FindChildComponent<TMP_Text>(
                        root,
                        "AuthContent/RememberToggle/RememberLabel"
                    );
            submitButton =
                submitButton != null
                    ? submitButton
                    : FindChildComponent<Button>(root, "AuthContent/SubmitButton");
            submitButtonText =
                submitButtonText != null ? submitButtonText : FindButtonText(submitButton);
            switchModeButton =
                switchModeButton != null
                    ? switchModeButton
                    : FindChildComponent<Button>(root, "AuthContent/SwitchModeButton");
            switchModeText =
                switchModeText != null ? switchModeText : FindButtonText(switchModeButton);
            errorText =
                errorText != null
                    ? errorText
                    : FindChildComponent<TMP_Text>(root, "AuthContent/ErrorText");
            authService = authService != null ? authService : GetComponent<PlayerAuthService>();
            authService =
                authService != null
                    ? authService
                    : FindAnyObjectByType<PlayerAuthService>(FindObjectsInactive.Include);
            authCanvas =
                authCanvas != null ? authCanvas
                : authPanel != null ? authPanel.GetComponent<Canvas>()
                : null;
            authRaycaster =
                authRaycaster != null ? authRaycaster
                : authPanel != null ? authPanel.GetComponent<GraphicRaycaster>()
                : null;
            gameplayCanvas =
                gameplayCanvas != null ? gameplayCanvas : FindGameplayCanvas(authPanel);
            gameplayRaycaster =
                gameplayRaycaster != null ? gameplayRaycaster
                : gameplayCanvas != null ? gameplayCanvas.GetComponent<GraphicRaycaster>()
                : null;
        }

        void BindRuntimeEvents()
        {
            if (_eventsBound)
                return;

            if (submitButton != null)
                submitButton.onClick.AddListener(HandleSubmitPressed);
            if (switchModeButton != null)
                switchModeButton.onClick.AddListener(ToggleMode);
            if (usernameInput != null)
                usernameInput.onValueChanged.AddListener(HandleFieldChanged);
            if (passwordInput != null)
                passwordInput.onValueChanged.AddListener(HandleFieldChanged);
            if (confirmPasswordInput != null)
                confirmPasswordInput.onValueChanged.AddListener(HandleFieldChanged);
            if (rememberToggle != null)
                rememberToggle.onValueChanged.AddListener(HandleToggleChanged);

            _eventsBound = true;
        }

        void UnbindRuntimeEvents()
        {
            if (!_eventsBound)
                return;

            if (submitButton != null)
                submitButton.onClick.RemoveListener(HandleSubmitPressed);
            if (switchModeButton != null)
                switchModeButton.onClick.RemoveListener(ToggleMode);
            if (usernameInput != null)
                usernameInput.onValueChanged.RemoveListener(HandleFieldChanged);
            if (passwordInput != null)
                passwordInput.onValueChanged.RemoveListener(HandleFieldChanged);
            if (confirmPasswordInput != null)
                confirmPasswordInput.onValueChanged.RemoveListener(HandleFieldChanged);
            if (rememberToggle != null)
                rememberToggle.onValueChanged.RemoveListener(HandleToggleChanged);

            _eventsBound = false;
        }

        void ConfigureInputs()
        {
            if (passwordInput != null)
                passwordInput.contentType = TMP_InputField.ContentType.Password;
            if (confirmPasswordInput != null)
                confirmPasswordInput.contentType = TMP_InputField.ContentType.Password;
        }

        void NormalizeVisualLayout()
        {
            NormalizeTextRect(
                submitButtonText,
                new Vector2(320f, 40f),
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                TextAlignmentOptions.Center
            );
            NormalizeTextRect(
                switchModeText,
                new Vector2(320f, 36f),
                Vector2.zero,
                new Vector2(0.5f, 0.5f),
                TextAlignmentOptions.Center
            );
            NormalizeTextRect(
                rememberLabel,
                new Vector2(150f, 24f),
                new Vector2(96f, 0f),
                new Vector2(0f, 0.5f),
                TextAlignmentOptions.Left
            );

            if (authPanel != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(
                    authPanel.GetComponent<RectTransform>()
                );
            }
        }

        void ApplyMode(bool registerMode)
        {
            _isRegisterMode = registerMode;

            if (authTitle != null)
                authTitle.text = registerMode ? "Create Account" : "Welcome Back";
            if (submitButtonText != null)
                submitButtonText.text = registerMode ? "Register" : "Login";
            if (switchModeText != null)
                switchModeText.text = registerMode ? "Switch to Login" : "Switch to Register";
            if (confirmPasswordGroup != null)
                confirmPasswordGroup.SetActive(registerMode);
            if (rememberToggle != null)
                rememberToggle.gameObject.SetActive(!registerMode);
        }

        void ApplyCanvasFocus(bool isAuthVisible)
        {
            if (authCanvas != null)
            {
                authCanvas.overrideSorting = true;
                authCanvas.sortingOrder = authCanvasSortingOrder;
                authCanvas.enabled = true;
            }

            if (authRaycaster != null)
            {
                authRaycaster.enabled = isAuthVisible;
            }

            if (gameplayRaycaster != null)
            {
                gameplayRaycaster.enabled = !isAuthVisible;
            }
        }

        public void ToggleMode()
        {
            HideError();
            ApplyMode(!_isRegisterMode);
        }

        public void HandleFieldChanged(string _)
        {
            HideError();
        }

        public void HandleToggleChanged(bool _)
        {
            HideError();
        }

        public void HandleSubmitPressed()
        {
            _ = SubmitAsync();
        }

        async Task SubmitAsync()
        {
            if (!_initialized)
                return;

            string username = usernameInput != null ? usernameInput.text.Trim() : string.Empty;
            string password = passwordInput != null ? passwordInput.text : string.Empty;
            string confirmPassword =
                confirmPasswordInput != null ? confirmPasswordInput.text : string.Empty;

            HideError();
            SetInputEnabled(false);

            try
            {
                string error = ValidateInput(username, password, confirmPassword);
                if (!string.IsNullOrEmpty(error))
                {
                    ShowError(error);
                    return;
                }

                if (_isRegisterMode)
                    await HandleRegisterAsync(username, password);
                else
                    await HandleLoginAsync(
                        username,
                        password,
                        rememberToggle != null && rememberToggle.isOn
                    );
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
            }
            finally
            {
                SetInputEnabled(true);
            }
        }

        void HideError()
        {
            if (errorText == null)
                return;

            errorText.text = string.Empty;
            errorText.gameObject.SetActive(false);
        }

        void ShowError(string message)
        {
            if (errorText != null)
            {
                errorText.text = message;
                errorText.gameObject.SetActive(true);
            }
            events.onError?.Invoke(message);
        }

        string ValidateInput(string username, string password, string confirmPassword)
        {
            if (string.IsNullOrWhiteSpace(username))
                return "Username is required.";
            if (username.Length < minUsernameLength)
                return $"Username must be at least {minUsernameLength} characters.";
            if (string.IsNullOrEmpty(password))
                return "Password is required.";
            if (password.Length < minPasswordLength)
                return $"Password must be at least {minPasswordLength} characters.";
            if (_isRegisterMode && password != confirmPassword)
                return "Passwords do not match.";
            return null;
        }

        async Task HandleLoginAsync(string username, string password, bool rememberMe)
        {
            if (authService == null)
                throw new InvalidOperationException("Player auth service is not configured.");

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.UIInput,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Info,
                    "Login attempt.",
                    source: nameof(AuthUIController),
                    data: new Dictionary<string, object>
                    {
                        ["username"] = username,
                        ["rememberMe"] = rememberMe,
                    }
                );

            PlayerAuthSessionResponse session = await authService.LoginAsync(
                username,
                password,
                rememberMe
            );

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.UIInput,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "Login success.",
                    source: nameof(AuthUIController),
                    data: new Dictionary<string, object>
                    {
                        ["username"] = session.username,
                        ["rememberMe"] = rememberMe,
                        ["sessionId"] = session.sessionId,
                        ["expiresAtUtc"] = session.expiresAtUtc,
                    }
                );

            events.onLoginSuccess?.Invoke(session.username);
        }

        async Task HandleRegisterAsync(string username, string password)
        {
            if (authService == null)
                throw new InvalidOperationException("Player auth service is not configured.");

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.UIInput,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Info,
                    "Register attempt.",
                    source: nameof(AuthUIController),
                    data: new Dictionary<string, object> { ["username"] = username }
                );

            PlayerAuthRegisterResponse registration = await authService.RegisterAsync(
                username,
                password
            );

            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.UIInput,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "Register success.",
                    source: nameof(AuthUIController),
                    data: new Dictionary<string, object>
                    {
                        ["username"] = registration.username,
                        ["playerId"] = registration.playerId,
                    }
                );

            events.onRegisterSuccess?.Invoke(registration.username);
        }

        void SetInputEnabled(bool enabled)
        {
            if (usernameInput != null)
                usernameInput.interactable = enabled;
            if (passwordInput != null)
                passwordInput.interactable = enabled;
            if (confirmPasswordInput != null)
                confirmPasswordInput.interactable = enabled;
            if (rememberToggle != null)
                rememberToggle.interactable = enabled && !_isRegisterMode;
            if (submitButton != null)
                submitButton.interactable = enabled;
            if (switchModeButton != null)
                switchModeButton.interactable = enabled;
        }

        public void ClosePanel()
        {
            ApplyCanvasFocus(isAuthVisible: false);
            if (authPanel != null)
            {
                authPanel.SetActive(false);
            }
            else
            {
                gameObject.SetActive(false);
            }
        }

        static void NormalizeTextRect(
            TMP_Text label,
            Vector2 size,
            Vector2 anchoredPosition,
            Vector2 anchor,
            TextAlignmentOptions alignment
        )
        {
            if (label == null)
                return;

            if (label.transform is not RectTransform rect)
                return;

            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;

            label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.overflowMode = TextOverflowModes.Overflow;
            label.raycastTarget = false;
        }

        static TMP_Text FindButtonText(Button button)
        {
            return button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
        }

        static T FindChildComponent<T>(Transform root, string relativePath)
            where T : Component
        {
            GameObject child = FindChild(root, relativePath);
            return child != null ? child.GetComponent<T>() : null;
        }

        static GameObject FindChild(Transform root, string relativePath)
        {
            if (root == null)
                return null;

            Transform child = root.Find(relativePath);
            return child != null ? child.gameObject : null;
        }

        static GameObject FindObject(string path)
        {
            return GameObject.Find(path);
        }

        static Canvas FindGameplayCanvas(GameObject authPanelObject)
        {
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Include);
            foreach (Canvas canvas in canvases)
            {
                if (canvas == null)
                    continue;

                if (authPanelObject != null && canvas.gameObject == authPanelObject)
                    continue;

                if (canvas.gameObject.name == "Canvas")
                    return canvas;
            }

            return null;
        }
    }
}
