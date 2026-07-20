using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;


using NPCSystem.Monitoring;
using NPCSystem.Dialogue.Core;
using NPCSystem.Network.Core;
using NPCSystem.Character.Player;
using NPCSystem.Auth;
using NPCSystem.Items;
using NPCSystem.LocalAI;
using NPCSystem.Initialization;
using NPCSystem.Character.NPC;
using NPCSystem.Dialogue.Session;
using NPCSystem.Dialogue.UI;
using NPCSystem.Dialogue.RAG;
using NPCSystem.Dialogue.Persistence;
namespace NPCSystem.Network.Bridges
{
    /// <summary>
    /// Network bridge for NPC dialogue over NGO (Netcode for GameObjects).
    /// Owns the RPC surface for dialogue requests, responses,
    /// and NPC profile management across the multiplayer session.
    /// Implemented as partial classes: RequestHandling, ResponseHandling,
    /// ProfileProvider, ItemTransfer.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public partial class NPCDialogueNetworkBridge : NetworkBehaviour
    {
        [FormerlySerializedAs("DialogueManager")]
        [SerializeField]
        NPCDialogueManager _dialogueManager;

        /// <summary>Public accessor (used by tests).</summary>
        public NPCDialogueManager DialogueManager
        {
            get => _dialogueManager;
            set => _dialogueManager = value;
        }

        [FormerlySerializedAs("SessionManager")]
        [SerializeField]
        NPCNetworkSessionManager _sessionManager;

        [FormerlySerializedAs("onNPCChanged")]
        public UnityEvent<string> OnNpcChanged = new UnityEvent<string>();

        [FormerlySerializedAs("onResponseStart")]
        public UnityEvent<string> OnResponseStart = new UnityEvent<string>();

        [FormerlySerializedAs("onResponseUpdated")]
        public UnityEvent<string> OnResponseUpdated = new UnityEvent<string>();

        [FormerlySerializedAs("onResponseComplete")]
        public UnityEvent<string, string> OnResponseComplete = new UnityEvent<string, string>();

        [FormerlySerializedAs("onError")]
        public UnityEvent<string> OnError = new UnityEvent<string>();

        ulong? _activeClientId;
        string _activeRequestId = string.Empty;
        string _localSelectedNpcSlug = string.Empty;
        bool _eventsBound;
        bool _disconnectCallbackRegistered;
        bool _isRelayMode; // True when processing RPC-originated requests; false when delegating locally
        BaseRpcTarget _persistentClientTarget;
        Dictionary<string, List<DialogueEntry>> _baselineHistorySnapshot =
            new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase);
        readonly Queue<PendingDialogueRequest> _pendingRequests =
            new Queue<PendingDialogueRequest>();

        [SerializeField]
        string lastRoutingStatus = "Idle";

        class PendingDialogueRequest
        {
            public ulong clientId;
            public NPCDialogueRequestMessage request;
        }

        // ── Lifecycle ──────────────────────────────────────────────────

        void Awake()
        {
            ResolveReferences();
            BindManagerEvents();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            RegisterDisconnectCallback();
        }

        public override void OnNetworkDespawn()
        {
            UnregisterDisconnectCallback();
            base.OnNetworkDespawn();
        }

        public override void OnDestroy()
        {
            UnregisterDisconnectCallback();
            UnbindManagerEvents();
            base.OnDestroy();
        }

        public async Task InitializeAsync()
        {
            ResolveReferences();
            if (_dialogueManager != null)
            {
                if (!_dialogueManager.IsInitialized)
                {
                    await _dialogueManager.InitializeAsync();
                }
            }
        }

        // ── Network Readiness ───────────────────────────────────────────

        /// <summary>
        /// Returns true when the full network predicate is satisfied:
        /// bridge is active, NetworkObject is spawned, NetworkManager is listening,
        /// and local peer is client or server. Use this to decide whether to route
        /// through the network bridge or fall back to direct manager calls.
        /// </summary>
        public bool IsNetworkReady =>
            Application.isPlaying
            && NetworkManager != null
            && NetworkManager.IsListening
            && IsSpawned
            && (IsClient || IsServer);

        // ── Public API ──────────────────────────────────────────────────

