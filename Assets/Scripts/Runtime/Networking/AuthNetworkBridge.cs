using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace NPCSystem
{
    /// <summary>
    /// Bridges successful player authentication to network start and player name registration.
    /// After auth succeeds: closes the auth UI, starts a NetworkManager (host or client),
    /// then sets the authenticated player name on the local NPCPlayerNetworkAvatar's NetworkVariable.
    ///
    /// Modes:
    ///   startAsHost = true  → StartHost() after auth   (first player / listen-server)
    ///   startAsHost = false → StartClient() after auth  (late-joining player)
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class AuthNetworkBridge : MonoBehaviour
    {
        [Header("References")]
        public AuthUIController authController;
        public NPCNetworkBootstrap networkBootstrap;

        [Header("Mode")]
        [Tooltip("True: start as host (listen-server). False: connect as client to an existing host.")]
        public bool startAsHost = true;

        [Tooltip("Host address to connect to when startAsHost is false.")]
        public string hostAddress = "127.0.0.1";

        [Tooltip("Host port to connect to when startAsHost is false.")]
        public ushort hostPort = 7777;

        [Header("Events")]
        public UnityEngine.Events.UnityEvent<string> onHostStarted = new UnityEngine.Events.UnityEvent<string>();

        string _authenticatedPlayerName = "";
        NPCFlowLogger _logger;

        public string PlayerName => _authenticatedPlayerName;

        /// <summary>
        /// Static accessor for the active player name (read by NPCDialogueManager when building prompts).
        /// Also used by NPCPlayerNetworkAvatar to auto-register on client spawn.
        /// </summary>
        public static string ActivePlayerName { get; private set; } = "Player";

        void Awake()
        {
            ResolveReferences();
        }

        void Start()
        {
            _logger = NPCFlowLogger.FindOrCreate();
            BindAuthEvents();
        }

        void OnDestroy()
        {
            UnbindAuthEvents();
        }

        void ResolveReferences()
        {
            if (authController == null)
                authController = FindAnyObjectByType<AuthUIController>(FindObjectsInactive.Include);
            if (networkBootstrap == null)
                networkBootstrap = FindAnyObjectByType<NPCNetworkBootstrap>(FindObjectsInactive.Include);
        }

        void BindAuthEvents()
        {
            if (authController == null) return;
            authController.events.onLoginSuccess.AddListener(HandleAuthSuccess);
            authController.events.onRegisterSuccess.AddListener(HandleAuthSuccess);
        }

        void UnbindAuthEvents()
        {
            if (authController == null) return;
            authController.events.onLoginSuccess.RemoveListener(HandleAuthSuccess);
            authController.events.onRegisterSuccess.RemoveListener(HandleAuthSuccess);
        }

        void HandleAuthSuccess(string username)
        {
            _authenticatedPlayerName = username?.Trim() ?? "";
            ActivePlayerName = _authenticatedPlayerName;

            // Close the auth UI immediately
            CloseAuthUI();

            _logger?.Log(NPCFlowStage.UIInput, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                $"Auth success for '{_authenticatedPlayerName}'. Starting network (mode: {(startAsHost ? "host" : "client")})...",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["playerName"] = _authenticatedPlayerName,
                    ["startAsHost"] = startAsHost
                });

            if (startAsHost)
                StartHostAndRegisterPlayerName();
            else
                StartClientAndRegisterPlayerName();
        }

        void CloseAuthUI()
        {
            GameObject panel = GameObject.Find("Canvas/AuthPanel");
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
            NetworkManager netManager = GetNetworkManager();
            if (netManager == null)
            {
                _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "Cannot start host: NetworkManager not found.",
                    source: nameof(AuthNetworkBridge));
                return;
            }

            if (netManager.IsListening)
            {
                SetPlayerNameOnLocalAvatar(netManager);
                return;
            }

            // Configure transport for listen-server
            ConfigureHostTransport();

            bool started = netManager.StartHost();
            if (!started)
            {
                _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "Failed to start host.",
                    source: nameof(AuthNetworkBridge));
                return;
            }

            _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "Host started. Waiting for local player object...",
                source: nameof(AuthNetworkBridge));

            // Wait for player object to spawn
            int attempts = 0;
            while (attempts < 100)
            {
                if (netManager.IsListening &&
                    netManager.LocalClient != null &&
                    netManager.LocalClient.PlayerObject != null &&
                    netManager.LocalClient.PlayerObject.gameObject != null)
                {
                    SetPlayerNameOnLocalAvatar(netManager);
                    onHostStarted?.Invoke(_authenticatedPlayerName);
                    return;
                }
                await System.Threading.Tasks.Task.Yield();
                attempts++;
            }

            _logger?.Log(NPCFlowStage.PlayerNameRegistration, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                "Player object did not spawn within timeout (host). Name set via OnNetworkSpawn.",
                source: nameof(AuthNetworkBridge));
        }

        void ConfigureHostTransport()
        {
            if (networkBootstrap == null) return;
            networkBootstrap.ResolveReferences();
            networkBootstrap.ApplyTransportConfiguration();
        }

        // ── Client mode ───────────────────────────────────────────

        async void StartClientAndRegisterPlayerName()
        {
            NetworkManager netManager = GetNetworkManager();
            if (netManager == null)
            {
                _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "Cannot connect as client: NetworkManager not found.",
                    source: nameof(AuthNetworkBridge));
                return;
            }

            if (netManager.IsListening)
            {
                _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Skipped, NPCFlowLogLevel.Info,
                    "Network already listening. Ignoring client start request.",
                    source: nameof(AuthNetworkBridge));
                return;
            }

            // Configure transport to point at the host
            ConfigureClientTransport(netManager);

            bool started = netManager.StartClient();
            if (!started)
            {
                _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Error, NPCFlowLogLevel.Error,
                    "Failed to start client.",
                    source: nameof(AuthNetworkBridge));
                return;
            }

            _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                $"Client started, connecting to {hostAddress}:{hostPort}...",
                source: nameof(AuthNetworkBridge),
                data: new Dictionary<string, object>
                {
                    ["hostAddress"] = hostAddress,
                    ["hostPort"] = hostPort
                });

            // Name will be registered automatically by NPCPlayerNetworkAvatar.OnNetworkSpawn
            // which reads AuthNetworkBridge.ActivePlayerName and calls RegisterPlayerNameServerRpc.

            onHostStarted?.Invoke(_authenticatedPlayerName);
        }

        void ConfigureClientTransport(NetworkManager netManager)
        {
            // Set the connect address on the transport
            var transport = netManager.NetworkConfig?.NetworkTransport;
            if (transport is Unity.Netcode.Transports.UTP.UnityTransport utp)
            {
                utp.ConnectionData.Address = string.IsNullOrWhiteSpace(hostAddress) ? "127.0.0.1" : hostAddress.Trim();
                utp.ConnectionData.Port = hostPort > 0 ? hostPort : (ushort)7777;
                _logger?.Log(NPCFlowStage.NetworkHost, NPCFlowStatus.Success, NPCFlowLogLevel.Debug,
                    $"Client transport configured: {utp.ConnectionData.Address}:{utp.ConnectionData.Port}",
                    source: nameof(AuthNetworkBridge));
            }
        }

        // ── Shared ────────────────────────────────────────────────

        void SetPlayerNameOnLocalAvatar(NetworkManager netManager)
        {
            if (netManager == null || netManager.LocalClient == null) return;

            GameObject playerObj = netManager.LocalClient.PlayerObject != null
                ? netManager.LocalClient.PlayerObject.gameObject
                : null;
            if (playerObj == null)
            {
                _logger?.Log(NPCFlowStage.PlayerNameRegistration, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                    "Local client has no PlayerObject.",
                    source: nameof(AuthNetworkBridge));
                return;
            }

            var avatar = playerObj.GetComponent<NPCPlayerNetworkAvatar>();
            if (avatar != null)
            {
                avatar.SetDisplayName(_authenticatedPlayerName);
                _logger?.Log(NPCFlowStage.PlayerNameRegistration, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                    $"Player name '{_authenticatedPlayerName}' set on {playerObj.name}.",
                    source: nameof(AuthNetworkBridge),
                    data: new Dictionary<string, object>
                    {
                        ["playerName"] = _authenticatedPlayerName,
                        ["playerObjectName"] = playerObj.name
                    });
            }
            else
            {
                _logger?.Log(NPCFlowStage.PlayerNameRegistration, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                    $"No NPCPlayerNetworkAvatar found on {playerObj.name}.",
                    source: nameof(AuthNetworkBridge));
            }
        }

        NetworkManager GetNetworkManager()
        {
            if (networkBootstrap != null && networkBootstrap.networkManager != null)
                return networkBootstrap.networkManager;
            return FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        }
    }
}
