using System.Threading.Tasks;
using EditorAttributes;
using UnityEngine;
using UnityEngine.SceneManagement;


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
namespace NPCSystem.Initialization
{
    [DisallowMultipleComponent]
    public sealed class WebGLGameplayLoadController : MonoBehaviour
    {
        [Title("WebGL Gameplay Load Controller")]
        [SerializeField, HideProperty]
        bool _loadAdditiveGameplayScene;

        [SerializeField, HideProperty]
        string _additiveGameplaySceneName = "Gameplay";

        [SerializeField, HideProperty]
        string[] _gameplayRootNames =
        {
            "Network_Manager",
            "NPCSceneInitialization",
            "NPCDialogueRuntimeBridge",
            "Backend",
            "NPCDialogueUI",
            "NPCDialogueSystem",
            "NPCNameplates",
            "Canvas",
            "Ground",
        };

        [SerializeField, HideProperty]
        bool _initializeDialogueUi = true;

        [SerializeField, HideProperty]
        bool _showGameplayCanvas = true;

        [SerializeField, ReadOnly]
        string _lastLoadStatus = "Idle";

        Task _loadTask;

        public bool IsLoaded => _loadTask != null && _loadTask.IsCompletedSuccessfully;

        public Task PrepareGameplayAsync()
        {
            _loadTask ??= PrepareGameplayInternalAsync();
            return _loadTask;
        }

        async Task PrepareGameplayInternalAsync()
        {
            _lastLoadStatus = "Preparing gameplay.";
            await Task.Yield();
            await LoadGameplaySceneAsync();
            ActivateGameplayRoots();
            await InitializeDialogueAsync();
            _lastLoadStatus = "Gameplay ready.";
        }

        async Task LoadGameplaySceneAsync()
        {
            if (
                !_loadAdditiveGameplayScene || string.IsNullOrWhiteSpace(_additiveGameplaySceneName)
            )
                return;

            string sceneName = _additiveGameplaySceneName.Trim();
            Scene scene = SceneManager.GetSceneByName(sceneName);
            if (scene.IsValid() && scene.isLoaded)
                return;

            AsyncOperation operation = SceneManager.LoadSceneAsync(
                sceneName,
                LoadSceneMode.Additive
            );
            if (operation == null)
            {
                _lastLoadStatus = $"Failed to start gameplay scene load: {sceneName}.";
                return;
            }

            while (!operation.isDone)
                await Task.Yield();
        }

        void ActivateGameplayRoots()
        {
            foreach (string rootName in _gameplayRootNames)
            {
                if (string.IsNullOrWhiteSpace(rootName))
                    continue;

                GameObject root = GameObject.Find(rootName.Trim());
                if (root == null)
                    root = FindInactiveRoot(rootName.Trim());
                if (root != null)
                    root.SetActive(true);
            }
        }

        async Task InitializeDialogueAsync()
        {
            if (!_initializeDialogueUi)
                return;

            // Find the initialization controller that was set up in the initial scene
            NPCSceneInitializationController initController = FindAnyObjectByType<NPCSceneInitializationController>(
                FindObjectsInactive.Include
            );

            if (initController != null && initController.IsDeferred)
            {
                // Let the pipeline handle Phases 3-8 in order
                await initController.ContinueInitializationAsync();
            }

            // Activate UI canvas after pipeline completes
            NPCDialogueUIController uiController = FindAnyObjectByType<NPCDialogueUIController>(
                FindObjectsInactive.Include
            );
            if (uiController != null)
            {
                if (_showGameplayCanvas)
                {
                    GameObject gameplayCanvas = uiController.GetGameplayCanvas();
                    if (gameplayCanvas != null)
                        gameplayCanvas.SetActive(true);
                }
                await uiController.InitializeOnDemandAsync();
            }
        }

        static GameObject FindInactiveRoot(string rootName)
        {
            GameObject[] roots = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject root in roots)
            {
                if (root == null || root.name != rootName)
                    continue;
                if (root.transform.parent != null)
                    continue;
                if (!root.scene.IsValid() || !root.scene.isLoaded)
                    continue;
                return root;
            }
            return null;
        }
    }
}
