using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCSystem
{
    [Serializable]
    public class PlayerAuthRegisterRequest
    {
        public string username;
        public string password;
    }

    [Serializable]
    public class PlayerAuthRegisterResponse
    {
        public string playerId;
        public string username;
        public string createdAtUtc;
    }

    [Serializable]
    public class PlayerAuthLoginRequest
    {
        public string username;
        public string password;
        public bool rememberMe;
        public string deviceId;
    }

    [Serializable]
    public class PlayerAuthSessionResponse
    {
        public string sessionId;
        public string playerId;
        public string username;
        public string sessionToken;
        public string createdAtUtc;
        public string expiresAtUtc;
        public string lastSeenAtUtc;
    }

    [Serializable]
    class PlayerAuthEmptyResponse
    {
    }

    [Serializable]
    class PlayerAuthErrorResponse
    {
        public string error;
    }

    static class PlayerSessionStore
    {
        const string RelativePath = "NPCDialogue/player-auth-session.json";

        public static PlayerAuthSessionResponse Load()
        {
            string fullPath = GetFullPath();
            if (!File.Exists(fullPath))
                return null;

            try
            {
                string json = File.ReadAllText(fullPath);
                PlayerAuthSessionResponse session = JsonUtility.FromJson<PlayerAuthSessionResponse>(json);
                if (session == null || string.IsNullOrWhiteSpace(session.sessionToken) || IsExpired(session.expiresAtUtc))
                {
                    Clear();
                    return null;
                }

                return session;
            }
            catch
            {
                Clear();
                return null;
            }
        }

        public static void Save(PlayerAuthSessionResponse session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.sessionToken))
            {
                Clear();
                return;
            }

            string fullPath = GetFullPath();
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, JsonUtility.ToJson(session, true));
        }

        public static void Clear()
        {
            string fullPath = GetFullPath();
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        public static bool IsExpired(string expiresAtUtc)
        {
            if (string.IsNullOrWhiteSpace(expiresAtUtc))
                return false;

            return DateTime.TryParse(expiresAtUtc, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out DateTime expiresAt)
                && expiresAt <= DateTime.UtcNow;
        }

        static string GetFullPath()
        {
            return Path.Combine(Application.persistentDataPath, RelativePath).Replace('\\', '/');
        }
    }

    [DefaultExecutionOrder(-350)]
    [DisallowMultipleComponent]
    public class PlayerAuthService : MonoBehaviour
    {
        [Title("Player Auth Service")]
        [HelpBox("Client-side gateway for login/register/session restore against the backend auth service. This component should only own HTTP auth/session concerns; multiplayer spawn and NGO ownership stay in AuthNetworkBridge and NetworkManager.", MessageMode.Log, drawAbove: true)]
        [Header("Service")]
        [SerializeField] string serviceBaseUrl = "http://localhost:5100";
        [SerializeField] float requestTimeoutSeconds = 15f;
        [SerializeField] bool validateStoredSessionOnStart = true;

        [SerializeField, ReadOnly] string lastAuthStatus = "Idle";
        [SerializeField, ReadOnly] string lastAuthRoute = string.Empty;
        [SerializeField, ReadOnly] long lastAuthDurationMs;

        bool _initialized;

        public PlayerAuthSessionResponse CurrentSession { get; private set; }
        public bool IsAuthenticated => CurrentSession != null
            && !string.IsNullOrWhiteSpace(CurrentSession.sessionToken)
            && !PlayerSessionStore.IsExpired(CurrentSession.expiresAtUtc);
        public string ServiceBaseUrl => serviceBaseUrl?.Trim() ?? string.Empty;

        [ShowInInspector]
        string ServiceBaseUrlPreview => serviceBaseUrl?.Trim() ?? string.Empty;

        [ShowInInspector]
        string AuthSessionPreview => CurrentSession == null ? "<none>" : $"{CurrentSession.username} ({CurrentSession.sessionId})";

        [ShowInInspector]
        string BackendHealthPreview => $"{BuildUrl("api/auth/login")} | timeout={Mathf.Max(1, Mathf.CeilToInt(requestTimeoutSeconds))}s";

        [Button("Validate Auth Service Settings")]
        void ValidateAuthServiceSettings()
        {
            bool validUrl = !string.IsNullOrWhiteSpace(serviceBaseUrl)
                && (serviceBaseUrl.StartsWith("http://") || serviceBaseUrl.StartsWith("https://"));
            bool validTimeout = requestTimeoutSeconds > 0f;
            lastAuthStatus = validUrl && validTimeout
                ? $"Auth service settings look valid: {BackendHealthPreview}"
                : "Auth service settings are incomplete. Check serviceBaseUrl and requestTimeoutSeconds.";

            NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ConfigurationValidation,
                validUrl && validTimeout ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                validUrl && validTimeout ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                lastAuthStatus,
                source: nameof(PlayerAuthService),
                data: new System.Collections.Generic.Dictionary<string, object>
                {
                    ["serviceBaseUrl"] = serviceBaseUrl ?? string.Empty,
                    ["requestTimeoutSeconds"] = requestTimeoutSeconds,
                    ["validateStoredSessionOnStart"] = validateStoredSessionOnStart
                });
        }

        public async Task<PlayerAuthSessionResponse> InitializeAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(serviceBaseUrl) && (serviceBaseUrl.Contains("localhost") || serviceBaseUrl.Contains("127.0.0.1")))
            {
                try
                {
                    Uri pageUri = new Uri(Application.absoluteURL);
                    if (pageUri.Host != "localhost" && pageUri.Host != "127.0.0.1")
                    {
                        UriBuilder authUriBuilder = new UriBuilder(serviceBaseUrl);
                        authUriBuilder.Host = pageUri.Host;
                        serviceBaseUrl = authUriBuilder.ToString().TrimEnd('/');
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PlayerAuthService] Failed to dynamically resolve serviceBaseUrl: {ex.Message}");
                }
            }
