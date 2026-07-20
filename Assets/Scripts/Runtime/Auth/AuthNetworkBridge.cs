using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EditorAttributes;
using Unity.Multiplayer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;


using NPCSystem.Monitoring;
using NPCSystem.Monitoring.Datadog;
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
namespace NPCSystem.Auth
{
    /// <summary>
    /// Bridges successful player authentication to network start and player name registration.
    /// After auth succeeds: closes the auth UI, starts a NetworkManager (host or client),
    /// then sets the authenticated player name on the local NPCPlayerNetworkAvatar's NetworkVariable.
    ///
    /// Modes:
    ///   _startAsHost = true  → StartHost() after auth   (first player / listen-server)
    ///   _startAsHost = false → StartClient() after auth  (late-joining player)
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class AuthNetworkBridge : MonoBehaviour
    {
        public enum ResolvedNetworkStartupMode
        {
            Host,
            Client,
        }

        [Title("Auth Network Bridge")]
        [HelpBox(
            "Bridges client-side auth success into NGO startup and player-name replication. This component owns login-to-spawn transition, not backend dialogue generation.",
            MessageMode.Log,
            drawAbove: true
        )]
        [Header("References")]
        [FormerlySerializedAs("AuthController")]
        [SerializeField]
        AuthUIController _authController;

        /// <summary>Public accessor (used by tests).</summary>
        public AuthUIController AuthController
        {
            get => _authController;
            set => _authController = value;
        }

        [FormerlySerializedAs("NetworkBootstrap")]
        [SerializeField]
        NPCNetworkBootstrap _networkBootstrap;

        /// <summary>Public accessor (used by tests).</summary>
        public NPCNetworkBootstrap NetworkBootstrap
        {
            get => _networkBootstrap;
            set => _networkBootstrap = value;
        }

        [SerializeField]
        WebGLGameplayLoadController _gameplayLoadController;

        [Header("Mode")]
        [Tooltip(
            "Dedicated-server projects should keep this disabled so auth always starts a client unless an explicit CLI override is supplied. Enable only for legacy listen-server/Multiplayer Play Mode host tests."
        )]
        [FormerlySerializedAs("AutoDetectStartupMode")]
        [SerializeField]
        bool _autoDetectStartupMode = false;

        /// <summary>Public accessor for _autoDetectStartupMode (used by tests).</summary>
        public bool AutoDetectStartupMode
        {
            get => _autoDetectStartupMode;
            set => _autoDetectStartupMode = value;
        }

        [Tooltip(
            "True by default for single-player listen-server: auth starts StartHost() immediately (no client connect timeout). Set false for dedicated-server flow where the player connects as a client."
        )]
        [FormerlySerializedAs("StartAsHost")]
        [SerializeField]
        bool _startAsHost = true;

        /// <summary>Public accessor for _startAsHost (used by tests).</summary>
        public bool StartAsHost
        {
            get => _startAsHost;
            set => _startAsHost = value;
        }

        [Tooltip("Host address to connect to when _startAsHost is false.")]
        [FormerlySerializedAs("HostAddress")]
        [SerializeField]
        string _hostAddress = "127.0.0.1";

        [Tooltip(
            "Host port to connect to when _startAsHost is false. 0 = use bootstrap's configured port."
        )]
        [FormerlySerializedAs("HostPort")]
        [SerializeField]
        ushort _hostPort = 0;

        [Header("Events")]
        [SerializeField]
        UnityEngine.Events.UnityEvent<string> _onHostStarted =
            new UnityEngine.Events.UnityEvent<string>();

        string _authenticatedPlayerName = "";
        NPCFlowLogger _logger;

        [SerializeField, ReadOnly]
        string lastBridgeStatus = "Idle";

        // ── Connection reliability tracking ──
        [SerializeField, ReadOnly]
        string _authFlowId;
        [SerializeField, ReadOnly]
        bool _authConnected;
        [SerializeField, ReadOnly]
        bool _authFallbackAttempted;
        [Tooltip("How long to wait for client connection before attempting fallback (ms). Default: 15000")]
        [SerializeField]
        int _connectionTimeoutMs = 15000;

        public string PlayerName => _authenticatedPlayerName;

        [ShowInInspector]
        string ResolvedModePreview => ResolveStartupMode().ToString();

        [ShowInInspector]
        string HostEndpointPreview => $"{_hostAddress}:{_hostPort}";

        /// <summary>
        /// Static accessor for the active player name (read by NPCDialogueManager when building prompts).
        /// Also used by NPCPlayerNetworkAvatar to auto-register on client spawn.
        /// </summary>
        public static string ActivePlayerName { get; internal set; } = "Player";

        void Reset()
        {
            ResolveReferences();
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ResolveReferences();
            }
        }

