using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EditorAttributes;
using Postgrest.Exceptions;
using Supabase.Interfaces;
using Supabase.Realtime;
using Supabase.Realtime.Interfaces;
using Supabase.Realtime.Models;
using Supabase.Realtime.PostgresChanges;
using UnityEngine;

namespace NPCSystem
{
    /// <summary>
    /// Manages Supabase Realtime WebSocket lifecycle (with REST polling
    /// fallback for WebGL where ClientWebSocket is unavailable).
    ///
    /// Tracks a single PostgresChanges subscription on the
    /// <c>dialogue_turns</c> table so that NPC dialogue UI can react live
    /// to new turns without polling the REST API on every frame.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public class SupabaseRealtimeService : MonoBehaviour
    {
        [FoldoutGroup("References", true, nameof(_authService))]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [SerializeField, HideProperty]
        PlayerAuthService _authService;

        [FoldoutGroup(
            "Behaviour",
            true,
            nameof(_enablePollingFallback),
            nameof(_pollingIntervalSeconds),
            nameof(_autoSubscribeRooms)
        )]
        [SerializeField]
        EditorAttributes.Void behaviourGroup;

        [SerializeField, HideProperty, Tooltip(
            "When true (default), falls back to REST polling when realtime WebSocket "
            + "is unavailable (WebGL builds). When false, WebGL will simply skip realtime."
        )]
        bool _enablePollingFallback = true;

        [SerializeField, HideProperty, Suffix("s"), Tooltip(
            "Polling interval when WebSocket is unavailable (WebGL only)."
        )]
        float _pollingIntervalSeconds = 3f;

        [SerializeField, HideProperty, Tooltip(
            "When true, automatically subscribes to room dialogue channels "
            + "when the player joins a multiplayer room (via room_memberships)."
        )]
        bool _autoSubscribeRooms = true;

        [FoldoutGroup("Debug", true, nameof(_lastConnectionState), nameof(_lastError))]
        [SerializeField]
        EditorAttributes.Void debugGroup;

        [SerializeField, HideProperty, ReadOnly]
        string _lastConnectionState = "Disconnected";

        [SerializeField, HideProperty, ReadOnly]
        string _lastError = string.Empty;

        [ShowInInspector]
        string RealtimeEndpointPreview => "ws://localhost:8093/socket";

        // ── Public Events ────────────────────────────────────────────

        /// <summary>
        /// Fired on the Unity main thread when a new dialogue turn is
        /// inserted into <c>dialogue_turns</c> by any client.
        /// </summary>
        public event Action<DialogueTurnRecord> OnDialogueTurnInserted;

        /// <summary>
        /// Fired when the underlying connection state changes.
        /// </summary>
        public event Action<Constants.SocketState> OnConnectionStateChanged;

        // ── State ────────────────────────────────────────────────────

        bool _initialized;
        bool _realtimeSupported;
        string _lastSeenSessionId;
        long _highestSeenTurnId;
        IRealtimeClient<RealtimeSocket, RealtimeChannel> _realtime;
        RealtimeChannel _turnChannel;
        RealtimeChannel _roomChannel;
        RealtimeBroadcast<RoomDialoguePayload> _roomBroadcast;
        SynchronizationContext _mainThreadContext;
        CancellationTokenSource _cts;
        Coroutine _pollingRoutine;

        NPCFlowLogger _logger;

        /// <summary>
        /// Fired when a room dialogue broadcast is received.
        /// </summary>
        public event Action<string, string, string> OnRoomDialogueReceived;

        // ── Lifecycle ──────────────────────────────────────────────

        void Awake()
        {
            _mainThreadContext = SynchronizationContext.Current;
            _logger = NPCFlowLogger.FindOrCreate();

#if UNITY_WEBGL && !UNITY_EDITOR
            _realtimeSupported = false;
            _lastConnectionState = "Polling (WebGL fallback)";
#else
            _realtimeSupported = true;
            _lastConnectionState = "Disconnected";
#endif
        }

        void OnDestroy()
        {
            Disconnect();
        }

        // ── Connection ──────────────────────────────────────────────

        /// <summary>
        /// Connect to Supabase Realtime (or start polling on WebGL).
        /// </summary>
        public async Task ConnectAsync()
        {
            if (_initialized)
                return;

            if (_authService == null || !_authService.IsAuthenticated)
            {
                _lastError = "Cannot connect: no authenticated session.";
                _logger?.Log(
                    NPCFlowStage.AuthSession,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Warning,
                    _lastError,
                    source: nameof(SupabaseRealtimeService)
                );
                return;
            }

            _cts = new CancellationTokenSource();

            if (_realtimeSupported)
                await ConnectWebSocketAsync();
            else if (_enablePollingFallback)
                StartPollingFallback();
            else
                _lastConnectionState = "Skipped (unsupported platform)";

            _initialized = true;
        }

        /// <summary>
        /// Disconnect from Realtime / stop polling. Safe to call multiple
        /// times.
        /// </summary>
        public void Disconnect()
        {
            _initialized = false;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            if (_pollingRoutine != null)
            {
                StopCoroutine(_pollingRoutine);
                _pollingRoutine = null;
            }

            if (_turnChannel != null)
            {
                try
                {
                    _turnChannel.RemovePostgresChangeHandler(
                        PostgresChangesOptions.ListenType.Inserts,
                        HandlePostgresChangeNotification
                    );
                    _realtime?.Remove(_turnChannel);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[{nameof(SupabaseRealtimeService)}] Channel cleanup: {ex.Message}"
                    );
                }
                _turnChannel = null;
            }

            if (_roomChannel != null)
            {
                try
                {
                    _realtime?.Remove(_roomChannel);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[{nameof(SupabaseRealtimeService)}] Room channel cleanup: {ex.Message}"
                    );
                }
                _roomChannel = null;
            }

