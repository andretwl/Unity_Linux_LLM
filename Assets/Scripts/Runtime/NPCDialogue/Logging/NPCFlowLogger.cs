using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using EditorAttributes;
using UnityEngine;

namespace NPCSystem
{
    [DefaultExecutionOrder(-3000)]
    public sealed class NPCFlowLogger : MonoBehaviour
    {
        // ── EditorAttributes group: reference pattern for any component logging ──
        [Title("NPC Flow Logger")]
        [HelpBox(
            "This logger provides a reusable Inspector logging pattern. To add logging to any component:\n1) Call NPCFlowLogger.FindOrCreate().Log(stage, status, level, message, source: nameof(YourClass));\n2) Or wrap work in using var scope = NPCFlowScope.Start(logger, stage, source: nameof(YourClass));\n3) Define stages in NPCFlowStage, statuses in NPCFlowStatus, levels in NPCFlowLogLevel.",
            MessageMode.Log,
            drawAbove: true
        )]
        [FoldoutGroup("Console Output", true, nameof(LogToUnityConsole))]
        [SerializeField]
        bool _logToUnityConsole = true;
        public bool LogToUnityConsole { get => _logToUnityConsole; set => _logToUnityConsole = value; }

        [SerializeField]
        bool _logToStdout = false;
        public bool LogToStdout { get => _logToStdout; set => _logToStdout = value; }

        [FoldoutGroup("File Output", true, nameof(LogToJsonlFile))]
        [SerializeField]
        bool _logToJsonlFile = true;
        public bool LogToJsonlFile { get => _logToJsonlFile; set => _logToJsonlFile = value; }

        [FoldoutGroup(
            "Text Sanitization",
            nameof(IncludeTextSnippets),
            nameof(IncludeRawTextPayloads),
            nameof(MaxSnippetChars)
        )]
        [SerializeField]
        bool _includeTextSnippets = false;
        public bool IncludeTextSnippets { get => _includeTextSnippets; set => _includeTextSnippets = value; }

        [SerializeField]
        bool _includeRawTextPayloads = false;
        public bool IncludeRawTextPayloads { get => _includeRawTextPayloads; set => _includeRawTextPayloads = value; }

        [ShowField(nameof(IncludeTextOrRaw))]
        [SerializeField]
        int _maxSnippetChars = 80;
        public int MaxSnippetChars { get => _maxSnippetChars; set => _maxSnippetChars = value; }

        [FoldoutGroup("Cache Settings", true, nameof(MaxInMemoryEvents))]
        [SerializeField]
        private EditorAttributes.Void cacheSettingsGroup;

        [SerializeField, HideProperty]
        int _maxInMemoryEvents = 500;
        public int MaxInMemoryEvents { get => _maxInMemoryEvents; set => _maxInMemoryEvents = value; }

        [FoldoutGroup(
            "Log File Storage",
            true,
            nameof(RelativeLogDirectory),
            nameof(OverrideAbsoluteLogDirectory),
            nameof(MaxLogDays),
            nameof(MaxLogDirectorySizeMB)
        )]
        [SerializeField]
        private EditorAttributes.Void logFileStorageGroup;

        [SerializeField, HideProperty]
        string _relativeLogDirectory = "NPCDialogue/Logs";
        public string RelativeLogDirectory { get => _relativeLogDirectory; set => _relativeLogDirectory = value; }

        [SerializeField, HideProperty]
        string _overrideAbsoluteLogDirectory = "";
        public string OverrideAbsoluteLogDirectory { get => _overrideAbsoluteLogDirectory; set => _overrideAbsoluteLogDirectory = value; }

        [SerializeField, HideProperty, Suffix("days")]
        int _maxLogDays = 7;
        public int MaxLogDays { get => _maxLogDays; set => _maxLogDays = value; }

        [SerializeField, HideProperty, Suffix("MB")]
        int _maxLogDirectorySizeMB = 100;
        public int MaxLogDirectorySizeMB { get => _maxLogDirectorySizeMB; set => _maxLogDirectorySizeMB = value; }