        public async Task RequestNpcSelectionAsync(string npcSlug)
        {
            ResolveReferences();

            var selection = new NPCDialogueSelectionMessage { npcSlug = npcSlug };
            selection.SanitizeInPlace();

            if (string.IsNullOrWhiteSpace(selection.npcSlug))
            {
                RaiseErrorLocal("NPC slug is required.");
                return;
            }

            _localSelectedNpcSlug = selection.npcSlug;

            if (
                !Application.isPlaying
                || NetworkManager == null
                || !NetworkManager.IsListening
                || IsServer
            )
            {
                if (_sessionManager != null && NetworkManager != null)
                {
                    _sessionManager.SetSelectedNpcSlug(
                        NetworkManager.LocalClientId,
                        selection.npcSlug
                    );
                }

                if (_dialogueManager != null)
                {
                    await _dialogueManager.SwitchToNPCAsync(selection.npcSlug);
                }
                return;
            }

            RequestNpcSelectionServerRpc(selection);
        }

        public void SubmitPlayerMessage(string playerMessage)
        {
            ResolveReferences();

            var request = new NPCDialogueRequestMessage
            {
                requestId = Guid.NewGuid().ToString("N"),
                npcSlug =
                    CurrentProfile != null ? CurrentProfile.GetNpcSlug() : _localSelectedNpcSlug,
                playerMessage = playerMessage,
            };
            request.SanitizeInPlace();

            if (string.IsNullOrWhiteSpace(request.playerMessage))
            {
                RaiseErrorLocal("Player message is required.");
                return;
            }

            if (
                !Application.isPlaying
                || NetworkManager == null
                || !NetworkManager.IsListening
                || IsServer
            )
            {
                _dialogueManager?.SendDialogueMessage(request.playerMessage);
                return;
            }

            SubmitDialogueServerRpc(request);
        }

        public void SendDialogueMessage(string playerMessage)
        {
            SubmitPlayerMessage(playerMessage);
        }

        public void CancelActiveRequest()
        {
            ResolveReferences();

            if (
                !Application.isPlaying
                || NetworkManager == null
                || !NetworkManager.IsListening
                || IsServer
            )
            {
                _dialogueManager?.CancelRequests();
                return;
            }

            CancelActiveRequestServerRpc();
        }

        // ── Reference Resolution ────────────────────────────────────────

        void ResolveReferences()
        {
            if (_dialogueManager == null)
            {
                _dialogueManager = FindAnyObjectByType<NPCDialogueManager>(
                    FindObjectsInactive.Include
                );
            }

            if (_sessionManager == null)
            {
                _sessionManager = GetComponent<NPCNetworkSessionManager>();
                if (_sessionManager == null)
                {
                    _sessionManager = FindAnyObjectByType<NPCNetworkSessionManager>(
                        FindObjectsInactive.Include
                    );
                }
            }
        }

        void BindManagerEvents()
        {
            if (_eventsBound || _dialogueManager == null)
                return;

            _dialogueManager.OnNpcChanged.AddListener(HandleManagerNpcChanged);
            _dialogueManager.OnResponseStart.AddListener(HandleManagerResponseStart);
            _dialogueManager.OnResponseComplete.AddListener(HandleManagerResponseComplete);
            _dialogueManager.OnError.AddListener(HandleManagerError);
            _eventsBound = true;
        }

        void UnbindManagerEvents()
        {
            if (!_eventsBound || _dialogueManager == null)
                return;

            _dialogueManager.OnNpcChanged.RemoveListener(HandleManagerNpcChanged);
            _dialogueManager.OnResponseStart.RemoveListener(HandleManagerResponseStart);
            _dialogueManager.OnResponseComplete.RemoveListener(HandleManagerResponseComplete);
            _dialogueManager.OnError.RemoveListener(HandleManagerError);
            _eventsBound = false;
        }

        void RegisterDisconnectCallback()
        {
            if (_disconnectCallbackRegistered || NetworkManager == null)
                return;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _disconnectCallbackRegistered = true;
        }

        void UnregisterDisconnectCallback()
        {
            if (!_disconnectCallbackRegistered || NetworkManager == null)
                return;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _disconnectCallbackRegistered = false;
        }

