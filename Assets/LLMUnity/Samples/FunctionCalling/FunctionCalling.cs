using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LLMUnity;
using NPCSystem;
using UnityEngine;
using UnityEngine.UI;

namespace LLMUnitySamples
{
    public class FunctionCalling : MonoBehaviour
    {
        [Serializable]
        class DialogueFunctionDefinition
        {
            public string Name;
            public string Description;
            public Func<string, Task<string>> Execute;
        }

        static readonly System.Random Random = new System.Random();
        static readonly string[] WeatherStates = { "sunny", "rainy", "cloudy", "stormy", "misty" };
        static readonly string[] EmotionalStates = { "curious", "guarded", "hopeful", "playful", "uneasy", "warm" };
        static readonly string[] RelationshipStates = { "cautious trust", "guarded curiosity", "earnest respect", "playful tension", "quiet suspicion" };
        static readonly string[] CreativeActions =
        {
            "gesture toward a prop in the room as if it hides part of the answer",
            "lower their voice and turn the line into a half-secret",
            "answer with a vivid sensory detail before the actual information",
            "tie the reply to a memory, rumor, or unresolved objective",
            "react to the player like the conversation just changed the emotional stakes"
        };

        const string DedicatedAgentName = "FunctionCallingAgent";
        const string DedicatedLlmName = "FunctionCallingLLM";

        public LLMAgent llmAgent;
        public NPCDialogueManager npcDialogueManager;
        public InputField playerText;
        public Text AIText;

        [TextArea(3, 6)]
        public string creativeGuide = "Choose the function that will make the current player/NPC exchange feel more alive, contextual, and scene-aware. Prefer functions that mention the active NPC, puzzle tools, dialogue state, or knowledge systems when relevant.";

        readonly List<DialogueFunctionDefinition> _functions = new List<DialogueFunctionDefinition>();
        string _lastNpcName = string.Empty;
        string _lastNpcReply = string.Empty;
        bool _subscribed;
        bool onValidateWarning = true;

        readonly Queue<string> _consoleLogs = new Queue<string>();
        const int MaxConsoleLogs = 10;

        void OnEnable()
        {
            Application.logMessageReceived += HandleLogReceived;
        }

        void OnDisable()
        {
            Application.logMessageReceived -= HandleLogReceived;
        }

        void HandleLogReceived(string logString, string stackTrace, LogType type)
        {
            if (ShouldFilterLog(logString)) return;

            string formattedLog = $"[{type}] {logString}";
            _consoleLogs.Enqueue(formattedLog);

            while (_consoleLogs.Count > MaxConsoleLogs)
            {
                _consoleLogs.Dequeue();
            }
        }

        bool ShouldFilterLog(string logString)
        {
            if (string.IsNullOrEmpty(logString)) return true;
            string lower = logString.ToLowerInvariant();
            return lower.Contains("choosing a dialogue function") ||
                   lower.Contains("llm chat") ||
                   lower.Contains("http") ||
                   lower.Contains("qdrant") ||
                   lower.Contains("cognee") ||
                   lower.Contains("post") ||
                   lower.Contains("get") ||
                   lower.Contains("host:") ||
                   lower.Contains("response:") ||
                   lower.Contains("embedding") ||
                   lower.Contains("localai") ||
                   lower.Contains("llmunity") ||
                   lower.Contains("functioncalling") ||
                   lower.Contains("dialoguefunctiondefinition");
        }

        string GetConsoleLogsSummary()
        {
            if (_consoleLogs.Count == 0) return "No live console logs captured.";
            return string.Join(" | ", _consoleLogs);
        }

        void Awake()
        {
            AutoAssignReferencesIfNeeded();
            BuildFunctions();
            SubscribeToDialogueEvents();
        }

        void Start()
        {
            AutoAssignReferencesIfNeeded();
            BuildFunctions();

            if (playerText != null)
            {
                playerText.onSubmit.RemoveListener(onInputFieldSubmit);
                playerText.onSubmit.AddListener(onInputFieldSubmit);
                playerText.Select();
            }
            else
            {
                Debug.LogWarning("[FunctionCalling] Player Input field is not assigned and could not be auto-wired.");
            }

            if (llmAgent != null)
            {
                llmAgent.grammar = MultipleChoiceGrammar();
            }
            else
            {
                Debug.LogError("[FunctionCalling] LLMAgent reference not set.");
            }
        }