        [FoldoutGroup("Retry Suppression", true, nameof(MaxDuplicateEventsPerMinute))]
        [SerializeField]
        private EditorAttributes.Void suppressionGroup;

        [SerializeField, HideProperty, Suffix("events/min")]
        int _maxDuplicateEventsPerMinute = 5;
        public int MaxDuplicateEventsPerMinute { get => _maxDuplicateEventsPerMinute; set => _maxDuplicateEventsPerMinute = value; }

        // ── Runtime state ──
        static NPCFlowLogger _instance;
        static readonly object _initSync = new object();
        static int _fallbackWarningEmitted;
        static bool _cleanupRun;

        readonly object _sync = new object();
        readonly List<NPCFlowEvent> _recentEvents = new List<NPCFlowEvent>();
        readonly Dictionary<string, SuppressionCounter> _suppressionCounters =
            new Dictionary<string, SuppressionCounter>();
        int _requestCounter;
        string _sessionId = string.Empty;
        string _currentLogPath = string.Empty;
        bool _fileFailureWarned;
        string _currentConversationId = string.Empty;

        // ── Inspector preview helpers (not serialised) ──
        [ShowInInspector]
        string InspectorSessionId =>
            string.IsNullOrWhiteSpace(_sessionId) ? "not initialized" : _sessionId;

        [ShowInInspector]
        string InspectorLogFilePath =>
            string.IsNullOrWhiteSpace(_currentLogPath) ? "not set" : _currentLogPath;

        [ShowInInspector]
        string InspectorConversationId =>
            string.IsNullOrWhiteSpace(_currentConversationId) ? "not set" : _currentConversationId;

        [ShowInInspector]
        int InspectorRecentEventCount
        {
            get
            {
                lock (_sync)
                {
                    return _recentEvents.Count;
                }
            }
        }

        // ── Public instance access ──
        public static NPCFlowLogger Instance => _instance;