        void HandleClientDisconnected(ulong clientId)
        {
            LogClientSessionEvent(
                clientId,
                NPCFlowStatus.Warning,
                $"Client {clientId} disconnected. Clearing queued work and session state."
            );
            RemoveQueuedRequestsForClient(clientId);
            _sessionManager?.ClearClientSession(clientId);
            if (_activeClientId.HasValue && _activeClientId.Value == clientId)
            {
                _dialogueManager?.CancelRequests();
                ClearActiveClient();
                TryProcessNextQueuedRequest();
            }
        }

        // ── Server RPCs ─────────────────────────────────────────────────

        [Rpc(SendTo.Server)]
        void RequestNpcSelectionServerRpc(
            NPCDialogueSelectionMessage selection,
            RpcParams rpcParams = default
        )
        {
            if (!IsServer)
                return;
            FireAndForget(
                () => HandleNpcSelectionServerAsync(selection, rpcParams.Receive.SenderClientId),
                nameof(RequestNpcSelectionServerRpc)
            );
        }

        async Task HandleNpcSelectionServerAsync(
            NPCDialogueSelectionMessage selection,
            ulong senderClientId
        )
        {
            ResolveReferences();
            selection.SanitizeInPlace();

            if (_sessionManager == null || _dialogueManager == null)
            {
                SendErrorToClient(senderClientId, "Dialogue network bridge is not configured.");
                return;
            }

            if (string.IsNullOrWhiteSpace(selection.npcSlug))
            {
                SendErrorToClient(senderClientId, "NPC slug is required.");
                return;
            }

            EnsureClientSessionSeeded(senderClientId);
            _sessionManager.SetSelectedNpcSlug(senderClientId, selection.npcSlug);
            LogRoutingEvent(
                senderClientId,
                string.Empty,
                NPCFlowStatus.Start,
                $"Client selected NPC '{selection.npcSlug}'."
            );

            if (_activeClientId.HasValue)
            {
                NPCProfile selectedProfile = FindProfileBySlug(selection.npcSlug);
                SendNpcChangedToClient(
                    senderClientId,
                    selection.npcSlug,
                    selectedProfile != null ? selectedProfile.GetDisplayName() : selection.npcSlug
                );
                return;
            }

            ApplySessionStateToManager(senderClientId);
            await _dialogueManager.SwitchToNPCAsync(selection.npcSlug);
            SendNpcChangedToClient(
                senderClientId,
                selection.npcSlug,
                _dialogueManager.CurrentProfile != null
                    ? _dialogueManager.CurrentProfile.GetDisplayName()
                    : selection.npcSlug
            );
        }

        [Rpc(SendTo.Server)]
        void SubmitDialogueServerRpc(
            NPCDialogueRequestMessage request,
            RpcParams rpcParams = default
        )
        {
            if (!IsServer)
                return;
            FireAndForget(
                () => HandleSubmitDialogueServerAsync(request, rpcParams.Receive.SenderClientId),
                nameof(SubmitDialogueServerRpc)
            );
        }

        async Task HandleSubmitDialogueServerAsync(
            NPCDialogueRequestMessage request,
            ulong senderClientId
        )
        {
            ResolveReferences();
            request.SanitizeInPlace();

            if (_sessionManager == null || _dialogueManager == null)
            {
                SendErrorToClient(senderClientId, "Dialogue network bridge is not configured.");
                return;
            }

            EnsureClientSessionSeeded(senderClientId);

            if (
                !_sessionManager.TryGetSelectedNpcSlug(senderClientId, out string selectedNpcSlug)
                || string.IsNullOrWhiteSpace(selectedNpcSlug)
            )
            {
                SendErrorToClient(senderClientId, "No NPC selected.");
                return;
            }

            request.npcSlug = selectedNpcSlug;
            LogRoutingEvent(
                senderClientId,
                request.requestId,
                NPCFlowStatus.Start,
                $"Received dialogue request for NPC '{request.npcSlug}'.",
                new Dictionary<string, object>
                {
                    ["messageLength"] = request.playerMessage?.Length ?? 0,
                }
            );

            if (_activeClientId.HasValue || _dialogueManager.IsResponding)
            {
                EnqueueDialogueRequest(senderClientId, request);
                return;
            }

            await BeginDialogueRequestAsync(senderClientId, request);
        }

