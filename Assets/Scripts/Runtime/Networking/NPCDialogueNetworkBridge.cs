using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

namespace NPCSystem
{
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public class NPCDialogueNetworkBridge : NetworkBehaviour
    {
        [Header("References")]
        public NPCDialogueManager dialogueManager;
        public NPCNetworkSessionManager sessionManager;

        [Header("Events")]
        public UnityEvent<string> onNPCChanged = new UnityEvent<string>();
        public UnityEvent<string> onResponseStart = new UnityEvent<string>();
        public UnityEvent<string> onResponseUpdated = new UnityEvent<string>();
        public UnityEvent<string, string> onResponseComplete = new UnityEvent<string, string>();
        public UnityEvent<string> onError = new UnityEvent<string>();
        public UnityEvent<NPCNotebookStateMessage> onNotebookStateChanged = new UnityEvent<NPCNotebookStateMessage>();

        ulong? _activeClientId;
        string _activeRequestId = string.Empty;
        string _localSelectedNpcSlug = string.Empty;
        NPCNotebookStateMessage _currentNotebookState;
        bool _eventsBound;
        bool _disconnectCallbackRegistered;
        readonly Queue<PendingDialogueRequest> _pendingRequests = new Queue<PendingDialogueRequest>();
        Dictionary<string, List<DialogueEntry>> _baselineHistorySnapshot = new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase);
        NPCEvidenceStateSnapshot _baselineEvidenceSnapshot = new NPCEvidenceStateSnapshot();
        [SerializeField] string lastRoutingStatus = "Idle";

        class PendingDialogueRequest
        {
            public ulong clientId;
            public NPCDialogueRequestMessage request;
        }

        public NPCProfile[] Profiles => dialogueManager == null ? Array.Empty<NPCProfile>() : dialogueManager.Profiles;
        public NPCProfile currentProfile
        {
            get
            {
                if (dialogueManager != null && (!Application.isPlaying || NetworkManager == null || !NetworkManager.IsListening || IsServer))
                {
                    return dialogueManager.currentProfile;
                }

                return FindProfileBySlug(_localSelectedNpcSlug);
            }
        }

        public bool isResponding => dialogueManager != null && dialogueManager.isResponding;
        public NPCNotebookStateMessage CurrentNotebookState => _currentNotebookState;

        string ActiveClientPreview => _activeClientId.HasValue ? _activeClientId.Value.ToString() : "<none>";

