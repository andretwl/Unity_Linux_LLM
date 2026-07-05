using System.Threading.Tasks;
using UnityEngine;

namespace NPCSystem
{
    [DefaultExecutionOrder(-1000)]
    public class NPCDialogueBootstrapper : MonoBehaviour
    {
        public NPCDialogueManager dialogueManager;
        public bool autoSelectDefaultNPC = true;
        public string defaultNpcSlug = "";

        [Header("Startup Mode")]
        [Tooltip("If true, dialogue systems initialize on Start. If false, they can be initialized on-demand (e.g., after player login success).")]
        public bool initializeOnStart = false;

        void Awake()
        {
            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            if (dialogueManager == null)
            {
                dialogueManager = GetComponent<NPCDialogueManager>();
                if (dialogueManager == null)
                {
                    dialogueManager = FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
                }
            }

            logger.Log(NPCFlowStage.SceneBootstrap, dialogueManager != null ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                dialogueManager != null ? NPCFlowLogLevel.Info : NPCFlowLogLevel.Warning,
                dialogueManager != null ? "Bootstrapper resolved dialogue manager." : "Bootstrapper could not resolve dialogue manager.",
                source: nameof(NPCDialogueBootstrapper));
        }

        async void Start()
        {
            if (initializeOnStart)
            {
                await InitializeOnDemandAsync();
            }
        }

        private Task _onDemandInitTask;

        public Task InitializeOnDemandAsync()
        {
            _onDemandInitTask ??= InitializeOnDemandInternalAsync();
            return _onDemandInitTask;
        }

        async Task InitializeOnDemandInternalAsync()
        {
            NPCFlowLogger logger = NPCFlowLogger.FindOrCreate();
            if (dialogueManager == null)
            {
                logger.Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Skipped, NPCFlowLogLevel.Warning,
                    "Bootstrapper start skipped because dialogue manager is missing.", source: nameof(NPCDialogueBootstrapper));
                return;
            }

            // Perform post-login LocalAI readiness probe
            var backendReadiness = FindAnyObjectByType<NPCBackendReadinessService>(FindObjectsInactive.Include);
            if (backendReadiness != null)
            {
                await backendReadiness.ProbeAsync(probeLocalAi: true);
            }

            await dialogueManager.InitializeAsync();
            logger.Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "Dialogue manager initialized from bootstrapper on demand.", source: nameof(NPCDialogueBootstrapper));

            // Initialize dialogue network bridge to sync baseline status post-login
            var bridge = FindAnyObjectByType<NPCDialogueNetworkBridge>(FindObjectsInactive.Include);
            if (bridge != null)
            {
                await bridge.InitializeAsync();
            }

            // Run smoke validator configuration check post-login once dialogue systems are active
            var validator = FindAnyObjectByType<NPCDialogueSmokeValidator>(FindObjectsInactive.Include);
            if (validator != null)
            {
                validator.ValidateConfiguration();
            }

            if (!autoSelectDefaultNPC || dialogueManager.currentProfile != null)
            {
                logger.Log(NPCFlowStage.NPCSwitch, NPCFlowStatus.Skipped, NPCFlowLogLevel.Info,
                    "Default NPC auto-select skipped.", source: nameof(NPCDialogueBootstrapper));
                return;
            }

            string npcKey = string.IsNullOrWhiteSpace(defaultNpcSlug)
                ? dialogueManager.GetDefaultProfileSlug()
                : defaultNpcSlug.Trim();

            if (string.IsNullOrWhiteSpace(npcKey))
            {
                logger.Log(NPCFlowStage.NPCSwitch, NPCFlowStatus.Skipped, NPCFlowLogLevel.Warning,
                    "Default NPC auto-select skipped because no profile slug was available.", source: nameof(NPCDialogueBootstrapper));
                return;
            }

            await dialogueManager.SwitchToNPCAsync(npcKey);
            logger.Log(NPCFlowStage.NPCSwitch, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                "Default NPC selected by bootstrapper.", source: nameof(NPCDialogueBootstrapper), npcSlug: npcKey);
        }
    }
}