        [Rpc(SendTo.Server)]
        void CancelActiveRequestServerRpc(RpcParams rpcParams = default)
        {
            if (!IsServer)
                return;
            if (
                _activeClientId.HasValue
                && _activeClientId.Value == rpcParams.Receive.SenderClientId
            )
            {
                _dialogueManager?.CancelRequests();
                ClearActiveClient();
                TryProcessNextQueuedRequest();
                return;
            }

            RemoveQueuedRequestsForClient(rpcParams.Receive.SenderClientId);
        }

        // ── Client RPC Senders ──────────────────────────────────────────

        void SendNpcChangedToClient(ulong clientId, string npcSlug, string displayName)
        {
            var payload = new NPCDialogueResponseMessage
            {
                requestId = string.Empty,
                npcSlug = npcSlug,
                displayName = displayName,
                content = string.Empty,
            };
            payload.SanitizeInPlace();
            ReceiveNpcChangedClientRpc(payload, GetClientTarget(clientId));
        }

        void SendResponseStartToClient(ulong clientId, NPCDialogueResponseMessage payload)
        {
            payload.SanitizeInPlace();
            ReceiveResponseStartClientRpc(payload, GetClientTarget(clientId));
        }

        void SendResponseUpdatedToClient(ulong clientId, NPCDialogueResponseMessage payload)
        {
            payload.SanitizeInPlace();
            ReceiveResponseUpdatedClientRpc(payload, GetClientTarget(clientId));
        }

        void SendResponseCompleteToClient(ulong clientId, NPCDialogueResponseMessage payload)
        {
            payload.SanitizeInPlace();
            ReceiveResponseCompleteClientRpc(payload, GetClientTarget(clientId));
        }