            if (_realtime != null)
            {
                try
                {
                    _realtime.RemoveStateChangedHandler(OnSocketStateChanged);
                    _realtime.Disconnect();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning(
                        $"[{nameof(SupabaseRealtimeService)}] Disconnect warning: {ex.Message}"
                    );
                }
                _realtime = null;
            }

            _lastSeenSessionId = null;
            _highestSeenTurnId = 0;
            _lastConnectionState = "Disconnected";
        }

        // ── WebSocket Path (non-WebGL) ──────────────────────────────

        async Task ConnectWebSocketAsync()
        {
            var client = _authService.SupabaseClient;
            if (client?.Realtime == null)
            {
                _lastError = "SupabaseClient has no Realtime instance.";
                SetConnectionState(Supabase.Realtime.Constants.SocketState.Error);
                return;
            }

            _realtime = client.Realtime;

            if (!string.IsNullOrWhiteSpace(_authService.CurrentSession?.sessionToken))
            {
                _realtime.SetAuth(_authService.CurrentSession.sessionToken);
            }

            _realtime.AddStateChangedHandler(OnSocketStateChanged);

            try
            {
                SetConnectionState(Supabase.Realtime.Constants.SocketState.Reconnect);
                await _realtime.ConnectAsync();
                SubscribeToDialogueTurns();

                _logger?.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    "Realtime WebSocket connected.",
                    source: nameof(SupabaseRealtimeService)
                );
            }
            catch (Exception ex)
            {
                _lastError = $"Realtime connect failed: {ex.Message}";
                SetConnectionState(Supabase.Realtime.Constants.SocketState.Error);

                _logger?.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Warning,
                    $"Realtime WebSocket connect failed: {ex.Message}",
                    source: nameof(SupabaseRealtimeService),
                    data: new Dictionary<string, object>
                    {
                        ["exceptionType"] = ex.GetType().Name,
                    }
                );

                if (_enablePollingFallback)
                    StartPollingFallback();
            }
        }

        void SubscribeToDialogueTurns()
        {
            if (_realtime == null)
                return;

            try
            {
                // Create a channel for the dialogue_turns table
                _turnChannel = _realtime.Channel(
                    "realtime",
                    "public",
                    "dialogue_turns",
                    null, null, null
                );

                // Register postgres_changes on this channel
                var options = new PostgresChangesOptions(
                    "public",
                    "dialogue_turns",
                    PostgresChangesOptions.ListenType.Inserts
                );
                _turnChannel.Register(options);
                _turnChannel.AddPostgresChangeHandler(
                    PostgresChangesOptions.ListenType.Inserts,
                    HandlePostgresChangeNotification
                );

                _logger?.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Debug,
                    "Realtime subscribed to dialogue_turns inserts.",
                    source: nameof(SupabaseRealtimeService)
                );
            }
            catch (Exception ex)
            {
                _lastError = $"Failed to subscribe to dialogue_turns: {ex.Message}";
                Debug.LogWarning($"[{nameof(SupabaseRealtimeService)}] {_lastError}");
            }
        }

        void HandlePostgresChangeNotification(IRealtimeChannel sender, PostgresChangesResponse response)
        {
            if (_mainThreadContext != null)
            {
                _mainThreadContext.Post(_ => DispatchTurnFromResponse(response), null);
            }
            else
            {
                DispatchTurnFromResponse(response);
            }
        }

        void DispatchTurnFromResponse(PostgresChangesResponse response)
        {
            try
            {
                var turn = response.Model<DialogueTurnRecord>();
                if (turn == null)
                    return;

                if (long.TryParse(turn.Id, out long parsedId))
                    _highestSeenTurnId = Math.Max(_highestSeenTurnId, parsedId);

                _logger?.Log(
                    NPCFlowStage.HistoryPersist,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Debug,
                    $"Realtime: new dialogue turn (role={turn.Role})",
                    source: nameof(SupabaseRealtimeService),
                    data: new Dictionary<string, object>
                    {
                        ["sessionId"] = turn.SessionId ?? string.Empty,
                        ["role"] = turn.Role ?? string.Empty,
                    }
                );

                OnDialogueTurnInserted?.Invoke(turn);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[{nameof(SupabaseRealtimeService)}] Failed to parse turn: {ex.Message}"
                );
            }
        }

        // ── WebGL Fallback: REST Polling ───────────────────────────

        void StartPollingFallback()
        {
            if (_pollingRoutine != null)
                StopCoroutine(_pollingRoutine);

            _pollingRoutine = StartCoroutine(PollDialogueTurnsRoutine());

            _logger?.Log(
                NPCFlowStage.ConfigurationValidation,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Realtime polling started (interval={_pollingIntervalSeconds}s).",
                source: nameof(SupabaseRealtimeService)
            );
        }

        IEnumerator PollDialogueTurnsRoutine()
        {
            var wait = new WaitForSeconds(_pollingIntervalSeconds);

            while (!_cts.IsCancellationRequested)
            {
                yield return wait;

                if (_authService == null || !_authService.IsAuthenticated)
                    continue;

                var client = _authService.SupabaseClient;
                if (client == null)
                    continue;

                var task = FetchLatestTurnsAsync(client);
                yield return new WaitUntil(() => task.IsCompleted);

                if (task.Exception != null)
                {
                    _lastError = $"Poll failed: {task.Exception.InnerException?.Message}";
                    continue;
                }

                List<DialogueTurnRecord> newTurns = task.Result;
                foreach (DialogueTurnRecord turn in newTurns)
                {
                    OnDialogueTurnInserted?.Invoke(turn);
                }
            }

            _pollingRoutine = null;
        }

        async Task<List<DialogueTurnRecord>> FetchLatestTurnsAsync(Supabase.Client client)
        {
            if (string.IsNullOrWhiteSpace(_lastSeenSessionId))
                return new List<DialogueTurnRecord>();

            try
            {
                var response = await client
                    .From<DialogueTurnRecord>()
                    .Filter("session_id", Postgrest.Constants.Operator.Equals, _lastSeenSessionId)
                    .Filter("id", Postgrest.Constants.Operator.GreaterThan, _highestSeenTurnId)
                    .Order("id", Postgrest.Constants.Ordering.Ascending)
                    .Get();

                if (response.Models == null || response.Models.Count == 0)
                    return new List<DialogueTurnRecord>();

                foreach (var turn in response.Models)
                {
                    if (turn.Id != null)
                    {
                        long id = long.TryParse(turn.Id, out long parsed) ? parsed : 0;
                        _highestSeenTurnId = Math.Max(_highestSeenTurnId, id);
                    }
                }

                return response.Models;
            }
            catch (PostgrestException ex) when (ex.Message.Contains("404"))
            {
                return new List<DialogueTurnRecord>();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[{nameof(SupabaseRealtimeService)}] Poll fetch failed: {ex.Message}"
                );
                return new List<DialogueTurnRecord>();
            }
        }

        // ── Session Tracking ────────────────────────────────────────

        /// <summary>
        /// Sets the currently active dialogue session ID so that the
        /// polling fallback knows which session's turns to track.
        /// Called by NPCDialogueSessionService on session start.
        /// </summary>
        public void SetActiveSession(string sessionId)
        {
            _lastSeenSessionId = sessionId;
            _highestSeenTurnId = 0;
        }

        /// <summary>
        /// Clears the active session when dialogue ends.
        /// </summary>
        public void ClearActiveSession()
        {
            _lastSeenSessionId = null;
            _highestSeenTurnId = 0;
        }

        // ── Room Broadcast Channels (Phase 6: Multiplayer) ────────

        /// <summary>
        /// Subscribe to a room's dialogue broadcast channel.
        /// All players in this room will receive NPC dialogue events
        /// published to <c>room:{roomCode}</c>.
        /// </summary>
        public async Task SubscribeToRoomChannelAsync(string roomCode)
        {
            if (!_realtimeSupported || _realtime == null || string.IsNullOrWhiteSpace(roomCode))
                return;

            // Unsubscribe from any previous room first
            if (_roomChannel != null)
            {
                try
                {
                    _realtime.Remove(_roomChannel);
                }
                catch { /* ignore */ }
                _roomChannel = null;
                _roomBroadcast = null;
            }

            try
            {
                string channelName = $"room:{roomCode}";
                _roomChannel = _realtime.Channel(
                    channelName,
                    null, null, null, null, null
                );

                // Register for broadcast events
                _roomBroadcast = _roomChannel.Register<RoomDialoguePayload>(false, false);
                _roomBroadcast.AddBroadcastEventHandler(OnRoomBroadcastReceived);

                await _roomChannel.Subscribe();

                _logger?.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Debug,
                    $"Joined room broadcast channel: {channelName}",
                    source: nameof(SupabaseRealtimeService),
                    data: new Dictionary<string, object>
                    {
                        ["roomCode"] = roomCode,
                        ["channelName"] = channelName,
                    }
                );
            }
            catch (Exception ex)
            {
                _logger?.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Warning,
                    $"Failed to subscribe to room channel '{roomCode}': {ex.Message}",
                    source: nameof(SupabaseRealtimeService)
                );
            }
        }

        /// <summary>
        /// Unsubscribe from the current room broadcast channel.
        /// </summary>
        public void UnsubscribeFromRoomChannel()
        {
            if (_roomChannel == null)
                return;

            try
            {
                if (_roomBroadcast != null)
                {
                    _roomBroadcast.RemoveBroadcastEventHandler(OnRoomBroadcastReceived);
                    _roomBroadcast = null;
                }
                _realtime?.Remove(_roomChannel);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[{nameof(SupabaseRealtimeService)}] Room unsubscribe: {ex.Message}"
                );
            }
            _roomChannel = null;

            _logger?.Log(
                NPCFlowStage.ConfigurationValidation,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Debug,
                "Left room broadcast channel.",
                source: nameof(SupabaseRealtimeService)
            );
        }

        void OnRoomBroadcastReceived(IRealtimeBroadcast sender, BaseBroadcast broadcast)
        {
            // Route broadcast events to main thread
            if (_mainThreadContext != null && broadcast is RoomDialoguePayload payload)
            {
                _mainThreadContext.Post(_ =>
                {
                    OnRoomDialogueReceived?.Invoke(
                        payload.NpcSlug,
                        payload.DialogueMessage,
                        payload.PlayerName
                    );
                }, null);
            }
        }

        /// <summary>
        /// Broadcast payload for room dialogue events.
        /// </summary>
        public class RoomDialoguePayload : BaseBroadcast
        {
            [Newtonsoft.Json.JsonProperty("npcSlug")]
            public string NpcSlug;

            [Newtonsoft.Json.JsonProperty("dialogueMessage")]
            public string DialogueMessage;

            [Newtonsoft.Json.JsonProperty("playerName")]
            public string PlayerName;

            [Newtonsoft.Json.JsonProperty("sessionId")]
            public string SessionId;

            [Newtonsoft.Json.JsonProperty("roomCode")]
            public string RoomCode;
        }

        // ── Socket State Handling ───────────────────────────────────

        void OnSocketStateChanged(
            IRealtimeClient<RealtimeSocket, RealtimeChannel> sender,
            Constants.SocketState state
        )
        {
            if (_mainThreadContext != null)
            {
                _mainThreadContext.Post(_ => { SetConnectionState(state); }, null);
            }
        }

        void SetConnectionState(Constants.SocketState state)
        {
            switch (state)
            {
                case Constants.SocketState.Open:
                    _lastConnectionState = "Connected";
                    break;
                case Constants.SocketState.Close:
                    _lastConnectionState = "Disconnected";
                    break;
                case Constants.SocketState.Reconnect:
                    _lastConnectionState = "Connecting";
                    break;
                case Constants.SocketState.Error:
                    _lastConnectionState = "Error";
                    break;
                default:
                    _lastConnectionState = state.ToString();
                    break;
            }

            OnConnectionStateChanged?.Invoke(state);
        }

        // ── Editor Validation ───────────────────────────────────────

        [Button("Validate Realtime Service Settings")]
        void ValidateSettings()
        {
            bool hasAuth = _authService != null;
            _lastConnectionState = hasAuth
                ? "Settings valid. Connect after auth."
                : "Missing Auth Service reference.";
            _lastError = string.Empty;

            _logger?.Log(
                NPCFlowStage.ConfigurationValidation,
                hasAuth ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                NPCFlowLogLevel.Info,
                _lastConnectionState,
                source: nameof(SupabaseRealtimeService)
            );
        }

        void Reset()
        {
            _authService = GetComponent<PlayerAuthService>();
            if (_authService == null)
                _authService = FindAnyObjectByType<PlayerAuthService>(
                    FindObjectsInactive.Include
                );
        }

        void OnValidate()
        {
            if (!Application.isPlaying && _authService == null)
            {
                _authService = GetComponent<PlayerAuthService>();
                if (_authService == null)
                    _authService = FindAnyObjectByType<PlayerAuthService>(
                        FindObjectsInactive.Include
                    );
            }
        }
    }
}
