using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCSystem
{
    [Serializable]
    public sealed class NPCBackendProbeResult
    {
        public string backendName;
        public string url;
        public bool reachable;
        public long responseCode;
        public long durationMs;
        public string status;
    }

    [Serializable]
    public sealed class NPCBackendReadinessSnapshot
    {
        public NPCBackendProbeResult auth = new NPCBackendProbeResult();
        public NPCBackendProbeResult localAi = new NPCBackendProbeResult();

        public bool AllRequiredBackendsReachable(bool requireAuth, bool requireLocalAi)
        {
            return (!requireAuth || auth.reachable) && (!requireLocalAi || localAi.reachable);
        }
    }

    [DisallowMultipleComponent]
    public sealed class NPCBackendReadinessService : MonoBehaviour
    {
        [Title("NPC Backend Readiness")]
        [HelpBox("Verifies the auth backend and LocalAI endpoint before multiplayer dialogue traffic starts. The auth route is considered reachable even on expected 401/404 responses, as long as Unity can complete the HTTP exchange.", MessageMode.Log, drawAbove: true)]
        [Header("References")]
        public PlayerAuthService authService;
        public NPCDialogueManager dialogueManager;

        [Header("Probe Targets")]
        [SerializeField] string authProbeRelativePath = "api/auth/session";
        [SerializeField] string localAiProbeRelativePath = "v1/models";
        [SerializeField] float requestTimeoutSeconds = 5f;
        [SerializeField] bool requireAuthBackend = true;
        [SerializeField] bool requireLocalAiBackend = true;
        [SerializeField] bool failInitializationOnRequiredBackendFailure = true;

        [Header("Runtime Diagnostics")]
        [SerializeField, ReadOnly] string lastReadinessStatus = "Not checked.";
        [SerializeField, ReadOnly] string lastAuthBackendStatus = "Idle";
        [SerializeField, ReadOnly] string lastLocalAiBackendStatus = "Idle";
        [SerializeField, ReadOnly] long lastProbeDurationMs;

        public NPCBackendReadinessSnapshot LastSnapshot { get; private set; } = new NPCBackendReadinessSnapshot();

        [ShowInInspector]
        string AuthProbePreview => BuildAuthProbeUrl();

        [ShowInInspector]
        string LocalAiProbePreview => BuildLocalAiProbeUrl();

        void Reset()
        {
            AutoAssignReferences();
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
            {
                AutoAssignReferences();
            }
        }

        [Button("Auto Assign Backend References")]
        void AutoAssignReferences()
        {
            if (authService == null)
            {
                authService = FindAnyObjectByType<PlayerAuthService>(FindObjectsInactive.Include);
            }

            if (dialogueManager == null)
            {
                dialogueManager = FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
            }

            lastReadinessStatus = $"References assigned. auth={AuthProbePreview} localai={LocalAiProbePreview}";
        }

        [Button("Probe Backends")]
        public async void ProbeBackendsFromInspector()
        {
            await ProbeAsync();
        }

        public async Task<NPCBackendReadinessSnapshot> ProbeAsync(bool probeLocalAi = true)
        {
            AutoAssignReferences();

            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            using var scope = NPCFlowScope.Start(logger, NPCFlowStage.BackendRequest, nameof(NPCBackendReadinessService), data: new Dictionary<string, object>
            {
                ["authProbeUrl"] = AuthProbePreview,
                ["localAiProbeUrl"] = LocalAiProbePreview,
                ["requireAuthBackend"] = requireAuthBackend,
                ["requireLocalAiBackend"] = requireLocalAiBackend && probeLocalAi
            });

            var authResult = await ProbeEndpointAsync("AuthBackend", AuthProbePreview, allowHttpErrorAsReachable: true);
            NPCBackendProbeResult localAiResult;

            if (probeLocalAi)
            {
                localAiResult = await ProbeEndpointAsync("LocalAI", LocalAiProbePreview, allowHttpErrorAsReachable: false);
            }
            else
            {
                localAiResult = new NPCBackendProbeResult
                {
                    backendName = "LocalAI",
                    url = LocalAiProbePreview,
                    reachable = true,
                    responseCode = 0,
                    durationMs = 0,
                    status = "LocalAI probing deferred until post-login."
                };
            }

            var snapshot = new NPCBackendReadinessSnapshot
            {
                auth = authResult,
                localAi = localAiResult
            };

            LastSnapshot = snapshot;
            lastProbeDurationMs = snapshot.auth.durationMs + snapshot.localAi.durationMs;
            lastAuthBackendStatus = snapshot.auth.status;
            lastLocalAiBackendStatus = snapshot.localAi.status;

            bool healthy = snapshot.AllRequiredBackendsReachable(requireAuthBackend, requireLocalAiBackend && probeLocalAi);
            lastReadinessStatus = healthy
                ? "Required multiplayer dialogue backends are reachable."
                : "One or more required multiplayer dialogue backends are unreachable.";

            var data = new Dictionary<string, object>
            {
                ["authReachable"] = snapshot.auth.reachable,
                ["authStatus"] = snapshot.auth.status,
                ["localAiReachable"] = snapshot.localAi.reachable,
                ["localAiStatus"] = snapshot.localAi.status
            };

            if (healthy)
            {
                scope.Success(lastReadinessStatus, data);
                return snapshot;
            }

            if (failInitializationOnRequiredBackendFailure)
            {
                var exception = new InvalidOperationException(lastReadinessStatus);
                scope.Error(exception, lastReadinessStatus, data);
                throw exception;
            }

            scope.Warning(lastReadinessStatus, data);
            return snapshot;
        }

        async Task<NPCBackendProbeResult> ProbeEndpointAsync(string backendName, string url, bool allowHttpErrorAsReachable)
        {
            DateTime startedAt = DateTime.UtcNow;
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.timeout = Mathf.Max(1, Mathf.CeilToInt(requestTimeoutSeconds));

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            long durationMs = (long)(DateTime.UtcNow - startedAt).TotalMilliseconds;
            bool transportReachable = request.result != UnityWebRequest.Result.ConnectionError
                                      && request.result != UnityWebRequest.Result.DataProcessingError;
            bool reachable = transportReachable && (request.result == UnityWebRequest.Result.Success || allowHttpErrorAsReachable);
            string responseCodeText = request.responseCode <= 0 ? "n/a" : request.responseCode.ToString();
            string status = reachable
                ? $"{backendName} reachable via {url} (HTTP {responseCodeText})."
                : $"{backendName} unreachable via {url}: {request.error}";

            return new NPCBackendProbeResult
            {
                backendName = backendName,
                url = url,
                reachable = reachable,
                responseCode = request.responseCode,
                durationMs = durationMs,
                status = status
            };
        }

        string BuildAuthProbeUrl()
        {
            string baseUrl = authService == null ? "http://localhost:5100" : authService.ServiceBaseUrl;
            return CombineUrl(baseUrl, authProbeRelativePath);
        }

        string BuildLocalAiProbeUrl()
        {
            if (dialogueManager == null)
            {
                return "http://localhost:8080/v1/models";
            }

            return CombineUrl($"http://{dialogueManager.remoteHost}:{dialogueManager.remotePort}", localAiProbeRelativePath);
        }

        static string CombineUrl(string baseUrl, string relativePath)
        {
            string normalizedBase = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.Trim().TrimEnd('/');
            string normalizedPath = string.IsNullOrWhiteSpace(relativePath) ? string.Empty : relativePath.Trim().TrimStart('/');
            return string.IsNullOrWhiteSpace(normalizedPath) ? normalizedBase : $"{normalizedBase}/{normalizedPath}";
        }
    }
}