        void OnDestroy()
        {
            if (playerText != null)
            {
                playerText.onSubmit.RemoveListener(onInputFieldSubmit);
            }

            if (_subscribed && npcDialogueManager != null)
            {
                npcDialogueManager.onNPCChanged.RemoveListener(HandleNpcChanged);
                npcDialogueManager.onResponseComplete.RemoveListener(HandleNpcResponseComplete);
                _subscribed = false;
            }
        }

        void AutoAssignReferencesIfNeeded()
        {
            if (llmAgent == null)
            {
                llmAgent = FindNamedComponent<LLMAgent>(DedicatedAgentName)
                    ?? FindNamedComponent<LLMAgent>("LLMAgent")
                    ?? FindAnyObjectByType<LLMAgent>(FindObjectsInactive.Include);
            }

            if (npcDialogueManager == null)
            {
                npcDialogueManager = FindNamedComponent<NPCDialogueManager>("NPCDialogueSystem") ?? FindAnyObjectByType<NPCDialogueManager>(FindObjectsInactive.Include);
            }

            if (playerText == null)
            {
                playerText = FindNamedComponent<InputField>("Canvas/PlayerInput") ?? FindAnyObjectByType<InputField>(FindObjectsInactive.Include);
            }

            if (AIText == null)
            {
                AIText = FindNamedComponent<Text>("Canvas/AIImage/AIText") ?? FindAnyObjectByType<Text>(FindObjectsInactive.Include);
            }
        }

        void SubscribeToDialogueEvents()
        {
            if (_subscribed || npcDialogueManager == null)
            {
                return;
            }

            npcDialogueManager.onNPCChanged.AddListener(HandleNpcChanged);
            npcDialogueManager.onResponseComplete.AddListener(HandleNpcResponseComplete);
            _subscribed = true;

            if (npcDialogueManager.currentProfile != null)
            {
                _lastNpcName = npcDialogueManager.currentProfile.GetDisplayName();
            }
        }

        void HandleNpcChanged(string npcName)
        {
            _lastNpcName = npcName ?? string.Empty;
        }

        void HandleNpcResponseComplete(string npcName, string message)
        {
            _lastNpcName = npcName ?? _lastNpcName;
            _lastNpcReply = message ?? string.Empty;
        }