#endif

            if (_initialized)
                return IsAuthenticated ? CurrentSession : null;

            using var scope = NPCFlowScope.Start(NPCFlowLogger.FindOrCreate(), NPCFlowStage.AuthSession, nameof(PlayerAuthService),
                data: new System.Collections.Generic.Dictionary<string, object>
                {
                    ["validateStoredSessionOnStart"] = validateStoredSessionOnStart,
                    ["serviceBaseUrl"] = serviceBaseUrl ?? string.Empty
                });

            CurrentSession = PlayerSessionStore.Load();
            _initialized = true;

            if (CurrentSession == null)
            {
                lastAuthStatus = "No stored auth session found.";
                scope.Skipped(lastAuthStatus);
                return null;
            }

            if (!validateStoredSessionOnStart)
            {
                lastAuthStatus = IsAuthenticated ? $"Stored auth session loaded for {CurrentSession.username}." : "Stored auth session is not valid.";
                scope.Success(lastAuthStatus, BuildSessionData(CurrentSession));
                return IsAuthenticated ? CurrentSession : null;
            }

            PlayerAuthSessionResponse restored = await TryRestoreStoredSessionAsync();
            if (restored == null)
            {
                scope.Warning("Stored auth session could not be restored.");
            }
            else
            {
                scope.Success($"Stored auth session restored for {restored.username}.", BuildSessionData(restored));
            }

            return restored;
        }

        public async Task<PlayerAuthRegisterResponse> RegisterAsync(string username, string password)
        {
            var request = new PlayerAuthRegisterRequest
            {
                username = username?.Trim() ?? string.Empty,
                password = password ?? string.Empty
            };

            return await SendRequestAsync<PlayerAuthRegisterResponse>("api/auth/register", UnityWebRequest.kHttpVerbPOST, JsonUtility.ToJson(request));
        }

        public async Task<PlayerAuthSessionResponse> LoginAsync(string username, string password, bool rememberMe)
        {
            var request = new PlayerAuthLoginRequest
            {
                username = username?.Trim() ?? string.Empty,
                password = password ?? string.Empty,
                rememberMe = rememberMe,
                deviceId = GetDeviceId()
            };

            PlayerAuthSessionResponse session = await SendRequestAsync<PlayerAuthSessionResponse>("api/auth/login", UnityWebRequest.kHttpVerbPOST, JsonUtility.ToJson(request));
            if (session == null || string.IsNullOrWhiteSpace(session.sessionToken))
                throw new InvalidOperationException("Auth server returned an invalid session.");

            CurrentSession = session;
            if (rememberMe)
                PlayerSessionStore.Save(session);
            else
                PlayerSessionStore.Clear();

            return session;
        }

        public async Task<PlayerAuthSessionResponse> TryRestoreStoredSessionAsync()
        {
            if (CurrentSession == null || string.IsNullOrWhiteSpace(CurrentSession.sessionToken))
                return null;

            if (PlayerSessionStore.IsExpired(CurrentSession.expiresAtUtc))
            {
                ClearLocalSession();
                return null;
            }

            string sessionToken = CurrentSession.sessionToken;

            try
            {
                PlayerAuthSessionResponse restored = await SendRequestAsync<PlayerAuthSessionResponse>("api/auth/session", UnityWebRequest.kHttpVerbGET, null, sessionToken);
                if (restored == null)
                {
                    ClearLocalSession();
                    return null;
                }

                restored.sessionToken = sessionToken;
                CurrentSession = restored;
                PlayerSessionStore.Save(restored);
                return restored;
            }
            catch
            {
                ClearLocalSession();
                return null;
            }
        }

        public async Task LogoutAsync()
        {
            if (CurrentSession == null || string.IsNullOrWhiteSpace(CurrentSession.sessionToken))
            {
                ClearLocalSession();
                return;
            }

            using var scope = NPCFlowScope.Start(NPCFlowLogger.FindOrCreate(), NPCFlowStage.AuthSession, nameof(PlayerAuthService),
                data: BuildSessionData(CurrentSession));
            try
            {
                await SendRequestAsync<PlayerAuthEmptyResponse>("api/auth/logout", UnityWebRequest.kHttpVerbPOST, "{}", CurrentSession.sessionToken);
                lastAuthStatus = $"Logged out auth session for {CurrentSession.username}.";
                scope.Success(lastAuthStatus);
            }
            finally
            {
                ClearLocalSession();
            }
        }

        async Task<TResponse> SendRequestAsync<TResponse>(string route, string method, string jsonBody = null, string bearerToken = null)
        {
            string url = BuildUrl(route);
            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            int timeoutSeconds = Mathf.Max(1, Mathf.CeilToInt(requestTimeoutSeconds));
            lastAuthRoute = route?.Trim() ?? string.Empty;
            var data = new System.Collections.Generic.Dictionary<string, object>
            {
                ["route"] = route ?? string.Empty,
                ["method"] = method ?? string.Empty,
                ["url"] = url,
                ["timeoutSeconds"] = timeoutSeconds,
                ["hasJsonBody"] = !string.IsNullOrWhiteSpace(jsonBody),
                ["hasBearerToken"] = !string.IsNullOrWhiteSpace(bearerToken)
            };
            using var scope = NPCFlowScope.Start(logger, NPCFlowStage.AuthRequest, nameof(PlayerAuthService), data: data);
            using var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = timeoutSeconds;
            request.SetRequestHeader("Accept", "application/json");

            if (!string.IsNullOrWhiteSpace(jsonBody))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
            }

            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
            {
                string error = ParseErrorMessage(request.downloadHandler.text, request.error);
                lastAuthDurationMs = scope.ElapsedMilliseconds;
                lastAuthStatus = $"Auth request failed: {route} -> {error}";
                scope.Error(new InvalidOperationException(error), lastAuthStatus, new System.Collections.Generic.Dictionary<string, object>
                {
                    ["responseText"] = request.downloadHandler.text ?? string.Empty
                });
                throw new InvalidOperationException(error);
            }

            string responseText = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(responseText))
            {
                lastAuthDurationMs = scope.ElapsedMilliseconds;
                lastAuthStatus = $"Auth request succeeded with empty response: {route}";
                scope.Success(lastAuthStatus);
                return default;
            }

            TResponse response = JsonUtility.FromJson<TResponse>(responseText);
            if (response == null)
            {
                string error = "Auth server returned an unreadable response.";
                lastAuthDurationMs = scope.ElapsedMilliseconds;
                lastAuthStatus = $"{route} failed: {error}";
                scope.Error(new InvalidOperationException(error), lastAuthStatus);
                throw new InvalidOperationException(error);
            }

            lastAuthDurationMs = scope.ElapsedMilliseconds;
            lastAuthStatus = $"Auth request succeeded: {route}";
            scope.Success(lastAuthStatus);

            return response;
        }

        void ClearLocalSession()
        {
            CurrentSession = null;
            PlayerSessionStore.Clear();
        }

        string BuildUrl(string route)
        {
            return $"{serviceBaseUrl.TrimEnd('/')}/{route.TrimStart('/')}";
        }

        static string ParseErrorMessage(string responseText, string fallback)
        {
            if (!string.IsNullOrWhiteSpace(responseText))
            {
                PlayerAuthErrorResponse error = JsonUtility.FromJson<PlayerAuthErrorResponse>(responseText);
                if (error != null && !string.IsNullOrWhiteSpace(error.error))
                    return error.error;
            }

            return string.IsNullOrWhiteSpace(fallback) ? "Auth request failed." : fallback;
        }

        static string GetDeviceId()
        {
            return string.IsNullOrWhiteSpace(SystemInfo.deviceUniqueIdentifier)
                ? SystemInfo.deviceName
                : SystemInfo.deviceUniqueIdentifier;
        }

        static System.Collections.Generic.Dictionary<string, object> BuildSessionData(PlayerAuthSessionResponse session)
        {
            return new System.Collections.Generic.Dictionary<string, object>
            {
                ["sessionId"] = session?.sessionId ?? string.Empty,
                ["playerId"] = session?.playerId ?? string.Empty,
                ["username"] = session?.username ?? string.Empty,
                ["expiresAtUtc"] = session?.expiresAtUtc ?? string.Empty
            };
        }
    }
}