        public string SessionId
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_sessionId))
                {
                    _sessionId = CreateSessionId();
                }
                return _sessionId;
            }
        }

        public string CurrentLogPath
        {
            get
            {
                EnsureLogPath();
                return _currentLogPath;
            }
        }

        /// <summary>
        /// Set the active conversationId for correlation across dialogue turns.
        /// All subsequent Log calls without an explicit conversationId will use this value.
        /// </summary>
        public string ConversationId
        {
            get => _currentConversationId;
            set => _currentConversationId = value ?? string.Empty;
        }

        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static NPCFlowLogger FindOrCreate()
        {
            if (_instance != null)
                return _instance;

            lock (_initSync)
            {
                if (_instance != null)
                    return _instance;

                NPCFlowLogger[] sceneLoggers = FindObjectsByType<NPCFlowLogger>(
                    FindObjectsInactive.Include
                );
                NPCFlowLogger sceneLogger = sceneLoggers.Length > 0 ? sceneLoggers[0] : null;
                if (sceneLogger != null)
                {
                    _instance = sceneLogger;
                    _instance.ApplyPlatformLoggingOverrides();
                    return _instance;
                }

                var loggerObject = new GameObject("NPCFlowLogger");
                if (Application.isPlaying)
                    DontDestroyOnLoad(loggerObject);
                _instance = loggerObject.AddComponent<NPCFlowLogger>();
                _instance.ApplyPlatformLoggingOverrides();
                _instance.Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    "Auto-created fallback NPCFlowLogger. Add an explicit scene logger for Inspector-controlled settings.",
                    source: nameof(NPCFlowLogger)
                );
                return _instance;
            }
        }

        public string NextRequestId()
        {
            return $"req-{Interlocked.Increment(ref _requestCounter):D6}";
        }

        public void Log(
            NPCFlowStage stage,
            NPCFlowStatus status,
            NPCFlowLogLevel level,
            string message,
            string source = null,
            string requestId = null,
            string npcSlug = null,
            long? durationMs = null,
            Dictionary<string, object> data = null
        )
        {
            Log(
                new NPCFlowEvent
                {
                    Stage = stage,
                    Status = status,
                    Level = level,
                    Message = message ?? string.Empty,
                    Source = source ?? string.Empty,
                    RequestId = requestId ?? string.Empty,
                    NpcSlug = npcSlug ?? string.Empty,
                    DurationMs = durationMs ?? 0,
                    Data = data,
                }
            );
        }

        public void Log(NPCFlowEvent flowEvent)
        {
            if (flowEvent == null)
                return;

            try
            {
                PrepareEvent(flowEvent);

                // Retry suppression: skip duplicate events that exceed the rate limit
                if (IsSuppressed(flowEvent))
                    return;

                AddToRingBuffer(flowEvent);
                if (LogToUnityConsole)
                    WriteUnityConsole(flowEvent);
                if (LogToStdout)
                    WriteStdout(flowEvent);
                if (LogToJsonlFile && SupportsPersistentFileLogging(Application.platform))
                    WriteJsonLine(flowEvent);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _fallbackWarningEmitted, 1) == 0)
                {
                    UnityEngine.Debug.LogWarning(
                        $"[NPCFlow] Logging failure suppressed after first warning: {ex.Message}"
                    );
                }
            }
        }

        public IReadOnlyList<NPCFlowEvent> GetRecentEvents()
        {
            lock (_sync)
            {
                return new List<NPCFlowEvent>(_recentEvents);
            }
        }

        [Button("Flush Log File", buttonHeight: 20f)]
        void FlushInspector()
        {
            EnsureLogPath();
            Log(
                NPCFlowStage.SceneBootstrap,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Debug,
                "Manual log flush triggered from Inspector.",
                source: nameof(NPCFlowLogger)
            );
        }

        [Button(buttonLabel: "Run Log Cleanup Now", buttonHeight: 20f)]
        void RunLogCleanupInspector()
        {
            string directory = !string.IsNullOrWhiteSpace(OverrideAbsoluteLogDirectory)
                ? OverrideAbsoluteLogDirectory.Trim()
                : Path.Combine(
                    path1: Application.persistentDataPath,
                    path2: string.IsNullOrWhiteSpace(RelativeLogDirectory)
                        ? "NPCDialogue/Logs"
                        : RelativeLogDirectory.Trim()
                );
            RunLogCleanup(directory);
        }

        [Button("Log Recent Event Count", buttonHeight: 20f)]
        void LogRecentEventCountInspector()
        {
            int count;
            lock (_sync)
            {
                count = _recentEvents.Count;
            }
            Log(
                NPCFlowStage.EditorWorkflow,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Info,
                $"Inspector diagnostic: {count} events in ring buffer.",
                source: nameof(NPCFlowLogger)
            );
        }

        [Button("Validate All Logger Settings")]
        void ValidateAllSettingsInspector()
        {
            var issues = new System.Collections.Generic.List<string>();
            if (!HasValidMaxEvents())
                issues.Add("MaxInMemoryEvents must be > 0");
            if (!HasValidMaxLogDays())
                issues.Add("MaxLogDays must be > 0");
            if (!HasValidMaxDirMB())
                issues.Add("MaxLogDirectorySizeMB must be > 0");
            if (!HasValidSnippetChars())
                issues.Add("MaxSnippetChars must be > 0 when text sanitization is active");
            if (!HasValidRelativeLogDir())
                issues.Add("RelativeLogDirectory cannot be empty");

            string directory = !string.IsNullOrWhiteSpace(OverrideAbsoluteLogDirectory)
                ? OverrideAbsoluteLogDirectory.Trim()
                : Path.Combine(
                    path1: Application.persistentDataPath,
                    path2: string.IsNullOrWhiteSpace(value: RelativeLogDirectory)
                        ? "NPCDialogue/Logs"
                        : RelativeLogDirectory.Trim()
                );

            Log(
                stage: NPCFlowStage.ConfigurationValidation,
                status: issues.Count == 0 ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                level: NPCFlowLogLevel.Info,
                message: issues.Count == 0
                    ? "All logger settings valid. Log path: "
                        + Path.Combine(path1: directory, path2: "npc-flow-*.jsonl").Replace(oldChar: '\\', newChar: '/')
                    : "Logger settings have "
                        + issues.Count
                        + " issue(s): "
                        + string.Join("; ", issues),
                source: nameof(NPCFlowLogger),
                data: new Dictionary<string, object>
                {
                    ["LogToUnityConsole"] = LogToUnityConsole,
                    ["LogToJsonlFile"] = LogToJsonlFile,
                    ["MaxInMemoryEvents"] = MaxInMemoryEvents,
                    ["MaxLogDays"] = MaxLogDays,
                    ["MaxLogDirectorySizeMB"] = MaxLogDirectorySizeMB,
                    ["logDirectory"] = directory,
                    ["issuesFound"] = issues.Count,
                }
            );
        }

        public void Flush()
        {
            EnsureLogPath();
            Log(
                NPCFlowStage.SceneBootstrap,
                NPCFlowStatus.Success,
                NPCFlowLogLevel.Debug,
                "Flush triggered.",
                source: nameof(NPCFlowLogger)
            );
        }

        public Dictionary<string, object> SummarizeText(string prefix, string text)
        {
            return NPCFlowTextSanitizer.MergeSummary(
                new Dictionary<string, object>(),
                prefix,
                text,
                IncludeTextSnippets || IncludeRawTextPayloads,
                MaxSnippetChars
            );
        }

        // ── Stage-to-Category mapping ──
        public static NPCFlowCategory StageToCategory(NPCFlowStage stage)
        {
            return stage switch
            {
                NPCFlowStage.SceneBootstrap or NPCFlowStage.ReferenceResolution
                    or NPCFlowStage.ConfigurationValidation or NPCFlowStage.ProfileIndexBuild
                        => NPCFlowCategory.Infrastructure,

                NPCFlowStage.HistoryLoad or NPCFlowStage.HistoryRestore
                    or NPCFlowStage.HistoryPersist => NPCFlowCategory.Memory,

                NPCFlowStage.NPCSwitch or NPCFlowStage.UIInput
                    => NPCFlowCategory.UI,

                NPCFlowStage.AuthRequest or NPCFlowStage.AuthSession
                    => NPCFlowCategory.Auth,

                NPCFlowStage.RequestStart or NPCFlowStage.ClientSession
                    or NPCFlowStage.DialogueRouting or NPCFlowStage.ActionSelection
                    or NPCFlowStage.ActionExecution or NPCFlowStage.GrammarOverride
                    or NPCFlowStage.GrammarRestore or NPCFlowStage.ResponseComplete
                        => NPCFlowCategory.Dialogue,

                NPCFlowStage.ContextRetrieval or NPCFlowStage.LocalRagReady
                    or NPCFlowStage.LocalRagSearch or NPCFlowStage.QdrantEmbedding
                    or NPCFlowStage.QdrantSearch => NPCFlowCategory.RAG,

                NPCFlowStage.PromptBuild or NPCFlowStage.DialogueGeneration
                    or NPCFlowStage.BackendRequest or NPCFlowStage.LLMChat
                    or NPCFlowStage.LLMStream => NPCFlowCategory.LLM,

                NPCFlowStage.NetworkHost or NPCFlowStage.PlayerSpawn
                    or NPCFlowStage.NpcSpawn or NPCFlowStage.PlayerNameRegistration
                    or NPCFlowStage.RpcTraffic or NPCFlowStage.OwnershipAuthority
                        => NPCFlowCategory.Network,

                NPCFlowStage.EditorWorkflow or NPCFlowStage.SmokeValidation
                    => NPCFlowCategory.EditorWorkflow,

                _ => NPCFlowCategory.Infrastructure,
            };
        }

        void ApplyPlatformLoggingOverrides()
        {
            if (SupportsPersistentFileLogging(Application.platform))
            {
                // On server/headless builds (Linux dedicated server running in Docker),
                // auto-enable stdout logging so the Docker json-file log driver captures
                // structured logs for forwarding to the Datadog Agent.
                if (Application.isBatchMode || Application.platform == RuntimePlatform.LinuxServer)
                {
                    _logToStdout = true;
                }
                return;
            }

            LogToJsonlFile = false;
        }

        public static bool SupportsPersistentFileLogging(RuntimePlatform platform)
        {
            return platform != RuntimePlatform.WebGLPlayer;
        }

        void PrepareEvent(NPCFlowEvent flowEvent)
        {
            flowEvent.SchemaVersion = flowEvent.SchemaVersion <= 0 ? 1 : flowEvent.SchemaVersion;
            if (string.IsNullOrWhiteSpace(flowEvent.TimestampUtc))
                flowEvent.TimestampUtc = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrWhiteSpace(flowEvent.SessionId))
                flowEvent.SessionId = SessionId;
            if (string.IsNullOrWhiteSpace(flowEvent.ConversationId) && !string.IsNullOrWhiteSpace(_currentConversationId))
                flowEvent.ConversationId = _currentConversationId;
            if (flowEvent.Category == NPCFlowCategory.Infrastructure && flowEvent.Stage != NPCFlowStage.SceneBootstrap)
                flowEvent.Category = StageToCategory(flowEvent.Stage);
            flowEvent.Source = flowEvent.Source ?? string.Empty;
            flowEvent.RequestId = flowEvent.RequestId ?? string.Empty;
            flowEvent.NpcSlug = flowEvent.NpcSlug ?? string.Empty;
            flowEvent.Message = flowEvent.Message ?? string.Empty;
        }

        /// <summary>
        /// Retry suppression: limits duplicate events per source+stage+message combos
        /// to MaxDuplicateEventsPerMinute, preventing log storms during retry storms.
        /// </summary>
        bool IsSuppressed(NPCFlowEvent flowEvent)
        {
            if (MaxDuplicateEventsPerMinute <= 0)
                return false;

            // Only suppress Warning and Error levels in retry loops
            if (flowEvent.Level != NPCFlowLogLevel.Warning && flowEvent.Level != NPCFlowLogLevel.Error)
                return false;

            // Build a suppression key from source + stage + truncated message
            string key = $"{flowEvent.Source}|{flowEvent.Stage}|{TruncateHash(flowEvent.Message, 60)}";

            long nowTicks = DateTime.UtcNow.Ticks;
            long windowTicks = TimeSpan.TicksPerMinute;

            lock (_sync)
            {
                if (!_suppressionCounters.TryGetValue(key, out var counter))
                {
                    counter = new SuppressionCounter();
                    _suppressionCounters[key] = counter;
                }

                // Prune expired timestamps
                counter.timestamps.RemoveAll(ts => nowTicks - ts > windowTicks);

                if (counter.timestamps.Count >= MaxDuplicateEventsPerMinute)
                {
                    // Event is suppressed — silently dropped
                    return true;
                }

                counter.timestamps.Add(nowTicks);

                // Periodically clean stale entries to prevent dictionary bloat
                if (_suppressionCounters.Count > 200)
                {
                    var staleKeys = new List<string>();
                    foreach (var kvp in _suppressionCounters)
                    {
                        kvp.Value.timestamps.RemoveAll(ts => nowTicks - ts > windowTicks);
                        if (kvp.Value.timestamps.Count == 0)
                            staleKeys.Add(kvp.Key);
                    }
                    foreach (var k in staleKeys)
                        _suppressionCounters.Remove(k);
                }

                return false;
            }
        }

        static string TruncateHash(string text, int maxLen)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
            return text.Length <= maxLen ? text : text.Substring(0, maxLen);
        }

        void AddToRingBuffer(NPCFlowEvent flowEvent)
        {
            lock (_sync)
            {
                _recentEvents.Add(flowEvent);
                int maxEvents = Mathf.Max(1, MaxInMemoryEvents);
                if (_recentEvents.Count > maxEvents)
                {
                    _recentEvents.RemoveRange(0, _recentEvents.Count - maxEvents);
                }
            }
        }

        void WriteUnityConsole(NPCFlowEvent flowEvent)
        {
            string line = FormatConsoleLine(flowEvent);
            switch (flowEvent.Level)
            {
                case NPCFlowLogLevel.Error:
                    UnityEngine.Debug.LogError(line);
                    break;
                case NPCFlowLogLevel.Warning:
                    UnityEngine.Debug.LogWarning(line);
                    break;
                default:
                    UnityEngine.Debug.Log(line);
                    break;
            }
        }

        /// <summary>
        /// Writes structured JSON to stdout (Console.Out) for collection by the
        /// Docker json-file log driver and forwarding to the Datadog Agent.
        /// On headless/server builds this is auto-enabled; on Editor it must be
        /// opted in via the Inspector or code.
        /// </summary>
        void WriteStdout(NPCFlowEvent flowEvent)
        {
            try
            {
                string json = flowEvent.ToJson();
                Console.WriteLine(json);
            }
            catch (Exception ex)
            {
                // Avoid recursive logging: write a minimal fallback to stderr
                Console.Error.WriteLine($"[NPCFlow] stdout write failed: {ex.Message}");
            }
        }

        void WriteJsonLine(NPCFlowEvent flowEvent)
        {
            try
            {
                EnsureLogPath();
                File.AppendAllText(_currentLogPath, flowEvent.ToJson() + Environment.NewLine);
            }
            catch (Exception ex)
            {
                if (!_fileFailureWarned)
                {
                    _fileFailureWarned = true;
                    UnityEngine.Debug.LogWarning(
                        $"[NPCFlow] Failed to write JSONL log: {ex.Message}"
                    );
                }
            }
        }

        void EnsureLogPath()
        {
            if (!string.IsNullOrWhiteSpace(_currentLogPath))
                return;

            string directory = !string.IsNullOrWhiteSpace(OverrideAbsoluteLogDirectory)
                ? OverrideAbsoluteLogDirectory.Trim()
                : Path.Combine(
                    Application.persistentDataPath,
                    string.IsNullOrWhiteSpace(RelativeLogDirectory)
                        ? "NPCDialogue/Logs"
                        : RelativeLogDirectory.Trim()
                );

            _currentLogPath = Path.Combine(directory, $"npc-flow-{SessionId}.jsonl")
                .Replace('\\', '/');

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            if (!_cleanupRun)
            {
                _cleanupRun = true;
                RunLogCleanup(directory);
            }
        }

        void RunLogCleanup(string directory)
        {
            try
            {
                if (!Directory.Exists(directory))
                    return;

                int deletedCount = 0;
                var logFiles = new List<System.IO.FileInfo>();

                foreach (string filePath in Directory.GetFiles(directory, "npc-flow-*.jsonl"))
                {
                    logFiles.Add(new System.IO.FileInfo(filePath));
                }

                if (logFiles.Count == 0)
                    return;

                // Phase 1: delete files older than MaxLogDays
                DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, MaxLogDays));
                for (int i = logFiles.Count - 1; i >= 0; i--)
                {
                    if (logFiles[i].LastWriteTimeUtc < cutoff)
                    {
                        try
                        {
                            logFiles[i].Delete();
                            logFiles.RemoveAt(i);
                            deletedCount++;
                        }
                        catch
                        { /* best effort */ }
                    }
                }

                // Phase 2: if still over size limit, delete oldest until under
                if (MaxLogDirectorySizeMB > 0 && logFiles.Count > 0)
                {
                    long maxBytes = (long)MaxLogDirectorySizeMB * 1024L * 1024L;
                    long currentBytes = 0;
                    foreach (var fi in logFiles)
                    {
                        if (fi.Exists)
                            currentBytes += fi.Length;
                    }

                    if (currentBytes > maxBytes)
                    {
                        logFiles.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                        foreach (var fi in logFiles)
                        {
                            if (currentBytes <= maxBytes || !fi.Exists)
                                break;
                            try
                            {
                                long len = fi.Length;
                                fi.Delete();
                                currentBytes -= len;
                                deletedCount++;
                            }
                            catch
                            { /* best effort */ }
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    Log(
                        NPCFlowStage.SceneBootstrap,
                        NPCFlowStatus.Success,
                        NPCFlowLogLevel.Debug,
                        $"Cleaned up {deletedCount} old log file(s) from {directory}.",
                        source: nameof(NPCFlowLogger),
                        data: new Dictionary<string, object>
                        {
                            ["directory"] = directory,
                            ["deletedCount"] = deletedCount,
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                Log(
                    NPCFlowStage.SceneBootstrap,
                    NPCFlowStatus.Warning,
                    NPCFlowLogLevel.Warning,
                    $"Log cleanup failed: {ex.Message}",
                    source: nameof(NPCFlowLogger)
                );
            }
        }

        public static void LogEditorWorkflow(
            NPCFlowStatus status,
            NPCFlowLogLevel level,
            string message,
            string source,
            Dictionary<string, object> data = null
        )
        {
            var flowEvent = new NPCFlowEvent
            {
                SessionId = $"editor-{DateTime.UtcNow:yyyyMMdd}",
                Source = source ?? "EditorWorkflow",
                Stage = NPCFlowStage.EditorWorkflow,
                Status = status,
                Level = level,
                Message = message ?? string.Empty,
                Data = data,
                Category = NPCFlowCategory.EditorWorkflow,
            };

            try
            {
                string projectRoot =
                    Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                string directory = Path.Combine(projectRoot, ".hermes", "runtime-logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(
                    directory,
                    $"npc-flow-editor-{DateTime.UtcNow:yyyyMMdd}.jsonl"
                );
                File.AppendAllText(path, flowEvent.ToJson() + Environment.NewLine);

                string line = FormatConsoleLine(flowEvent);
                if (level == NPCFlowLogLevel.Error)
                    UnityEngine.Debug.LogError(line);
                else if (level == NPCFlowLogLevel.Warning)
                    UnityEngine.Debug.LogWarning(line);
                else
                    UnityEngine.Debug.Log(line);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning(
                    $"[NPCFlow] Failed to write editor workflow log: {ex.Message}"
                );
            }
        }

        static string FormatConsoleLine(NPCFlowEvent flowEvent)
        {
            string request = string.IsNullOrWhiteSpace(flowEvent.RequestId)
                ? "-"
                : flowEvent.RequestId;
            string npc = string.IsNullOrWhiteSpace(flowEvent.NpcSlug) ? "-" : flowEvent.NpcSlug;
            string source = string.IsNullOrWhiteSpace(flowEvent.Source) ? "-" : flowEvent.Source;
            string conv = string.IsNullOrWhiteSpace(flowEvent.ConversationId) ? "" : $" conv={flowEvent.ConversationId}";
            return $"[NPCFlow] {flowEvent.Stage}/{flowEvent.Status} level={flowEvent.Level} source={source} request={request} npc={npc}{conv} durationMs={flowEvent.DurationMs} :: {flowEvent.Message}";
        }

        static string CreateSessionId()
        {
            return $"unity-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        // ── Validation helpers for EditorAttributes ──
        bool IncludeTextOrRaw() => IncludeTextSnippets || IncludeRawTextPayloads;

        bool HasValidSnippetChars() => !IncludeTextOrRaw() || MaxSnippetChars > 0;

        bool HasValidMaxEvents() => MaxInMemoryEvents > 0;

        bool HasValidRelativeLogDir() => !string.IsNullOrWhiteSpace(RelativeLogDirectory);

        bool HasValidMaxLogDays() => MaxLogDays > 0;

        bool HasValidMaxDirMB() => MaxLogDirectorySizeMB > 0;

        // ── Suppression counter helper ──
        class SuppressionCounter
        {
            public List<long> timestamps = new List<long>();
        }
    }
}