        void Awake()
        {
            ResolveReferences();
        }

        void Start()
        {
            _logger = NPCFlowLogger.FindOrCreate();
#if !UNITY_SERVER
            BindAuthEvents();
#endif
        }

        void OnDestroy()
        {
#if !UNITY_SERVER
            UnbindAuthEvents();
#endif
        }

        void ResolveReferences()
        {
#if !UNITY_SERVER
            if (_authController == null)
                _authController = GetComponent<AuthUIController>();

            if (_authController == null)
                _authController = FindAnyObjectByType<AuthUIController>(
                    FindObjectsInactive.Include
                );
#endif
            if (_networkBootstrap == null)
                _networkBootstrap = FindAnyObjectByType<NPCNetworkBootstrap>(
                    FindObjectsInactive.Include
                );

            if (_gameplayLoadController == null)
                _gameplayLoadController = FindAnyObjectByType<WebGLGameplayLoadController>(
                    FindObjectsInactive.Include
                );
        }

#if !UNITY_SERVER
        void BindAuthEvents()
        {
            ResolveReferences();

            if (_authController == null)
            {
                _logger?.Log(
                    NPCFlowStage.ConfigurationValidation,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "AuthNetworkBridge cannot bind auth events because Auth Controller is not assigned and no AuthUIController was found.",
                    source: nameof(AuthNetworkBridge),
                    data: new Dictionary<string, object>
                    {
                        ["gameObject"] = gameObject.name,
                        ["expectedComponent"] = nameof(AuthUIController),
                    }
                );
                lastBridgeStatus =
                    "Missing Auth Controller reference; auth success cannot start networking.";
                return;
            }

            _authController.events.onLoginSuccess.AddListener(HandleAuthSuccess);
            _authController.events.onRegisterSuccess.AddListener(HandleAuthSuccess);

            _logger?.Log(
                NPCFlowStage.ConfigurationValidation,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Debug,
                "AuthNetworkBridge bound AuthUIController success events.",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["_authController"] = _authController.name,
                    ["_networkBootstrap"] =
                        _networkBootstrap != null ? _networkBootstrap.name : "<missing>",
                }
            );
        }

        void UnbindAuthEvents()
        {
            if (_authController == null)
                return;
            _authController.events.onLoginSuccess.RemoveListener(HandleAuthSuccess);
            _authController.events.onRegisterSuccess.RemoveListener(HandleAuthSuccess);
        }
