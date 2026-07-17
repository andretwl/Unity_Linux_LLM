using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EditorAttributes;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace NPCSystem
{
    /// <summary>
    /// Client for calling Supabase Edge Functions (via the Edge Runtime)
    /// from Unity.  Each method maps to a route on the main Edge Function
    /// service (port 8098 by default).
    ///
    /// For WebGL builds, UnityWebRequest is the only available HTTP path,
    /// so all calls go through UnityWebRequest.Post/Get.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class EdgeFunctionClient : MonoBehaviour
    {
        [FoldoutGroup("References", true, nameof(_dialogueManager))]
        [SerializeField]
        EditorAttributes.Void referencesGroup;

        [SerializeField, HideProperty]
        NPCDialogueManager _dialogueManager;

        [FoldoutGroup("Behaviour", true, nameof(_edgeFunctionUrl), nameof(_requestTimeoutSeconds))]
        [SerializeField]
        EditorAttributes.Void behaviourGroup;

        [SerializeField, HideProperty]
        [Tooltip("Base URL of the Edge Runtime that orchestrates memory and dialogue processing.")]
        string _edgeFunctionUrl = "http://localhost:8098";

        [SerializeField, HideProperty, Suffix("s")]
        float _requestTimeoutSeconds = 10f;

        NPCFlowLogger _logger;

        void Awake()
        {
            _logger = NPCFlowLogger.FindOrCreate();
        }

        void Reset()
        {
            _dialogueManager = FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
        }

        bool IsReady => !string.IsNullOrWhiteSpace(_edgeFunctionUrl);

        string BaseUrl => _edgeFunctionUrl;

        // ── Public API ──────────────────────────────────────────

        /// <summary>
        /// POST /memory/process-turn
        /// Generate embedding for a dialogue turn and store in dialogue_turn_vectors.
        /// </summary>
        public async Task<ProcessTurnResult> ProcessTurnAsync(
            long turnId,
            string userId,
            string npcSlug,
            string role,
            string content
        )
        {
            return await PostAsync<ProcessTurnResult>(
                "/memory/process-turn",
                new Dictionary<string, object>
                {
                    ["turn_id"] = turnId,
                    ["user_id"] = userId,
                    ["npc_slug"] = npcSlug,
                    ["role"] = role,
                    ["content"] = content,
                }
            );
        }

        /// <summary>
        /// POST /memory/summarize-session
        /// Generate a summary for a completed dialogue session.
        /// </summary>
        public async Task<SummarizeSessionResult> SummarizeSessionAsync(
            string sessionId,
            string userId
        )
        {
            return await PostAsync<SummarizeSessionResult>(
                "/memory/summarize-session",
                new Dictionary<string, object> { ["session_id"] = sessionId, ["user_id"] = userId }
            );
        }

        /// <summary>
        /// POST /memory/update-relationship
        /// Analyze a dialogue exchange and update player_npc_relationships.
        /// </summary>
        public async Task<UpdateRelationshipResult> UpdateRelationshipAsync(
            string userId,
            string npcSlug,
            string playerMessage,
            string npcResponse,
            int currentTrust = 50
        )
        {
            return await PostAsync<UpdateRelationshipResult>(
                "/memory/update-relationship",
                new Dictionary<string, object>
                {
                    ["user_id"] = userId,
                    ["npc_slug"] = npcSlug,
                    ["player_message"] = playerMessage,
                    ["npc_response"] = npcResponse,
                    ["current_trust"] = currentTrust,
                }
            );
        }

        /// <summary>
        /// POST /room/broadcast-dialogue
        /// Broadcast an NPC dialogue response to all members of a room.
        /// </summary>
        public async Task<RoomBroadcastResult> BroadcastRoomDialogueAsync(
            string roomId,
            string npcSlug,
            string dialogueMessage,
            string playerName = null,
            string sessionId = null
        )
        {
            return await PostAsync<RoomBroadcastResult>(
                "/room/broadcast-dialogue",
                new Dictionary<string, object>
                {
                    ["room_id"] = roomId,
                    ["npc_slug"] = npcSlug,
                    ["dialogue_message"] = dialogueMessage,
                    ["player_name"] = playerName ?? string.Empty,
                    ["session_id"] = sessionId ?? string.Empty,
                }
            );
        }

        // ── Internal ────────────────────────────────────────────

        async Task<T> PostAsync<T>(string route, object body)
            where T : class, new()
        {
            string url = $"{BaseUrl}{route}";

            try
            {
                string jsonBody = JsonConvert.SerializeObject(body);
                using var request = new UnityWebRequest(url, "POST")
                {
                    uploadHandler = new UploadHandlerRaw(
                        System.Text.Encoding.UTF8.GetBytes(jsonBody)
                    ),
                    downloadHandler = new DownloadHandlerBuffer(),
                };
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = Mathf.Max(1, Mathf.CeilToInt(_requestTimeoutSeconds));

                var operation = request.SendWebRequest();
                while (!operation.isDone)
                    await Task.Yield();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    _logger?.Log(
                        NPCFlowStage.BackendRequest,
                        NPCFlowStatus.Error,
                        NPCFlowLogLevel.Error,
                        $"Edge Function {route} failed: {request.error}",
                        source: nameof(EdgeFunctionClient)
                    );
                    return null;
                }

                string responseText = request.downloadHandler?.text;
                if (string.IsNullOrWhiteSpace(responseText))
                    return null;

                return JsonConvert.DeserializeObject<T>(responseText);
            }
            catch (Exception ex)
            {
                _logger?.Log(
                    NPCFlowStage.BackendRequest,
                    NPCFlowStatus.Error,
                    NPCFlowLogLevel.Error,
                    $"Edge Function {route} exception: {ex.Message}",
                    source: nameof(EdgeFunctionClient)
                );
                return null;
            }
        }

        // ── Result types ────────────────────────────────────────

        [Serializable]
        public class ProcessTurnResult
        {
            [Newtonsoft.Json.JsonProperty("status")]
            public string Status;

            [Newtonsoft.Json.JsonProperty("turn_id")]
            public long TurnId;

            [Newtonsoft.Json.JsonProperty("content_hash")]
            public string ContentHash;

            [Newtonsoft.Json.JsonProperty("dimensions")]
            public int Dimensions;

            [Newtonsoft.Json.JsonProperty("reason")]
            public string Reason;
        }

        [Serializable]
        public class SummarizeSessionResult
        {
            [Newtonsoft.Json.JsonProperty("status")]
            public string Status;

            [Newtonsoft.Json.JsonProperty("session_id")]
            public string SessionId;

            [Newtonsoft.Json.JsonProperty("summary_length")]
            public int SummaryLength;
        }

        [Serializable]
        public class UpdateRelationshipResult
        {
            [Newtonsoft.Json.JsonProperty("status")]
            public string Status;

            [Newtonsoft.Json.JsonProperty("npc_slug")]
            public string NpcSlug;

            [Newtonsoft.Json.JsonProperty("trust_delta")]
            public int TrustDelta;

            [Newtonsoft.Json.JsonProperty("new_mood")]
            public string NewMood;
        }

        [Serializable]
        public class RoomBroadcastResult
        {
            [Newtonsoft.Json.JsonProperty("status")]
            public string Status;

            [Newtonsoft.Json.JsonProperty("room_id")]
            public string RoomId;

            [Newtonsoft.Json.JsonProperty("npc_slug")]
            public string NpcSlug;
        }

        // ── Editor Validation ───────────────────────────────────

        [Button("Validate Settings")]
        void ValidateSettings()
        {
            _logger?.Log(
                NPCFlowStage.ConfigurationValidation,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"EdgeFunctionClient: baseUrl={BaseUrl}",
                source: nameof(EdgeFunctionClient)
            );
        }
    }
}
