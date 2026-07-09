using System;
using System.Collections.Generic;
using EditorAttributes;
using Unity.Multiplayer;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

namespace NPCSystem
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
        public AuthUIController AuthController { get => _authController; set => _authController = value; }
        [FormerlySerializedAs("NetworkBootstrap")]
        [SerializeField]
        NPCNetworkBootstrap _networkBootstrap;
        /// <summary>Public accessor (used by tests).</summary>
        public NPCNetworkBootstrap NetworkBootstrap { get => _networkBootstrap; set => _networkBootstrap = value; }

        [Header("Mode")]
        [Tooltip(
            "Dedicated-server projects should keep this disabled so auth always starts a client unless an explicit CLI override is supplied. Enable only for legacy listen-server/Multiplayer Play Mode host tests."
        )]
        [FormerlySerializedAs("AutoDetectStartupMode")]
        [SerializeField]
        bool _autoDetectStartupMode = false;
        /// <summary>Public accessor for _autoDetectStartupMode (used by tests).</summary>
        public bool AutoDetectStartupMode { get => _autoDetectStartupMode; set => _autoDetectStartupMode = value; }

        [Tooltip(
            "False for Docker/dedicated-server flow: auth starts StartClient() against the dedicated server. Enable only for intentional legacy listen-server host tests."
        )]
        [FormerlySerializedAs("StartAsHost")]
        [SerializeField]
        bool _startAsHost = false;
        /// <summary>Public accessor for _startAsHost (used by tests).</summary>
        public bool StartAsHost { get => _startAsHost; set => _startAsHost = value; }

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

        public string PlayerName => _authenticatedPlayerName;

        [ShowInInspector]
        string ResolvedModePreview => ResolveStartupMode().ToString();

        [ShowInInspector]
        string HostEndpointPreview => $"{_hostAddress}:{_hostPort}";

        /// <summary>
        /// Static accessor for the active player name (read by NPCDialogueManager when building prompts).
        /// Also used by NPCPlayerNetworkAvatar to auto-register on client spawn.
        /// </summary>
        public static string ActivePlayerName { get; private set; } = "Player";

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
                _authController = FindAnyObjectByType<AuthUIController>(FindObjectsInactive.Include);