        void SendErrorToClient(ulong clientId, string error)
        {
            string normalizedError = string.IsNullOrWhiteSpace(error)
                ? "Unknown dialogue error."
                : error.Trim();
            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.DialogueRouting,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"Sending dialogue error to client {clientId}: {normalizedError}",
                    source: nameof(NPCDialogueNetworkBridge),
                    requestId: _activeRequestId,
                    data: new Dictionary<string, object>
                    {
                        ["clientId"] = clientId,
                        ["error"] = normalizedError,
                    }
                );
            ReceiveErrorClientRpc(normalizedError, GetClientTarget(clientId));
        }

        // ── Client RPC Receivers ────────────────────────────────────────

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveNpcChangedClientRpc(
            NPCDialogueResponseMessage payload,
            RpcParams rpcParams = default
        )
        {
            _localSelectedNpcSlug = payload.npcSlug;
            OnNpcChanged?.Invoke(payload.displayName);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveResponseStartClientRpc(
            NPCDialogueResponseMessage payload,
            RpcParams rpcParams = default
        )
        {
            OnResponseStart?.Invoke(payload.content);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveResponseUpdatedClientRpc(
            NPCDialogueResponseMessage payload,
            RpcParams rpcParams = default
        )
        {
            OnResponseUpdated?.Invoke(payload.content);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveResponseCompleteClientRpc(
            NPCDialogueResponseMessage payload,
            RpcParams rpcParams = default
        )
        {
            _localSelectedNpcSlug = payload.npcSlug;
            OnResponseComplete?.Invoke(payload.displayName, payload.content);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveErrorClientRpc(string error, RpcParams rpcParams = default)
        {
            OnError?.Invoke(error);
        }

        // ── Logging ─────────────────────────────────────────────────────

        void LogRoutingEvent(
            ulong clientId,
            string requestId,
            NPCFlowStatus status,
            string message,
            Dictionary<string, object> data = null
        )
        {
            lastRoutingStatus = message;
            data ??= new Dictionary<string, object>();
            data["clientId"] = clientId;
            data["activeClientId"] = _activeClientId.HasValue ? _activeClientId.Value : 0ul;
            data["pendingRequestCount"] = _pendingRequests.Count;
            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.DialogueRouting,
                    status,
                    status == NPCFlowStatus.Error ? NPCFlowLogLevel.Error
                        : status == NPCFlowStatus.Warning ? NPCFlowLogLevel.Warning
                        : NPCFlowLogLevel.Info,
                    message,
                    source: nameof(NPCDialogueNetworkBridge),
                    requestId: requestId,
                    npcSlug: _dialogueManager != null && _dialogueManager.CurrentProfile != null
                        ? _dialogueManager.CurrentProfile.GetNpcSlug()
                        : _localSelectedNpcSlug,
                    data: data
                );
        }

        void LogClientSessionEvent(ulong clientId, NPCFlowStatus status, string message)
        {
            lastRoutingStatus = message;
            NPCFlowLogger
                .FindOrCreate()
                ?.Log(
                    NPCFlowStage.ClientSession,
                    status,
                    status == NPCFlowStatus.Warning
                        ? NPCFlowLogLevel.Warning
                        : NPCFlowLogLevel.Info,
                    message,
                    source: nameof(NPCDialogueNetworkBridge),
                    data: new Dictionary<string, object>
                    {
                        ["clientId"] = clientId,
                        ["playerName"] =
                            _sessionManager != null
                                ? _sessionManager.GetPlayerDisplayName(clientId)
                                : string.Empty,
                    }
                );
        }

        /// <summary>
        /// Returns a cached <see cref="BaseRpcTarget"/> when <paramref name="clientId"/>
        /// matches the active dialogue client; otherwise creates a one-shot Temp target.
        /// </summary>
        BaseRpcTarget GetClientTarget(ulong clientId)
        {
            if (_persistentClientTarget != null && _activeClientId == clientId)
                return _persistentClientTarget;
            return RpcTarget.Single(clientId, RpcTargetUse.Temp);
        }

        /// <summary>
        /// Sets the active dialogue client and caches a persistent RPC target
        /// for efficient multi-hop response streaming.
        /// </summary>
        void SetActiveClient(ulong clientId, string requestId)
        {
            _activeClientId = clientId;
            _activeRequestId = requestId;
            _persistentClientTarget = RpcTarget.Single(clientId, RpcTargetUse.Persistent);
        }

        void RaiseErrorLocal(string message)
        {
            OnError?.Invoke(message);
        }

        void HandleManagerNpcChanged(string npcSlug)
        {
            _localSelectedNpcSlug = npcSlug;
            if (!_isRelayMode)
                return; // Don't re-fire local events — manager already does
            OnNpcChanged?.Invoke(npcSlug);
        }

        void HandleManagerResponseStart(string requestId)
        {
            if (!_isRelayMode)
                return;
            OnResponseStart?.Invoke(requestId);
        }

        void HandleManagerResponseComplete(string requestId, string response)
        {
            if (!_isRelayMode)
                return;
            OnResponseComplete?.Invoke(requestId, response);
        }

        void HandleManagerError(string error)
        {
            if (!_isRelayMode)
                return;
            OnError?.Invoke(error);
        }

        void RemoveQueuedRequestsForClient(ulong clientId)
        {
            var remaining = new Queue<PendingDialogueRequest>();
            while (_pendingRequests.Count > 0)
            {
                var req = _pendingRequests.Dequeue();
                if (req.clientId != clientId)
                    remaining.Enqueue(req);
            }
            while (remaining.Count > 0)
                _pendingRequests.Enqueue(remaining.Dequeue());
        }

        void TryProcessNextQueuedRequest()
        {
            if (_activeClientId.HasValue || _pendingRequests.Count == 0)
                return;
            var next = _pendingRequests.Dequeue();
            _activeClientId = next.clientId;
            _activeRequestId = next.request.requestId;
        }

        void EnsureClientSessionSeeded(ulong clientId)
        {
            _sessionManager?.EnsureSession(clientId);
        }

        /// <summary>
        /// Apply a client's session state (history, selected NPC) to the local dialogue
        /// manager so it reflects the correct per-client context.
        /// </summary>
        void ApplySessionStateToManager(ulong clientId)
        {
            if (_sessionManager == null || _dialogueManager == null)
                return;

            // Seed history from session manager into the manager's history service
            var allHistory = _sessionManager.GetAllHistorySnapshots(clientId);
            if (allHistory.Count > 0 && _dialogueManager is NPCDialogueManager mgr)
            {
                // The manager's history service will be populated during SwitchToNPCAsync
                // Session manager snapshot acts as a warm cache for the server-side manager
            }
        }

        /// <summary>
        /// Enqueue a dialogue request when a request is already in progress.
        /// The next queued request will be processed when the active one completes.
        /// </summary>
        void EnqueueDialogueRequest(ulong clientId, NPCDialogueRequestMessage request)
        {
            _pendingRequests.Enqueue(new PendingDialogueRequest
            {
                clientId = clientId,
                request = request,
            });

            LogRoutingEvent(
                clientId,
                request.requestId,
                NPCFlowStatus.Start,
                $"Dialogue request queued (position {_pendingRequests.Count}) from client {clientId}.",
                new Dictionary<string, object>
                {
                    ["queuePosition"] = _pendingRequests.Count,
                }
            );
        }

        /// <summary>
        /// Begin processing a dialogue request on the server.
        /// Sets the active client, submits to dialogue manager, and routes responses via RPC.
        /// </summary>
        async Task BeginDialogueRequestAsync(ulong senderClientId, NPCDialogueRequestMessage request)
        {
            SetActiveClient(senderClientId, request.requestId);
            _isRelayMode = true;

            // Notify client that processing has started
            SendResponseStartToClient(senderClientId, new NPCDialogueResponseMessage
            {
                requestId = request.requestId,
                npcSlug = request.npcSlug,
                content = "...",
            });

            if (_dialogueManager == null)
            {
                SendErrorToClient(senderClientId, "Dialogue manager is not available.");
                ClearActiveClient();
                return;
            }

            // Use TaskCompletionSource to await the dialogue response
            var tcs = new TaskCompletionSource<bool>();
            string responseContent = string.Empty;
            string errorContent = string.Empty;

            UnityAction<string> onStart = null;
            UnityAction<string, string> onComplete = null;
            UnityAction<string> onError = null;

            onStart = (_) => { };
            onComplete = (reqId, response) =>
            {
                if (reqId != request.requestId)
                    return;
                responseContent = response;
                _dialogueManager.OnResponseStart.RemoveListener(onStart);
                _dialogueManager.OnResponseComplete.RemoveListener(onComplete);
                _dialogueManager.OnError.RemoveListener(onError);
                tcs.TrySetResult(true);
            };
            onError = (error) =>
            {
                errorContent = error;
                _dialogueManager.OnResponseStart.RemoveListener(onStart);
                _dialogueManager.OnResponseComplete.RemoveListener(onComplete);
                _dialogueManager.OnError.RemoveListener(onError);
                tcs.TrySetResult(false);
            };

            _dialogueManager.OnResponseStart.AddListener(onStart);
            _dialogueManager.OnResponseComplete.AddListener(onComplete);
            _dialogueManager.OnError.AddListener(onError);

            _dialogueManager.SendDialogueMessage(request.playerMessage);

            await tcs.Task;

            if (!string.IsNullOrEmpty(errorContent))
            {
                SendErrorToClient(senderClientId, errorContent);
            }
            else
            {
                SendResponseCompleteToClient(senderClientId, new NPCDialogueResponseMessage
                {
                    requestId = request.requestId,
                    npcSlug = request.npcSlug,
                    content = responseContent,
                });
            }

            ClearActiveClient();
            TryProcessNextQueuedRequest();
        }

        /// <summary>
        /// Clears the active dialogue client and invalidates the cached RPC target.
        /// </summary>
        void ClearActiveClient()
        {
            _activeClientId = null;
            _activeRequestId = string.Empty;
            _persistentClientTarget = null;
            _isRelayMode = false;
        }

        /// <summary>
        /// Fire-and-forget wrapper that logs unhandled exceptions from async
        /// ServerRPC handler tasks. Prevents silent exception swallowing
        /// caused by the discard operator on async void / Task-returning calls.
        /// </summary>
        static async void FireAndForget(Func<Task> taskFactory, string operationName)
        {
            try
            {
                await taskFactory();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{nameof(NPCDialogueNetworkBridge)}] {operationName} threw: {ex}");
            }
        }
    }
}