        int PendingRequestCount => _pendingRequests.Count;

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
            if (dialogueManager != null)
            {
                if (dialogueManager.isInitialized)
                {
                    CaptureBaselineState();
                    UpdateNotebookStateLocal();
                }
                else if (dialogueManager.initializeOnStart)
                {
                    await dialogueManager.InitializeAsync();
                    CaptureBaselineState();
                    UpdateNotebookStateLocal();
                }
            }
        }

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

            if (!Application.isPlaying || NetworkManager == null || !NetworkManager.IsListening || IsServer)
            {
                if (sessionManager != null && NetworkManager != null)
                {
                    sessionManager.SetSelectedNpcSlug(NetworkManager.LocalClientId, selection.npcSlug);
                }

                if (dialogueManager != null)
                {
                    await dialogueManager.SwitchToNPCAsync(selection.npcSlug);
                    UpdateNotebookStateLocal();
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
                npcSlug = currentProfile != null ? currentProfile.GetNpcSlug() : _localSelectedNpcSlug,
                playerMessage = playerMessage
            };
            request.SanitizeInPlace();

            if (string.IsNullOrWhiteSpace(request.playerMessage))
            {
                RaiseErrorLocal("Player message is required.");
                return;
            }

            if (!Application.isPlaying || NetworkManager == null || !NetworkManager.IsListening || IsServer)
            {
                dialogueManager?.SendMessage(request.playerMessage);
                return;
            }

            SubmitDialogueServerRpc(request);
        }

        public void CancelActiveRequest()
        {
            ResolveReferences();

            if (!Application.isPlaying || NetworkManager == null || !NetworkManager.IsListening || IsServer)
            {
                dialogueManager?.CancelRequests();
                return;
            }

            CancelActiveRequestServerRpc();
        }

        void ResolveReferences()
        {
            if (dialogueManager == null)
            {
                dialogueManager = FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
            }

            if (sessionManager == null)
            {
                sessionManager = GetComponent<NPCNetworkSessionManager>();
                if (sessionManager == null)
                {
                    sessionManager = FindAnyObjectByType<NPCNetworkSessionManager>(FindObjectsInactive.Include);
                }
            }
        }

        void BindManagerEvents()
        {
            if (_eventsBound || dialogueManager == null) return;

            dialogueManager.onNPCChanged.AddListener(HandleManagerNpcChanged);
            dialogueManager.onResponseStart.AddListener(HandleManagerResponseStart);
            dialogueManager.onResponseUpdated.AddListener(HandleManagerResponseUpdated);
            dialogueManager.onResponseComplete.AddListener(HandleManagerResponseComplete);
            dialogueManager.onError.AddListener(HandleManagerError);
            _eventsBound = true;
        }

        void UnbindManagerEvents()
        {
            if (!_eventsBound || dialogueManager == null) return;

            dialogueManager.onNPCChanged.RemoveListener(HandleManagerNpcChanged);
            dialogueManager.onResponseStart.RemoveListener(HandleManagerResponseStart);
            dialogueManager.onResponseUpdated.RemoveListener(HandleManagerResponseUpdated);
            dialogueManager.onResponseComplete.RemoveListener(HandleManagerResponseComplete);
            dialogueManager.onError.RemoveListener(HandleManagerError);
            _eventsBound = false;
        }

        void RegisterDisconnectCallback()
        {
            if (_disconnectCallbackRegistered || NetworkManager == null) return;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _disconnectCallbackRegistered = true;
        }

        void UnregisterDisconnectCallback()
        {
            if (!_disconnectCallbackRegistered || NetworkManager == null) return;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _disconnectCallbackRegistered = false;
        }

        void HandleClientDisconnected(ulong clientId)
        {
            LogClientSessionEvent(clientId, NPCFlowStatus.Warning, $"Client {clientId} disconnected. Clearing queued work and session state.");
            RemoveQueuedRequestsForClient(clientId);
            sessionManager?.ClearClientSession(clientId);
            if (_activeClientId.HasValue && _activeClientId.Value == clientId)
            {
                dialogueManager?.CancelRequests();
                _activeClientId = null;
                _activeRequestId = string.Empty;
                TryProcessNextQueuedRequest();
            }
        }

        void HandleManagerNpcChanged(string displayName)
        {
            if (ShouldRelayLocally())
            {
                onNPCChanged?.Invoke(displayName);
                UpdateNotebookStateLocal();
            }
        }

        void HandleManagerResponseStart(string playerMessage)
        {
            if (_activeClientId.HasValue && IsServer && NetworkManager != null && NetworkManager.IsListening)
            {
                var payload = BuildResponsePayload(playerMessage);
                LogRoutingEvent(_activeClientId.Value, _activeRequestId, NPCFlowStatus.Start, "Relaying response-start event to requesting client.");
                SendResponseStartToClient(_activeClientId.Value, payload);
                return;
            }

            if (ShouldRelayLocally())
            {
                onResponseStart?.Invoke(playerMessage);
            }
        }

        void HandleManagerResponseUpdated(string partialResponse)
        {
            if (_activeClientId.HasValue && IsServer && NetworkManager != null && NetworkManager.IsListening)
            {
                var payload = BuildResponsePayload(partialResponse);
                LogRoutingEvent(_activeClientId.Value, _activeRequestId, NPCFlowStatus.Start, "Relaying response-update event to requesting client.");
                SendResponseUpdatedToClient(_activeClientId.Value, payload);
                return;
            }

            if (ShouldRelayLocally())
            {
                onResponseUpdated?.Invoke(partialResponse);
            }
        }

        void HandleManagerResponseComplete(string npcDisplayName, string response)
        {
            if (_activeClientId.HasValue && IsServer && NetworkManager != null && NetworkManager.IsListening)
            {
                ulong clientId = _activeClientId.Value;
                SyncSessionFromManagerState(clientId);

                var payload = BuildResponsePayload(response);
                payload.displayName = npcDisplayName;
                LogRoutingEvent(clientId, _activeRequestId, NPCFlowStatus.Success, "Relaying completed NPC response to requesting client.");
                SendResponseCompleteToClient(clientId, payload);
                SendNotebookStateToClient(clientId, BuildNotebookStateMessage());
                _activeClientId = null;
                _activeRequestId = string.Empty;
                TryProcessNextQueuedRequest();
                return;
            }

            if (ShouldRelayLocally())
            {
                onResponseComplete?.Invoke(npcDisplayName, response);
                UpdateNotebookStateLocal();
            }
        }

        void HandleManagerError(string error)
        {
            if (_activeClientId.HasValue && IsServer && NetworkManager != null && NetworkManager.IsListening)
            {
                ulong clientId = _activeClientId.Value;
                SyncSessionFromManagerState(clientId);
                LogRoutingEvent(clientId, _activeRequestId, NPCFlowStatus.Error, $"Relaying dialogue error to requesting client: {error}");
                SendErrorToClient(clientId, error);
                _activeClientId = null;
                _activeRequestId = string.Empty;
                TryProcessNextQueuedRequest();
                return;
            }

            if (ShouldRelayLocally())
            {
                onError?.Invoke(error);
            }
        }

        bool ShouldRelayLocally()
        {
            return !Application.isPlaying || NetworkManager == null || !NetworkManager.IsListening || IsServer;
        }

        void RaiseErrorLocal(string error)
        {
            if (!string.IsNullOrWhiteSpace(error))
            {
                onError?.Invoke(error);
            }
        }

        [Rpc(SendTo.Server)]
        void RequestNpcSelectionServerRpc(NPCDialogueSelectionMessage selection, RpcParams rpcParams = default)
        {
            _ = HandleNpcSelectionServerAsync(selection, rpcParams.Receive.SenderClientId);
        }

        async Task HandleNpcSelectionServerAsync(NPCDialogueSelectionMessage selection, ulong senderClientId)
        {
            ResolveReferences();
            selection.SanitizeInPlace();

            if (sessionManager == null || dialogueManager == null)
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
            sessionManager.SetSelectedNpcSlug(senderClientId, selection.npcSlug);
            LogRoutingEvent(senderClientId, string.Empty, NPCFlowStatus.Start, $"Client selected NPC '{selection.npcSlug}'.");

            if (_activeClientId.HasValue)
            {
                NPCProfile selectedProfile = FindProfileBySlug(selection.npcSlug);
                SendNpcChangedToClient(senderClientId, selection.npcSlug, selectedProfile != null ? selectedProfile.GetDisplayName() : selection.npcSlug);
                SendNotebookStateToClient(senderClientId, BuildNotebookStateMessageForClient(senderClientId));
                return;
            }

            ApplySessionStateToManager(senderClientId);
            await dialogueManager.SwitchToNPCAsync(selection.npcSlug);
            SendNpcChangedToClient(senderClientId, selection.npcSlug, dialogueManager.currentProfile != null ? dialogueManager.currentProfile.GetDisplayName() : selection.npcSlug);
            SendNotebookStateToClient(senderClientId, BuildNotebookStateMessage());
        }

        [Rpc(SendTo.Server)]
        void SubmitDialogueServerRpc(NPCDialogueRequestMessage request, RpcParams rpcParams = default)
        {
            _ = HandleSubmitDialogueServerAsync(request, rpcParams.Receive.SenderClientId);
        }

        async Task HandleSubmitDialogueServerAsync(NPCDialogueRequestMessage request, ulong senderClientId)
        {
            ResolveReferences();
            request.SanitizeInPlace();

            if (sessionManager == null || dialogueManager == null)
            {
                SendErrorToClient(senderClientId, "Dialogue network bridge is not configured.");
                return;
            }

            EnsureClientSessionSeeded(senderClientId);

            if (!sessionManager.TryGetSelectedNpcSlug(senderClientId, out string selectedNpcSlug) || string.IsNullOrWhiteSpace(selectedNpcSlug))
            {
                SendErrorToClient(senderClientId, "No NPC selected.");
                return;
            }

            request.npcSlug = selectedNpcSlug;
            LogRoutingEvent(senderClientId, request.requestId, NPCFlowStatus.Start,
                $"Received dialogue request for NPC '{request.npcSlug}'.",
                new Dictionary<string, object>
                {
                    ["messageLength"] = request.playerMessage?.Length ?? 0
                });

            if (_activeClientId.HasValue || dialogueManager.isResponding)
            {
                EnqueueDialogueRequest(senderClientId, request);
                return;
            }

            await BeginDialogueRequestAsync(senderClientId, request);
        }

        [Rpc(SendTo.Server)]
        void CancelActiveRequestServerRpc(RpcParams rpcParams = default)
        {
            if (_activeClientId.HasValue && _activeClientId.Value == rpcParams.Receive.SenderClientId)
            {
                dialogueManager?.CancelRequests();
                _activeClientId = null;
                _activeRequestId = string.Empty;
                TryProcessNextQueuedRequest();
                return;
            }

            RemoveQueuedRequestsForClient(rpcParams.Receive.SenderClientId);
        }

        void SendNpcChangedToClient(ulong clientId, string npcSlug, string displayName)
        {
            var payload = new NPCDialogueResponseMessage
            {
                requestId = string.Empty,
                npcSlug = npcSlug,
                displayName = displayName,
                content = string.Empty
            };
            payload.SanitizeInPlace();
            ReceiveNpcChangedClientRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        void SendResponseStartToClient(ulong clientId, NPCDialogueResponseMessage payload)
        {
            payload.SanitizeInPlace();
            ReceiveResponseStartClientRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        void SendResponseUpdatedToClient(ulong clientId, NPCDialogueResponseMessage payload)
        {
            payload.SanitizeInPlace();
            ReceiveResponseUpdatedClientRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        void SendResponseCompleteToClient(ulong clientId, NPCDialogueResponseMessage payload)
        {
            payload.SanitizeInPlace();
            ReceiveResponseCompleteClientRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        void SendErrorToClient(ulong clientId, string error)
        {
            string normalizedError = string.IsNullOrWhiteSpace(error) ? "Unknown dialogue error." : error.Trim();
            NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.DialogueRouting, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                $"Sending dialogue error to client {clientId}: {normalizedError}",
                source: nameof(NPCDialogueNetworkBridge), requestId: _activeRequestId,
                data: new Dictionary<string, object>
                {
                    ["clientId"] = clientId,
                    ["error"] = normalizedError
                });
            ReceiveErrorClientRpc(normalizedError, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        void SendNotebookStateToClient(ulong clientId, NPCNotebookStateMessage payload)
        {
            payload.SanitizeInPlace();
            ReceiveNotebookStateClientRpc(payload, RpcTarget.Single(clientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveNpcChangedClientRpc(NPCDialogueResponseMessage payload, RpcParams rpcParams = default)
        {
            _localSelectedNpcSlug = payload.npcSlug;
            onNPCChanged?.Invoke(payload.displayName);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveResponseStartClientRpc(NPCDialogueResponseMessage payload, RpcParams rpcParams = default)
        {
            onResponseStart?.Invoke(payload.content);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveResponseUpdatedClientRpc(NPCDialogueResponseMessage payload, RpcParams rpcParams = default)
        {
            onResponseUpdated?.Invoke(payload.content);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveResponseCompleteClientRpc(NPCDialogueResponseMessage payload, RpcParams rpcParams = default)
        {
            _localSelectedNpcSlug = payload.npcSlug;
            onResponseComplete?.Invoke(payload.displayName, payload.content);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveErrorClientRpc(string error, RpcParams rpcParams = default)
        {
            onError?.Invoke(error);
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void ReceiveNotebookStateClientRpc(NPCNotebookStateMessage payload, RpcParams rpcParams = default)
        {
            _currentNotebookState = payload;
            _localSelectedNpcSlug = string.IsNullOrWhiteSpace(payload.npcSlug) ? _localSelectedNpcSlug : payload.npcSlug;
            onNotebookStateChanged?.Invoke(payload);
        }

        NPCDialogueResponseMessage BuildResponsePayload(string content)
        {
            return new NPCDialogueResponseMessage
            {
                requestId = _activeRequestId,
                npcSlug = dialogueManager != null && dialogueManager.currentProfile != null ? dialogueManager.currentProfile.GetNpcSlug() : _localSelectedNpcSlug,
                displayName = dialogueManager != null && dialogueManager.currentProfile != null ? dialogueManager.currentProfile.GetDisplayName() : currentProfile != null ? currentProfile.GetDisplayName() : string.Empty,
                content = content ?? string.Empty
            };
        }

        NPCNotebookStateMessage BuildNotebookStateMessage()
        {
            return NPCNotebookStateFormatter.Build(
                dialogueManager != null ? dialogueManager.CaptureEvidenceSnapshot() : new NPCEvidenceStateSnapshot(),
                dialogueManager != null && dialogueManager.currentProfile != null ? dialogueManager.currentProfile.GetNpcSlug() : _localSelectedNpcSlug);
        }

        void UpdateNotebookStateLocal()
        {
            if (!ShouldRelayLocally()) return;
            _currentNotebookState = BuildNotebookStateMessage();
            onNotebookStateChanged?.Invoke(_currentNotebookState);
        }

        NPCNotebookStateMessage BuildNotebookStateMessageForClient(ulong clientId)
        {
            string selectedNpcSlug = string.Empty;
            if (sessionManager != null)
            {
                sessionManager.TryGetSelectedNpcSlug(clientId, out selectedNpcSlug);
            }

            return NPCNotebookStateFormatter.Build(
                sessionManager != null ? sessionManager.GetEvidenceSnapshot(clientId) : new NPCEvidenceStateSnapshot(),
                selectedNpcSlug);
        }

        public void RefreshNotebookStateForClient(ulong clientId)
        {
            if (!IsServer || NetworkManager == null || !NetworkManager.IsListening)
            {
                return;
            }

            SendNotebookStateToClient(clientId, BuildNotebookStateMessageForClient(clientId));
        }

        void CaptureBaselineState()
        {
            if (dialogueManager == null) return;
            _baselineHistorySnapshot = CloneHistorySnapshot(dialogueManager.CaptureHistorySnapshot());
            _baselineEvidenceSnapshot = dialogueManager.CaptureEvidenceSnapshot()?.Clone() ?? new NPCEvidenceStateSnapshot();
        }

        void EnqueueDialogueRequest(ulong clientId, NPCDialogueRequestMessage request)
        {
            _pendingRequests.Enqueue(new PendingDialogueRequest
            {
                clientId = clientId,
                request = request
            });
            LogRoutingEvent(clientId, request.requestId, NPCFlowStatus.Warning,
                "Dialogue request queued because another client request is in progress.",
                new Dictionary<string, object>
                {
                    ["pendingRequestCount"] = _pendingRequests.Count
                });
        }

        async Task BeginDialogueRequestAsync(ulong clientId, NPCDialogueRequestMessage request)
        {
            _activeClientId = clientId;
            _activeRequestId = request.requestId;
            await WaitForResolvedPlayerNameAsync(clientId);
            ApplySessionStateToManager(clientId);
            LogRoutingEvent(clientId, request.requestId, NPCFlowStatus.Start,
                $"Applying client session and switching to NPC '{request.npcSlug}' before dialogue generation.");
            await dialogueManager.SwitchToNPCAsync(request.npcSlug);
            dialogueManager.SendMessage(request.playerMessage);
        }

        async Task WaitForResolvedPlayerNameAsync(ulong clientId)
        {
            string initialName = ResolvePlayerDisplayName(clientId);
            if (!LooksLikeFallbackPlayerName(initialName))
            {
                return;
            }

            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(50);
                string updatedName = ResolvePlayerDisplayName(clientId);
                if (!LooksLikeFallbackPlayerName(updatedName))
                {
                    LogClientSessionEvent(clientId, NPCFlowStatus.Success,
                        $"Resolved player display name to '{updatedName}' before dialogue generation.");
                    return;
                }
            }

            LogClientSessionEvent(clientId, NPCFlowStatus.Warning,
                $"Player display name still unresolved before dialogue generation. Using fallback '{ResolvePlayerDisplayName(clientId)}'.");
        }

        void TryProcessNextQueuedRequest()
        {
            if (_activeClientId.HasValue || dialogueManager == null || sessionManager == null || !Application.isPlaying) return;

            while (_pendingRequests.Count > 0)
            {
                PendingDialogueRequest pending = _pendingRequests.Dequeue();
                if (!sessionManager.TryGetSelectedNpcSlug(pending.clientId, out string selectedNpcSlug) || string.IsNullOrWhiteSpace(selectedNpcSlug))
                {
                    continue;
                }

                pending.request.npcSlug = selectedNpcSlug;
                _ = BeginDialogueRequestAsync(pending.clientId, pending.request);
                break;
            }
        }

        void RemoveQueuedRequestsForClient(ulong clientId)
        {
            if (_pendingRequests.Count == 0) return;

            var preservedRequests = new Queue<PendingDialogueRequest>();
            while (_pendingRequests.Count > 0)
            {
                PendingDialogueRequest pending = _pendingRequests.Dequeue();
                if (pending.clientId != clientId)
                {
                    preservedRequests.Enqueue(pending);
                }
            }

            while (preservedRequests.Count > 0)
            {
                _pendingRequests.Enqueue(preservedRequests.Dequeue());
            }
        }

        void EnsureClientSessionSeeded(ulong clientId)
        {
            if (sessionManager == null) return;
            if (dialogueManager != null && _baselineHistorySnapshot.Count == 0)
            {
                CaptureBaselineState();
            }

            string playerDisplayName = ResolvePlayerDisplayName(clientId);
            if (sessionManager.HasSession(clientId)) return;

            sessionManager.SetAllHistorySnapshots(clientId, CloneHistorySnapshot(_baselineHistorySnapshot));
            sessionManager.SetEvidenceSnapshot(clientId, _baselineEvidenceSnapshot?.Clone() ?? new NPCEvidenceStateSnapshot());
            sessionManager.SetPlayerDisplayName(clientId, playerDisplayName);
            LogClientSessionEvent(clientId, NPCFlowStatus.Success, $"Seeded new client session for '{playerDisplayName}'.");
        }

        void ApplySessionStateToManager(ulong clientId)
        {
            if (dialogueManager == null || sessionManager == null) return;
            sessionManager.SetPlayerDisplayName(clientId, ResolvePlayerDisplayName(clientId));
            dialogueManager.ApplyHistorySnapshot(sessionManager.GetAllHistorySnapshots(clientId));
            dialogueManager.ApplyEvidenceSnapshot(sessionManager.GetEvidenceSnapshot(clientId));
            dialogueManager.SetRuntimePlayerContext(sessionManager.GetPlayerDisplayName(clientId), clientId);
            LogClientSessionEvent(clientId, NPCFlowStatus.Success,
                $"Applied session to dialogue manager for '{sessionManager.GetPlayerDisplayName(clientId)}'.");
        }

        void SyncSessionFromManagerState(ulong clientId)
        {
            if (dialogueManager == null || sessionManager == null) return;
            sessionManager.SetAllHistorySnapshots(clientId, dialogueManager.CaptureHistorySnapshot());
            sessionManager.SetEvidenceSnapshot(clientId, dialogueManager.CaptureEvidenceSnapshot());
            LogClientSessionEvent(clientId, NPCFlowStatus.Success,
                $"Synchronized dialogue manager state back into session cache for '{sessionManager.GetPlayerDisplayName(clientId)}'.");
        }

        NPCProfile FindProfileBySlug(string npcSlug)
        {
            if (dialogueManager == null || string.IsNullOrWhiteSpace(npcSlug)) return null;

            foreach (NPCProfile profile in dialogueManager.Profiles)
            {
                if (profile != null && string.Equals(profile.GetNpcSlug(), npcSlug.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return profile;
                }
            }

            return null;
        }

        static Dictionary<string, List<DialogueEntry>> CloneHistorySnapshot(Dictionary<string, List<DialogueEntry>> historyByNpc)
        {
            var clone = new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in historyByNpc ?? new Dictionary<string, List<DialogueEntry>>(StringComparer.OrdinalIgnoreCase))
            {
                List<DialogueEntry> entries = new List<DialogueEntry>();
                foreach (DialogueEntry entry in pair.Value ?? new List<DialogueEntry>())
                {
                    if (entry == null) continue;
                    entries.Add(new DialogueEntry
                    {
                        role = entry.role,
                        content = entry.content,
                        timestampUtc = entry.timestampUtc
                    });
                }
                clone[pair.Key] = entries;
            }

            return clone;
        }

        string ResolvePlayerDisplayName(ulong clientId)
        {
            if (NetworkManager == null || !NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
            {
                return $"Player {clientId}";
            }

            NetworkObject playerObject = client.PlayerObject;
            if (playerObject == null)
            {
                return $"Player {clientId}";
            }

            NPCPlayerNetworkAvatar avatar = playerObject.GetComponent<NPCPlayerNetworkAvatar>();
            return avatar != null ? avatar.DisplayName : $"Player {clientId}";
        }

        static bool LooksLikeFallbackPlayerName(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName)) return true;

            string normalized = playerName.Trim();
            if (!normalized.StartsWith("Player ", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return ulong.TryParse(normalized.Substring("Player ".Length), out _);
        }

        void LogRoutingEvent(ulong clientId, string requestId, NPCFlowStatus status, string message, Dictionary<string, object> data = null)
        {
            lastRoutingStatus = message;
            data ??= new Dictionary<string, object>();
            data["clientId"] = clientId;
            data["activeClientId"] = _activeClientId.HasValue ? _activeClientId.Value : 0ul;
            data["pendingRequestCount"] = _pendingRequests.Count;
            NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.DialogueRouting, status,
                status == NPCFlowStatus.Error ? NPCFlowLogLevel.Error :
                status == NPCFlowStatus.Warning ? NPCFlowLogLevel.Warning : NPCFlowLogLevel.Info,
                message,
                source: nameof(NPCDialogueNetworkBridge),
                requestId: requestId,
                npcSlug: dialogueManager != null && dialogueManager.currentProfile != null ? dialogueManager.currentProfile.GetNpcSlug() : _localSelectedNpcSlug,
                data: data);
        }

        void LogClientSessionEvent(ulong clientId, NPCFlowStatus status, string message)
        {
            lastRoutingStatus = message;
            NPCFlowLogger.FindOrCreate()?.Log(NPCFlowStage.ClientSession, status,
                status == NPCFlowStatus.Warning ? NPCFlowLogLevel.Warning : NPCFlowLogLevel.Info,
                message,
                source: nameof(NPCDialogueNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["clientId"] = clientId,
                    ["playerName"] = sessionManager != null ? sessionManager.GetPlayerDisplayName(clientId) : string.Empty
                });
        }
    }
}