#endif
            if (_networkBootstrap == null)
                _networkBootstrap = FindAnyObjectByType<NPCNetworkBootstrap>(
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
#if UNITY_WEBGL && !UNITY_EDITOR
            if (
                string.IsNullOrWhiteSpace(_hostAddress)
                || _hostAddress == "127.0.0.1"
                || _hostAddress == "localhost"
            )
            {
                try
                {
                    Uri uri = new Uri(Application.absoluteURL);
                    if (uri.Host != "localhost" && uri.Host != "127.0.0.1")
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

            // Close the auth UI immediately
            CloseAuthUI();

            _logger?.Log(
                NPCFlowStage.UIInput,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Auth success for '{_authenticatedPlayerName}'. Starting network (mode: {resolvedMode.ToString().ToLowerInvariant()})...",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["playerName"] = _authenticatedPlayerName,
                    ["_startAsHost"] = _startAsHost,
                    ["_autoDetectStartupMode"] = _autoDetectStartupMode,
                    ["resolvedMode"] = resolvedMode.ToString(),
                }
            );
            lastBridgeStatus =
                $"Auth success for {_authenticatedPlayerName}; resolved mode {resolvedMode}.";

            // Initialize dialogue UI and backend system on demand to be memory smart!
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
            }
            else
#endif
            {
                // Headless fallback
                var bootstrapper = FindAnyObjectByType<NPCDialogueBootstrapper>(
                    FindObjectsInactive.Include
                );
                if (bootstrapper != null)
                {
                    _logger?.Log(
                        NPCFlowStage.SceneBootstrap,
                        NPCFlowStatus.Start,
                        NPCFlowLogLevel.Info,
                        "Triggering on-demand backend dialogue system initialization (headless fallback).",
                        source: nameof(AuthNetworkBridge)
                    );
                    await bootstrapper.InitializeOnDemandAsync();
                }
            }

            if (resolvedMode == ResolvedNetworkStartupMode.Host)
                StartHostAndRegisterPlayerName();
            else
                StartClientAndRegisterPlayerName();
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

        // ── Host mode ─────────────────────────────────────────────

        async void StartHostAndRegisterPlayerName()
        {
            if (_networkBootstrap == null)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "Cannot start host: NPCNetworkBootstrap not found.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = "Cannot start host: NPCNetworkBootstrap not found.";
                return;
            }

            NetworkManager netManager = _networkBootstrap.NetworkManager;
            if (netManager == null)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "Cannot start host: NetworkManager not found via bootstrap.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = "Cannot start host: NetworkManager not found.";
                return;
            }

            if (netManager.IsListening)
            {
                SetPlayerNameOnLocalAvatar(netManager);
                return;
            }

            // Bootstrap already configured transport in Awake.
            // Set host mode and let bootstrap's StartConfiguredMode handle the rest.
            _networkBootstrap.TransportConfig.autoStartMode = NPCNetworkAutoStartMode.Host;
            bool started = _networkBootstrap.StartConfiguredMode();
            if (!started)
            {
                NPCTransportConfig cfg = _networkBootstrap.TransportConfig;
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"Failed to start host on port {cfg.port} via bootstrap.",
                    source: nameof(AuthNetworkBridge),
                    data: new Dictionary<string, object>
                    {
                        ["_hostAddress"] = cfg.connectAddress ?? "unknown",
                        ["_hostPort"] = cfg.port,
                        ["listenAddress"] = cfg.listenAddress ?? "unknown",
                    }
                );
                lastBridgeStatus = $"Failed to start host on {cfg.connectAddress}:{cfg.port}.";
                return;
            }

            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                "Host started via bootstrap. Waiting for local player object...",
                source: nameof(AuthNetworkBridge)
            );
            lastBridgeStatus = "Host started. Waiting for local player object.";

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
                    "Cannot connect as client: NPCNetworkBootstrap not found.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = "Cannot connect as client: NPCNetworkBootstrap not found.";
                return;
            }

            NetworkManager netManager = _networkBootstrap.NetworkManager;
            if (netManager == null)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "Cannot connect as client: NetworkManager not found via bootstrap.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = "Cannot connect as client: NetworkManager not found.";
                return;
            }

            if (netManager.IsListening)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Skipped,
                    NPCFlowLogLevel.Info,
                    "Network already listening. Ignoring client start request.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = "Network already listening; skipped duplicate client start.";
                return;
            }

            // Override bootstrap's connect address if specified, then delegate to bootstrap
            _networkBootstrap.TransportConfig.connectAddress = string.IsNullOrWhiteSpace(_hostAddress)
                ? "127.0.0.1"
                : _hostAddress.Trim();
            if (_hostPort > 0)
                _networkBootstrap.TransportConfig.port = _hostPort;

            _networkBootstrap.TransportConfig.autoStartMode = NPCNetworkAutoStartMode.Client;
            bool started = _networkBootstrap.StartConfiguredMode();
            if (!started)
            {
                _logger?.Log(
                    NPCFlowStage.NetworkHost,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    "Failed to start client via bootstrap.",
                    source: nameof(AuthNetworkBridge)
                );
                lastBridgeStatus = $"Failed to start client to {_hostAddress}:{_hostPort}.";
                return;
            }

            string effectiveAddress = _networkBootstrap.TransportConfig.connectAddress;
            ushort effectivePort = _networkBootstrap.TransportConfig.port;
            _logger?.Log(
                NPCFlowStage.NetworkHost,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Client started via bootstrap, connecting to {effectiveAddress}:{effectivePort}...",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["_hostAddress"] = effectiveAddress,
                    ["_hostPort"] = effectivePort,
                }
            );
            lastBridgeStatus =
                $"Client started to {_hostAddress}:{(_hostPort > 0 ? _hostPort : _networkBootstrap.TransportConfig.port)} as {_authenticatedPlayerName}.";

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