#endif

        async void HandleAuthSuccess(string username)
        {
            try
            {
                await HandleAuthSuccessAsync(username);
            }
            catch (Exception ex)
            {
                _logger?.Log(
                    NPCFlowStage.AuthSession,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[{_authFlowId ?? "?"}] Auth success handler threw: {ex.Message}",
                    source: nameof(AuthNetworkBridge),
                    data: new Dictionary<string, object>
                    {
                        ["flowId"] = _authFlowId ?? "?",
                        ["exception"] = ex.ToString(),
                    }
                );
                lastBridgeStatus = $"Auth handler crashed: {ex.Message}";
                DatadogMetricsService.Increment(
                    "auth.handler.crash",
                    tags: new[] { $"flow_id:{_authFlowId ?? "?"}" }
                );
            }
        }

        async Task HandleAuthSuccessAsync(string username)
        {
            // Generate a unique flow ID so every auth attempt is traceable end-to-end
            _authFlowId = Guid.NewGuid().ToString("N")[..8];
            _authConnected = false;
            _authFallbackAttempted = false;

            using var authSpan = DatadogTracer.StartSpan(
                "auth.login",
                service: "unity-dedicated-server",
                resource: "PlayerAuth",
                type: "auth",
                tags: new[] { $"player_name:{username?.Trim() ?? "unknown"}", $"flow_id:{_authFlowId}" }
            );

#if UNITY_WEBGL && !UNITY_EDITOR
            if (
                string.IsNullOrWhiteSpace(_hostAddress)
                || NPCNetworkUtils.IsLocalHost(_hostAddress)
            )
            {
                try
                {
                    Uri uri = new Uri(Application.absoluteURL);
                    if (!NPCNetworkUtils.IsLocalHost(uri.Host))
                    {
                        _hostAddress = uri.Host;
                    }
                }
                catch { }
            }
#endif

            _authenticatedPlayerName = username?.Trim() ?? "";
            ActivePlayerName = _authenticatedPlayerName;
            ResolvedNetworkStartupMode resolvedMode = ResolveStartupMode();

            authSpan.SetTag("mode", resolvedMode.ToString().ToLowerInvariant());

            CloseAuthUI();

            DatadogMetricsService.Increment(
                "auth.login.count",
                tags: new[] { $"mode:{resolvedMode.ToString().ToLowerInvariant()}", $"flow_id:{_authFlowId}" }
            );

            _logger?.Log(
                NPCFlowStage.UIInput,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"[{_authFlowId}] Auth success for '{_authenticatedPlayerName}'. Starting network (mode: {resolvedMode.ToString().ToLowerInvariant()}, fallback: enabled)...",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["flowId"] = _authFlowId,
                    ["playerName"] = _authenticatedPlayerName,
                    ["resolvedMode"] = resolvedMode.ToString(),
                    ["fallbackEnabled"] = resolvedMode == ResolvedNetworkStartupMode.Client,
                }
            );
            lastBridgeStatus =
                $"[{_authFlowId}] Auth ok for {_authenticatedPlayerName}; mode={resolvedMode}";

            await PrepareGameplayAsync();

            // Register connection tracking before starting (detached from bootstrap callbacks)
            RegisterAuthCallbacks();

            if (resolvedMode == ResolvedNetworkStartupMode.Host)
            {
                StartHostAndRegisterPlayerName();
            }
            else
            {
                StartClientAndRegisterPlayerName();
                // Fire-and-forget watcher: monitors connection, triggers Host fallback on timeout
                _ = StartConnectionWatcherAsync();
            }
        }

        async System.Threading.Tasks.Task PrepareGameplayAsync()
        {
            ResolveReferences();
            if (_gameplayLoadController != null)
            {
                _logger?.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Info,
                    "Preparing gameplay systems after authentication.",
                    source: nameof(AuthNetworkBridge)
                );
                await _gameplayLoadController.PrepareGameplayAsync();
                return;
            }

#if !UNITY_SERVER
            var uiController = FindAnyObjectByType<NPCDialogueUIController>(
                FindObjectsInactive.Include
            );
            if (uiController != null)
            {
                GameObject gameplayCanvas = uiController.GetGameplayCanvas();
                if (gameplayCanvas != null)
                {
                    gameplayCanvas.SetActive(true);
                }

                _logger?.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Info,
                    "Triggering on-demand dialogue UI and backend system initialization.",
                    source: nameof(AuthNetworkBridge)
                );
                await uiController.InitializeOnDemandAsync();
                return;
            }