        void BuildFunctions()
        {
            _functions.Clear();
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "Weather",
                Description = "Use when the player asks about weather, atmosphere, or outdoor conditions.",
                Execute = _ => Task.FromResult($"The atmosphere feels {Pick(WeatherStates)} around {GetNpcDisplayName()}.")
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "Time",
                Description = "Use when the player asks about time, schedules, urgency, or pacing.",
                Execute = _ => Task.FromResult($"It feels like {Random.Next(24):D2}:{Random.Next(60):D2}, and {GetNpcDisplayName()} reacts as if the next beat matters now.")
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "Emotion",
                Description = "Use when the player asks how someone feels or the emotional tone of the exchange.",
                Execute = message => Task.FromResult($"{GetNpcDisplayName()} reads the moment as {Pick(EmotionalStates)} after hearing '{TrimForQuote(message)}'.")
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "CurrentNpc",
                Description = "Use when the player asks who they are talking to or wants NPC-specific context.",
                Execute = _ => Task.FromResult(DescribeCurrentNpc())
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "ProfileVoice",
                Description = "Use when the reply should reflect the active NPC profile voice, personality, or dialogue rules.",
                Execute = _ => Task.FromResult(DescribeProfileVoice())
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "RelationshipPulse",
                Description = "Use when the player needs a social read on trust, tension, or rapport with the NPC.",
                Execute = message => Task.FromResult($"The relationship energy between the player and {GetNpcDisplayName()} feels like {Pick(RelationshipStates)} after '{TrimForQuote(message)}'.")
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "RagMemory",
                Description = "Use when the reply should lean on RAG, Qdrant, lore memory, or retrieved knowledge instead of improvising blindly.",
                Execute = message => Task.FromResult(DescribeKnowledgeRoute(message))
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "SceneFocus",
                Description = "Use when the best reply should mention current scene systems, puzzle tools, or dialogue infrastructure.",
                Execute = _ => Task.FromResult($"Scene focus: {string.Join(", ", GetSceneFeatures())}. This is a good moment for {GetNpcDisplayName()} to ground the reply in the current interaction space.")
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "TransportGuard",
                Description = "Use when the reply should respect the current LocalAI/LLMUnity transport setup and avoid assumptions that conflict with runtime wiring.",
                Execute = _ => Task.FromResult(DescribeTransportGuardrails())
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "CreativeHint",
                Description = "Use when the player needs a flavorful clue, mystery nudge, or creative next beat in the dialogue.",
                Execute = message => Task.FromResult(BuildCreativeHint(message))
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "PuzzleNudge",
                Description = "Use when the player sounds stuck and the reply should connect to notes, map, help, solve, or scene progression.",
                Execute = message => Task.FromResult(BuildPuzzleNudge(message))
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "ConsoleAwareness",
                Description = "Use when the player refers to glitches, bugs, errors, the console, or background reality anomalies.",
                Execute = _ => Task.FromResult($"{GetNpcDisplayName()} senses glitches in the background fabric of reality. The system's console logs reveal: {GetConsoleLogsSummary()}. Let {GetNpcDisplayName()} break the fourth wall creatively based on this.")
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "MemoryRecall",
                Description = "Use when the player asks about past conversations, previous interactions, memories, or what was discussed before.",
                Execute = message => RecallMemoryAsync(message)
            });
            _functions.Add(new DialogueFunctionDefinition
            {
                Name = "CodebaseKnowledge",
                Description = "Use when the player asks about code, C# scripts, Unity classes, systems, technical implementation details, or the physical structure of this project's code.",
                Execute = message => QueryCodebaseKnowledgeAsync(message)
            });
        }

        async Task<string> RecallMemoryAsync(string query)
        {
            return $"{GetNpcDisplayName()} tries to recall past conversations about '{TrimForQuote(query)}' but the long-term memory service is not currently connected. They respond based on what they know now.";
        }

        async Task<string> QueryCodebaseKnowledgeAsync(string query)
        {
            if (npcDialogueManager == null || npcDialogueManager.qdrantRag == null || npcDialogueManager.rag == null)
            {
                return $"[CodebaseKnowledge] Codebase index service or RAG embedder is unavailable. {GetNpcDisplayName()} feels like they lack the physical blueprint of their own universe.";
            }

            string originalCollection = npcDialogueManager.qdrantRag.collectionName;
            try
            {
                // Swapping collection to the codebase index
                npcDialogueManager.qdrantRag.collectionName = "unity_linux_llm_codebase_v1";

                string searchResult = await npcDialogueManager.qdrantRag.SearchMemoryAsync(npcDialogueManager.rag, query);
                
                if (string.IsNullOrEmpty(searchResult))
                {
                    return $"{GetNpcDisplayName()} searches the physical blueprints of the universe for '{TrimForQuote(query)}' but finds no matching structures. They remark on the mysteries of the unwritten laws.";
                }

                return $"{GetNpcDisplayName()} accesses the codebase blueprint. For query '{TrimForQuote(query)}', the structural C# details found are:\n{searchResult.Trim()}\nGround the NPC's reply in these physical script files or architectural relations.";
            }
            catch (Exception ex)
            {
                Debug.LogError($"[FunctionCalling] Error during CodebaseKnowledge: {ex.Message}");
                return $"{GetNpcDisplayName()} attempts to peer into the underlying cosmos but suffers a compiler static error (Error: {ex.Message}).";
            }
            finally
            {
                // Restore original collection
                npcDialogueManager.qdrantRag.collectionName = originalCollection;
            }
        }

        string[] GetFunctionNames()
        {
            return _functions.Select(function => function.Name).ToArray();
        }

        string MultipleChoiceGrammar()
        {
            return "root ::= (\"" + string.Join("\" | \"", GetFunctionNames()) + "\")";
        }

        string ConstructPrompt(string message)
        {
            string prompt = "Choose the best creative dialogue helper function for this Unity player/NPC exchange.\n\n";
            prompt += $"Guide: {creativeGuide}\n";
            prompt += $"Current NPC: {GetNpcDisplayName()}\n";
            prompt += $"Profile voice: {DescribeProfileVoice()}\n";
            prompt += $"Knowledge route: {DescribeKnowledgeRoute(message)}\n";
            prompt += $"Transport guardrails: {DescribeTransportGuardrails()}\n";
            prompt += $"Scene systems: {string.Join(", ", GetSceneFeatures())}\n";
            if (!string.IsNullOrWhiteSpace(_lastNpcReply))
            {
                prompt += $"Last NPC reply: {TrimForPrompt(_lastNpcReply, 160)}\n";
            }
            prompt += $"Player input: {message}\n\n";
            prompt += "Functions:\n";
            foreach (DialogueFunctionDefinition function in _functions)
            {
                prompt += $"- {function.Name}: {function.Description}\n";
            }
            prompt += "\nAnswer only with the function name that best helps the next player/NPC dialogue beat.";
            return prompt;
        }

        async Task<string> CallFunction(string functionName, string message)
        {
            DialogueFunctionDefinition definition = FindFunction(functionName) ?? FindFunction(ChooseFallbackFunction(message));
            return definition != null ? await definition.Execute(message) : await Task.FromResult(BuildCreativeHint(message));
        }

        DialogueFunctionDefinition FindFunction(string functionName)
        {
            string normalized = NormalizeFunctionName(functionName);
            return _functions.FirstOrDefault(function => NormalizeFunctionName(function.Name) == normalized);
        }

        string NormalizeFunctionName(string functionName)
        {
            return new string((functionName ?? string.Empty).Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        string ChooseFallbackFunction(string message)
        {
            string lower = (message ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("weather") || lower.Contains("rain") || lower.Contains("storm") || lower.Contains("outside")) return "Weather";
            if (lower.Contains("time") || lower.Contains("late") || lower.Contains("when")) return "Time";
            if (lower.Contains("feel") || lower.Contains("emotion") || lower.Contains("mood")) return "Emotion";
            if (lower.Contains("who") || lower.Contains("name") || lower.Contains("npc")) return "CurrentNpc";
            if (lower.Contains("personality") || lower.Contains("voice") || lower.Contains("character") || lower.Contains("roleplay")) return "ProfileVoice";
            if (lower.Contains("trust") || lower.Contains("relationship") || lower.Contains("friend") || lower.Contains("enemy")) return "RelationshipPulse";
            if (lower.Contains("rag") || lower.Contains("memory") || lower.Contains("lore") || lower.Contains("knowledge") || lower.Contains("qdrant")) return "RagMemory";
            if (lower.Contains("note") || lower.Contains("map") || lower.Contains("help") || lower.Contains("solve") || lower.Contains("stuck") || lower.Contains("hint")) return "PuzzleNudge";
            if (lower.Contains("remote") || lower.Contains("backend") || lower.Contains("localai") || lower.Contains("transport")) return "TransportGuard";
            if (lower.Contains("scene") || lower.Contains("here") || lower.Contains("around us")) return "SceneFocus";
            if (lower.Contains("glitch") || lower.Contains("bug") || lower.Contains("error") || lower.Contains("console") || lower.Contains("log") || lower.Contains("reality")) return "ConsoleAwareness";
            if (lower.Contains("past conversation") || lower.Contains("history") || lower.Contains("remember") || lower.Contains("recall") || lower.Contains("we talked")) return "MemoryRecall";
            if (lower.Contains("code") || lower.Contains("script") || lower.Contains("class") || lower.Contains("c#") || lower.Contains("symbol") || lower.Contains("blueprint")) return "CodebaseKnowledge";
            return "CreativeHint";
        }

        string DescribeCurrentNpc()
        {
            string npcName = GetNpcDisplayName();
            if (npcDialogueManager != null && npcDialogueManager.currentProfile != null)
            {
                return $"You are currently speaking with {npcName}, an active profile in the NPC dialogue system. Let the reply sound specific to that character instead of generic assistant chatter.";
            }

            return $"The active speaker is {npcName}. If the next line can be personalized, anchor it to that persona and the surrounding scene systems.";
        }

        string DescribeProfileVoice()
        {
            NPCProfile profile = npcDialogueManager != null ? npcDialogueManager.currentProfile : null;
            if (profile == null)
            {
                return "No active NPC profile is selected yet; keep the line adaptable and scene-aware.";
            }

            string systemPrompt = TrimForPrompt(profile.systemPrompt, 120);
            return $"{profile.GetDisplayName()} / slug={profile.GetNpcSlug()} / ragCategory={profile.GetRagCategory()} / systemPrompt={systemPrompt}";
        }

        string DescribeKnowledgeRoute(string message)
        {
            if (npcDialogueManager == null)
            {
                return "NPC dialogue manager unavailable, so rely on scene context rather than knowledge retrieval.";
            }

            NPCProfile profile = npcDialogueManager.currentProfile;
            string category = profile != null ? profile.GetRagCategory() : "unknown";
            bool qdrantEnabled = npcDialogueManager.useQdrantRag && npcDialogueManager.qdrantRag != null;
            bool ragEnabled = npcDialogueManager.enableRAG && npcDialogueManager.rag != null;
            string messageHint = string.IsNullOrWhiteSpace(message) ? "general dialogue" : $"player topic '{TrimForQuote(message)}'";

            List<string> routes = new List<string>();
            if (qdrantEnabled)
            {
                routes.Add($"Qdrant collection {npcDialogueManager.qdrantRag.collectionName} at {npcDialogueManager.qdrantRag.qdrantUrl}");
            }
            if (ragEnabled)
            {
                routes.Add($"local RAG category {category}");
            }
            if (routes.Count == 0)
            {
                routes.Add("prompt-only improvisation");
            }
            return $"For {messageHint}, prefer {string.Join(", then ", routes)}.";
        }

        string DescribeTransportGuardrails()
        {
            List<string> notes = new List<string>();
            if (npcDialogueManager != null && npcDialogueManager.useRemoteServer)
            {
                notes.Add("NPC dialogue uses direct OpenAI-compatible HTTP to LocalAI through NPCDialogueManager.");
            }
            if (llmAgent != null)
            {
                notes.Add($"Function selection agent '{llmAgent.gameObject.name}' uses LLMUnity remote={llmAgent.remote.ToString().ToLowerInvariant()}.");
                if (llmAgent.llm != null)
                {
                    notes.Add($"Its LLM object is '{llmAgent.llm.gameObject.name}' on port {llmAgent.llm.port} remote={llmAgent.llm.remote.ToString().ToLowerInvariant()}.");
                }
            }
            notes.Add("Do not assume the NPC reply transport and the function-selection transport are the same subsystem.");
            return string.Join(" ", notes);
        }

        string BuildCreativeHint(string message)
        {
            string npcName = GetNpcDisplayName();
            string action = Pick(CreativeActions);
            string lastReply = string.IsNullOrWhiteSpace(_lastNpcReply)
                ? "No prior NPC line is cached yet, so introduce a new dramatic beat."
                : $"Echo or contrast the last NPC line: '{TrimForPrompt(_lastNpcReply, 120)}'.";
            return $"Have {npcName} {action}. Keep it tied to the player's prompt '{TrimForQuote(message)}'. {lastReply} Respect this voice brief: {DescribeProfileVoice()}";
        }

        string BuildPuzzleNudge(string message)
        {
            List<string> systems = GetSceneFeatures();
            string focus = systems.FirstOrDefault(feature => feature.Contains("notes", StringComparison.OrdinalIgnoreCase) || feature.Contains("map", StringComparison.OrdinalIgnoreCase) || feature.Contains("help", StringComparison.OrdinalIgnoreCase) || feature.Contains("solve", StringComparison.OrdinalIgnoreCase))
                ?? "the dialogue system";
            return $"Nudge the player toward {focus}. Let {GetNpcDisplayName()} respond to '{TrimForQuote(message)}' with a subtle clue instead of a direct answer. If lore is needed, prefer this route: {DescribeKnowledgeRoute(message)}";
        }

        List<string> GetSceneFeatures()
        {
            List<string> features = new List<string>();
            MaybeAddFeature(features, npcDialogueManager != null, "NPC dialogue system");
            MaybeAddFeature(features, npcDialogueManager != null && npcDialogueManager.currentProfile != null, $"active NPC profile: {npcDialogueManager.currentProfile.GetDisplayName()}");
            MaybeAddFeature(features, GameObject.Find("RAG") != null, "RAG memory");
            MaybeAddFeature(features, GameObject.Find("LLMRAG") != null, "LLM+RAG overlay");
            MaybeAddFeature(features, IsSceneObjectActive("Canvas/NotesButton"), "notes UI");
            MaybeAddFeature(features, IsSceneObjectActive("Canvas/MapButton"), "map UI");
            MaybeAddFeature(features, IsSceneObjectActive("Canvas/HelpButton"), "help UI");
            MaybeAddFeature(features, IsSceneObjectActive("Canvas/SolveButton"), "solve UI");
            MaybeAddFeature(features, llmAgent != null && llmAgent.remote, "remote LLM agent");
            MaybeAddFeature(features, llmAgent != null && llmAgent.gameObject.name == DedicatedAgentName, "dedicated function-calling agent");
            MaybeAddFeature(features, llmAgent != null && llmAgent.llm != null && llmAgent.llm.gameObject.name == DedicatedLlmName, "dedicated function-calling LLM");
            MaybeAddFeature(features, npcDialogueManager != null && npcDialogueManager.useQdrantRag && npcDialogueManager.qdrantRag != null, $"Qdrant {npcDialogueManager.qdrantRag.collectionName}");
            if (features.Count == 0)
            {
                features.Add("basic dialogue scene");
            }
            return features;
        }

        bool IsSceneObjectActive(string path)
        {
            GameObject target = GameObject.Find(path);
            return target != null && target.activeInHierarchy;
        }

        void MaybeAddFeature(List<string> features, bool condition, string feature)
        {
            if (condition && !features.Contains(feature))
            {
                features.Add(feature);
            }
        }

        static T FindNamedComponent<T>(string path) where T : Component
        {
            GameObject target = GameObject.Find(path);
            return target != null ? target.GetComponent<T>() : null;
        }

        static string Pick(string[] values)
        {
            return values[Random.Next(values.Length)];
        }

        static string TrimForPrompt(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            string trimmed = text.Trim().Replace("\n", " ");
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength) + "…";
        }

        static string TrimForQuote(string text)
        {
            return TrimForPrompt(text, 80).Replace("'", "’");
        }

        string GetNpcDisplayName()
        {
            if (npcDialogueManager != null && npcDialogueManager.currentProfile != null)
            {
                return npcDialogueManager.currentProfile.GetDisplayName();
            }

            if (!string.IsNullOrWhiteSpace(_lastNpcName))
            {
                return _lastNpcName;
            }

            return "the current NPC";
        }

        async void onInputFieldSubmit(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                AIReplyComplete();
                return;
            }

            AutoAssignReferencesIfNeeded();
            BuildFunctions();

            if (llmAgent == null)
            {
                Debug.LogError("[FunctionCalling] Cannot run function selection without an LLMAgent reference.");
                if (AIText != null)
                {
                    AIText.text = "Missing LLMAgent reference.";
                }
                AIReplyComplete();
                return;
            }

            if (playerText != null)
            {
                playerText.interactable = false;
            }

            if (AIText != null)
            {
                AIText.text = "Choosing a dialogue function...";
            }

            string functionName = (await llmAgent.Chat(ConstructPrompt(message))).Trim();
            DialogueFunctionDefinition resolvedFunction = FindFunction(functionName) ?? FindFunction(ChooseFallbackFunction(message));
            string result = resolvedFunction != null ? await resolvedFunction.Execute(message) : BuildCreativeHint(message);
            if (AIText != null)
            {
                AIText.text = $"{GetNpcDisplayName()} -> {(resolvedFunction != null ? resolvedFunction.Name : ChooseFallbackFunction(message))}\n{result}";
            }

            await Task.Yield();
            AIReplyComplete();
        }

        public void AIReplyComplete()
        {
            if (playerText != null)
            {
                playerText.text = "";
                playerText.interactable = true;
                playerText.Select();
            }
        }

        public void CancelRequests()
        {
            llmAgent?.CancelRequests();
        }

        public void ExitGame()
        {
            Debug.Log("Exit button clicked");
            Application.Quit();
        }

        void OnValidate()
        {
            if (onValidateWarning && llmAgent != null && !llmAgent.remote && llmAgent.llm != null && llmAgent.llm.model == "")
            {
                Debug.LogWarning($"Please select a model in the {llmAgent.llm.gameObject.name} GameObject!");
                onValidateWarning = false;
            }

            if (npcDialogueManager != null && npcDialogueManager.useRemoteServer && llmAgent != null && llmAgent.remote && llmAgent.gameObject.name != DedicatedAgentName)
            {
                Debug.LogWarning("[FunctionCalling] This scene shares an LLMAgent with NPCDialogueManager. Prefer a dedicated FunctionCallingAgent when NPCDialogueManager.useRemoteServer is enabled.");
            }
        }
    }
}
