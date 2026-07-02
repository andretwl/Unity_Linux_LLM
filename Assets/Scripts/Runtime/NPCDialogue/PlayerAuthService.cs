using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
        [Header("Service")]
        [SerializeField] string serviceBaseUrl = "http://localhost:5100";
        [SerializeField] float requestTimeoutSeconds = 15f;
        [SerializeField] bool validateStoredSessionOnStart = true;

        bool _initialized;

        public PlayerAuthSessionResponse CurrentSession { get; private set; }
        public bool IsAuthenticated => CurrentSession != null
            && !string.IsNullOrWhiteSpace(CurrentSession.sessionToken)
            && !PlayerSessionStore.IsExpired(CurrentSession.expiresAtUtc);

        public async Task<PlayerAuthSessionResponse> InitializeAsync()
        {
            if (_initialized)
                return IsAuthenticated ? CurrentSession : null;

            CurrentSession = PlayerSessionStore.Load();
            _initialized = true;

            if (CurrentSession == null)
                return null;

            if (!validateStoredSessionOnStart)
                return IsAuthenticated ? CurrentSession : null;

            return await TryRestoreStoredSessionAsync();
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

            try
            {
                await SendRequestAsync<PlayerAuthEmptyResponse>("api/auth/logout", UnityWebRequest.kHttpVerbPOST, "{}", CurrentSession.sessionToken);
            }
            finally
            {
                ClearLocalSession();
            }
        }

        async Task<TResponse> SendRequestAsync<TResponse>(string route, string method, string jsonBody = null, string bearerToken = null)
        {
            string url = BuildUrl(route);
            using var request = new UnityWebRequest(url, method);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(requestTimeoutSeconds));
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
                throw new InvalidOperationException(ParseErrorMessage(request.downloadHandler.text, request.error));
            }

            string responseText = request.downloadHandler.text;
            if (string.IsNullOrWhiteSpace(responseText))
                return default;

            TResponse response = JsonUtility.FromJson<TResponse>(responseText);
            if (response == null)
                throw new InvalidOperationException("Auth server returned an unreadable response.");

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
    }
}