#endif

            var manager = FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
            if (manager != null)
            {
                _logger?.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Start,
                    NPCFlowLogLevel.Info,
                    "Triggering on-demand backend dialogue system initialization.",
                    source: nameof(AuthNetworkBridge)
                );
                await manager.InitializeAsync();
            }
        }

        ResolvedNetworkStartupMode ResolveStartupMode()
        {
            if (TryGetCommandLineStartupMode(out ResolvedNetworkStartupMode commandLineMode))
            {
                return commandLineMode;
            }

            if (NPCPlayModeInstanceResolver.TryGetPlayerIndex(out int playerIndex))
            {
                return playerIndex <= 1
                    ? ResolvedNetworkStartupMode.Host
                    : ResolvedNetworkStartupMode.Client;
            }

            if (_autoDetectStartupMode)
            {
                MultiplayerRoleFlags roleMask = MultiplayerRolesManager.ActiveMultiplayerRoleMask;
                if (roleMask == MultiplayerRoleFlags.Client)
                {
                    return ResolvedNetworkStartupMode.Client;
                }

                if (
                    roleMask == MultiplayerRoleFlags.Server
                    || roleMask == MultiplayerRoleFlags.ClientAndServer
                )
                {
                    return ResolvedNetworkStartupMode.Host;
                }
            }

            return _startAsHost
                ? ResolvedNetworkStartupMode.Host
                : ResolvedNetworkStartupMode.Client;
        }

        public static bool TryGetCommandLineStartupMode(out ResolvedNetworkStartupMode mode)
        {
            string[] args = Environment.GetCommandLineArgs();
            foreach (string arg in args)
            {
                if (string.Equals(arg, "-npc-client", StringComparison.OrdinalIgnoreCase))
                {
                    mode = ResolvedNetworkStartupMode.Client;
                    return true;
                }

                if (string.Equals(arg, "-npc-host", StringComparison.OrdinalIgnoreCase))
                {
                    mode = ResolvedNetworkStartupMode.Host;
                    return true;
                }

                if (string.Equals(arg, "-npc-server", StringComparison.OrdinalIgnoreCase))
                {
                    mode = ResolvedNetworkStartupMode.Host;
                    return true;
                }
            }

            mode = ResolvedNetworkStartupMode.Host;
            return false;
        }

        void CloseAuthUI()
        {
#if !UNITY_SERVER
            if (_authController != null)
            {
                _authController.ClosePanel();
                return;
            }
#endif

            GameObject panel = GameObject.Find("AuthPanel");
            if (panel == null)
            {
                panel = GameObject.Find("Canvas/AuthPanel");
            }

            if (panel != null)
            {
                panel.SetActive(false);
                return;
            }

            GameObject authUI = GameObject.Find("AuthUI");
            if (authUI != null)
            {
                authUI.SetActive(false);
            }
        }

        // ── Connection reliability ─────────────────────────────────

        /// <summary>
        /// Register temporary callbacks on NetworkManager to track whether
        /// the client connection succeeded. These are cleaned up after
        /// first connect/disconnect or when fallback fires.
        /// </summary>
        void RegisterAuthCallbacks()
        {
            NetworkManager nm = GetNetworkManager();
            if (nm == null) return;
            // Unregister first to avoid double-subscription from retry paths
            nm.OnClientConnectedCallback -= OnAuthClientConnected;
            nm.OnClientDisconnectCallback -= OnAuthClientDisconnected;
            nm.OnClientConnectedCallback += OnAuthClientConnected;
            nm.OnClientDisconnectCallback += OnAuthClientDisconnected;
        }

        void UnregisterAuthCallbacks()
        {
            NetworkManager nm = GetNetworkManager();
            if (nm == null) return;
            nm.OnClientConnectedCallback -= OnAuthClientConnected;
            nm.OnClientDisconnectCallback -= OnAuthClientDisconnected;
        }

        void OnAuthClientConnected(ulong clientId)
        {
            if (!_authFallbackAttempted)
            {
                _authConnected = true;
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    $"[{_authFlowId ?? "?"}] Client connected to server (clientId={clientId}).",
                    source: nameof(AuthNetworkBridge),
                    data: new Dictionary<string, object> { ["flowId"] = _authFlowId ?? "?", ["clientId"] = clientId }
                );
            }
            // Keep callbacks registered for full session — bootstrap handles disconnect logging
        }

        void OnAuthClientDisconnected(ulong clientId)
        {
            if (_authFallbackAttempted || _authConnected)
                return;

            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Warning,
                NPCFlowLogLevel.Info,
                $"[{_authFlowId ?? "?"}] Client disconnected before establishing connection (clientId={clientId}).",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object> { ["flowId"] = _authFlowId ?? "?", ["clientId"] = clientId }
            );

            // Don't trigger fallback here — let StartConnectionWatcherAsync handle it
            // with a clear timeout. This avoids racing against the 60s Unity internal timeout.
        }

        /// <summary>
        /// Polls for connection success up to _connectionTimeoutMs.
        /// If no connection is established within the window, triggers a Host fallback.
        /// Only meaningful in Client mode.
        /// </summary>
        async Task StartConnectionWatcherAsync()
        {
            if (_authConnected || _authFallbackAttempted || _connectionTimeoutMs <= 0)
                return;

            int timeoutMs = _connectionTimeoutMs;
            int waited = 0;
            int intervalMs = Mathf.Min(200, timeoutMs / 10);

            while (waited < timeoutMs && !_authConnected && !_authFallbackAttempted)
            {
                await System.Threading.Tasks.Task.Delay(intervalMs);
                waited += intervalMs;
            }

            if (!_authConnected && !_authFallbackAttempted)
            {
                await TryFallbackToHostAsync();
            }
        }

        /// <summary>
        /// Fallback: shut down the failed client attempt, then start as Host.
        /// Only fires once per auth flow.
        /// </summary>
        async Task TryFallbackToHostAsync()
        {
            if (_authFallbackAttempted) return;
            _authFallbackAttempted = true;

            string flowId = _authFlowId ?? Guid.NewGuid().ToString("N")[..8];

            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Fallback,
                NPCFlowLogLevel.Warning,
                $"[{flowId}] Client connection failed or timed out after {_connectionTimeoutMs}ms. Falling back to Host mode.",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["flowId"] = flowId,
                    ["timeoutMs"] = _connectionTimeoutMs,
                    ["fallback"] = true,
                    ["playerName"] = _authenticatedPlayerName,
                }
            );

            DatadogMetricsService.Increment(
                "auth.fallback.to_host",
                tags: new[] { $"flow_id:{flowId}" }
            );
            lastBridgeStatus = $"[{flowId}] Client failed; falling back to Host";

            UnregisterAuthCallbacks();

            // Clean shutdown of stale client connection
            NetworkManager netManager = GetNetworkManager();
            if (netManager != null && netManager.IsListening)
            {
                netManager.Shutdown();
                // Let shutdown propagate before re-starting
                await System.Threading.Tasks.Task.Delay(500);
            }

            // Reset connection flag for host attempt
            _authConnected = false;

            // Configure for Host mode
            if (_networkBootstrap != null)
            {
                _networkBootstrap.TransportConfig.AutoStartMode = NPCNetworkAutoStartMode.Host;
                _networkBootstrap.TransportConfig.ConnectAddress = "0.0.0.0";
            }

            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Start,
                NPCFlowLogLevel.Info,
                $"[{flowId}] Attempting Host start after client fallback.",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object> { ["flowId"] = flowId }
            );

            // Re-register callbacks for Host mode
            RegisterAuthCallbacks();
            StartHostAndRegisterPlayerName();
        }

        // ── Host mode ─────────────────────────────────────────────

        async void StartHostAndRegisterPlayerName()
        {
            if (_networkBootstrap == null)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[{_authFlowId ?? "?"}] Cannot start host: NPCNetworkBootstrap not found.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = $"[{_authFlowId ?? "?"}] Cannot start host: NPCNetworkBootstrap not found.";
                return;
            }

            NetworkManager netManager = _networkBootstrap.NetworkManager;
            if (netManager == null)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[{_authFlowId ?? "?"}] Cannot start host: NetworkManager not found via bootstrap.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = $"[{_authFlowId ?? "?"}] Cannot start host: NetworkManager not found.";
                return;
            }

            if (netManager.IsListening)
            {
                SetPlayerNameOnLocalAvatar(netManager);
                return;
            }

            // Bootstrap already configured transport in Awake; just set host mode
            _networkBootstrap.TransportConfig.AutoStartMode = NPCNetworkAutoStartMode.Host;
            bool started = _networkBootstrap.StartConfiguredMode();
            if (!started)
            {
                NPCTransportConfig cfg = _networkBootstrap.TransportConfig;
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"Failed to start host on port {cfg.Port} via bootstrap.",
                    source: nameof(AuthNetworkBridge),
                    data: new Dictionary<string, object>
                    {
                        ["_hostAddress"] = cfg.ConnectAddress ?? "unknown",
                        ["_hostPort"] = cfg.Port,
                        ["listenAddress"] = cfg.ListenAddress ?? "unknown",
                    }
                );
                lastBridgeStatus = $"Failed to start host on {cfg.ConnectAddress}:{cfg.Port}.";
                return;
            }

            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"[{_authFlowId ?? "?"}] Host started via bootstrap. Waiting for local player object...",
                source: nameof(AuthNetworkBridge)
            );
            lastBridgeStatus = $"[{_authFlowId ?? "?"}] Host started. Waiting for local player object.";

            _authConnected = true;
            UnregisterAuthCallbacks();

            // Wait for player object to spawn
            int attempts = 0;
            while (attempts < 100)
            {
                if (
                    netManager.IsListening
                    && netManager.LocalClient != null
                    && netManager.LocalClient.PlayerObject != null
                    && netManager.LocalClient.PlayerObject.gameObject != null
                )
                {
                    SetPlayerNameOnLocalAvatar(netManager);
                    _onHostStarted?.Invoke(_authenticatedPlayerName);
                    return;
                }
                await System.Threading.Tasks.Task.Yield();
                attempts++;
            }

            _logger?.Log(
                NPCFlowStage.PlayerNameRegistration,
                NPCFlowStatus.Warning,
                NPCFlowLogLevel.Warning,
                "Player object did not spawn within timeout (host). Name set via OnNetworkSpawn.",
                source: nameof(AuthNetworkBridge)
            );
            lastBridgeStatus =
                "Host player object spawn timed out; fallback to OnNetworkSpawn registration.";
        }

        // ── Client mode ───────────────────────────────────────────

        void StartClientAndRegisterPlayerName()
        {
            if (_networkBootstrap == null)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[{_authFlowId ?? "?"}] Cannot connect as client: NPCNetworkBootstrap not found.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = $"[{_authFlowId ?? "?"}] Cannot connect as client: NPCNetworkBootstrap not found.";
                return;
            }

            NetworkManager netManager = _networkBootstrap.NetworkManager;
            if (netManager == null)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[{_authFlowId ?? "?"}] Cannot connect as client: NetworkManager not found via bootstrap.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = $"[{_authFlowId ?? "?"}] Cannot connect as client: NetworkManager not found.";
                return;
            }

            if (netManager.IsListening)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Info,
                    $"[{_authFlowId ?? "?"}] Network already listening. Ignoring client start request.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = $"[{_authFlowId ?? "?"}] Network already listening; skipped duplicate client start.";
                return;
            }

            // Override bootstrap's connect address if specified, then delegate to bootstrap
            _networkBootstrap.TransportConfig.ConnectAddress = string.IsNullOrWhiteSpace(
                _hostAddress
            )
                ? "127.0.0.1"
                : _hostAddress.Trim();
            if (_hostPort > 0)
                _networkBootstrap.TransportConfig.Port = _hostPort;

            _networkBootstrap.TransportConfig.AutoStartMode = NPCNetworkAutoStartMode.Client;
            bool started = _networkBootstrap.StartConfiguredMode();
            if (!started)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"[{_authFlowId ?? "?"}] Failed to start client via bootstrap.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = $"[{_authFlowId ?? "?"}] Failed to start client to {_hostAddress}:{_hostPort}.";
                return;
            }

            string effectiveAddress = _networkBootstrap.TransportConfig.ConnectAddress;
            ushort effectivePort = _networkBootstrap.TransportConfig.Port;
            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"[{_authFlowId ?? "?"}] Client started, connecting to {effectiveAddress}:{effectivePort}...",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["flowId"] = _authFlowId ?? "?",
                    ["hostAddress"] = effectiveAddress,
                    ["hostPort"] = effectivePort,
                }
            );
            lastBridgeStatus =
                $"[{_authFlowId ?? "?"}] Client started to {effectiveAddress}:{effectivePort} as {_authenticatedPlayerName}.";

            // Name will be registered automatically by NPCPlayerNetworkAvatar.OnNetworkSpawn
            // which reads AuthNetworkBridge.ActivePlayerName and calls RegisterPlayerNameServerRpc.
            _onHostStarted?.Invoke(_authenticatedPlayerName);
        }

        // ── Shared ────────────────────────────────────────────────

        void SetPlayerNameOnLocalAvatar(NetworkManager netManager)
        {
            if (netManager == null || netManager.LocalClient == null)
                return;

            GameObject playerObj =
                netManager.LocalClient.PlayerObject != null
                    ? netManager.LocalClient.PlayerObject.gameObject
                    : null;
            if (playerObj == null)
            {
                _logger?.Log(
                    NPCFlowStage.PlayerNameRegistration,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    "Local client has no PlayerObject.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = "Local client has no PlayerObject.";
                return;
            }

            var avatar = playerObj.GetComponent<NPCPlayerNetworkAvatar>();
            if (avatar != null)
            {
                avatar.SetDisplayName(_authenticatedPlayerName);
                _logger?.Log(
                    NPCFlowStage.PlayerNameRegistration,
                    NPCFlowStatus.Success,
                    NPCFlowLogLevel.Info,
                    $"Player name '{_authenticatedPlayerName}' set on {playerObj.name}.",
                    source: nameof(AuthNetworkBridge),
                    data: new Dictionary<string, object>
                    {
                        ["playerName"] = _authenticatedPlayerName,
                        ["playerObjectName"] = playerObj.name,
                    }
                );
                lastBridgeStatus =
                    $"Player name {_authenticatedPlayerName} set on {playerObj.name}.";
            }
            else
            {
                _logger?.Log(
                    NPCFlowStage.PlayerNameRegistration,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    $"No NPCPlayerNetworkAvatar found on {playerObj.name}.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = $"No NPCPlayerNetworkAvatar found on {playerObj.name}.";
            }
        }

        NetworkManager GetNetworkManager()
        {
            if (_networkBootstrap != null && _networkBootstrap.NetworkManager != null)
                return _networkBootstrap.NetworkManager;
            return FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        }
    }
}
