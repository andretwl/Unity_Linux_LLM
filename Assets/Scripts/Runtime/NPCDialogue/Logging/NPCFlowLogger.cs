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
        [HelpBox("This logger provides a reusable Inspector logging pattern. To add logging to any component:\n1) Call NPCFlowLogger.FindOrCreate().Log(stage, status, level, message, source: nameof(YourClass));\n2) Or wrap work in using var scope = NPCFlowScope.Start(logger, stage, source: nameof(YourClass));\n3) Define stages in NPCFlowStage, statuses in NPCFlowStatus, levels in NPCFlowLogLevel.", MessageMode.Log, drawAbove: true)]
        [FoldoutGroup("Console Output", true, nameof(logToUnityConsole))]
        public bool logToUnityConsole = true;

        [FoldoutGroup("File Output", true, nameof(logToJsonlFile))]
        public bool logToJsonlFile = true;

        [FoldoutGroup("Text Sanitization", nameof(includeTextSnippets), nameof(includeRawTextPayloads), nameof(maxSnippetChars))]
        public bool includeTextSnippets = false;
        public bool includeRawTextPayloads = false;

        [ShowField(nameof(IncludeTextOrRaw))]
        public int maxSnippetChars = 80;

        [Title("Cache Settings")]
        public int maxInMemoryEvents = 500;

        [Title("Log File Storage")]
        [FoldoutGroup("Log File Storage", nameof(relativeLogDirectory), nameof(overrideAbsoluteLogDirectory), nameof(maxLogDays), nameof(maxLogDirectorySizeMB))]
        public string relativeLogDirectory = "NPCDialogue/Logs";
        public string overrideAbsoluteLogDirectory = "";
        public int maxLogDays = 7;
        public int maxLogDirectorySizeMB = 100;

        // ── Runtime state ──
        static NPCFlowLogger _instance;
        static int _fallbackWarningEmitted;
        static bool _cleanupRun;

        readonly object _sync = new object();
        readonly List<NPCFlowEvent> _recentEvents = new List<NPCFlowEvent>();
        int _requestCounter;
        string _sessionId = string.Empty;
        string _currentLogPath = string.Empty;
        bool _fileFailureWarned;

        // ── Inspector preview helpers (not serialised) ──
        [ShowInInspector]
        string InspectorSessionId => string.IsNullOrWhiteSpace(_sessionId) ? "not initialized" : _sessionId;

        [ShowInInspector]
        string InspectorLogFilePath => string.IsNullOrWhiteSpace(_currentLogPath) ? "not set" : _currentLogPath;

        [ShowInInspector]
        int InspectorRecentEventCount
        {
            get { lock (_sync) { return _recentEvents.Count; } }
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


        void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static NPCFlowLogger FindOrCreate()
        {
            if (_instance != null) return _instance;

            NPCFlowLogger[] sceneLoggers = FindObjectsByType<NPCFlowLogger>(FindObjectsInactive.Include);
            NPCFlowLogger sceneLogger = sceneLoggers.Length > 0 ? sceneLoggers[0] : null;
            if (sceneLogger != null)
            {
                _instance = sceneLogger;
                _instance.ApplyPlatformLoggingOverrides();
                return _instance;
            }

            var loggerObject = new GameObject("NPCFlowLogger");
            DontDestroyOnLoad(loggerObject);
            _instance = loggerObject.AddComponent<NPCFlowLogger>();
            _instance.ApplyPlatformLoggingOverrides();
            _instance.Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                "Auto-created fallback NPCFlowLogger. Add an explicit scene logger for Inspector-controlled settings.",
                source: nameof(NPCFlowLogger));
            return _instance;
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
            Dictionary<string, object> data = null)
        {
            Log(new NPCFlowEvent
            {
                stage = stage,
                status = status,
                level = level,
                message = message ?? string.Empty,
                source = source ?? string.Empty,
                requestId = requestId ?? string.Empty,
                npcSlug = npcSlug ?? string.Empty,
                conversationId = npcSlug ?? string.Empty,
                durationMs = durationMs ?? 0,
                data = data ?? new Dictionary<string, object>()
            });
        }

        public void Log(NPCFlowEvent flowEvent)
        {
            if (flowEvent == null) return;

            try
            {
                ApplyPlatformLoggingOverrides();
                PrepareEvent(flowEvent);
                AddToRingBuffer(flowEvent);
                if (logToUnityConsole) WriteUnityConsole(flowEvent);
                if (logToJsonlFile && SupportsPersistentFileLogging(Application.platform)) WriteJsonLine(flowEvent);
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref _fallbackWarningEmitted, 1) == 0)
                {
                    UnityEngine.Debug.LogWarning($"[NPCFlow] Logging failure suppressed after first warning: {ex.Message}");
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
            Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Success, NPCFlowLogLevel.Debug,
                "Manual log flush triggered from Inspector.", source: nameof(NPCFlowLogger));
        }

        [Button("Run Log Cleanup Now", buttonHeight: 20f)]
        void RunLogCleanupInspector()
        {
            string directory = !string.IsNullOrWhiteSpace(overrideAbsoluteLogDirectory)
                ? overrideAbsoluteLogDirectory.Trim()
                : Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(relativeLogDirectory) ? "NPCDialogue/Logs" : relativeLogDirectory.Trim());
            RunLogCleanup(directory);
        }

        [Button("Log Recent Event Count", buttonHeight: 20f)]
        void LogRecentEventCountInspector()
        {
            int count;
            lock (_sync) { count = _recentEvents.Count; }
            Log(NPCFlowStage.EditorWorkflow, NPCFlowStatus.Success, NPCFlowLogLevel.Info,
                $"Inspector diagnostic: {count} events in ring buffer.", source: nameof(NPCFlowLogger));
        }

        [Button("Validate All Logger Settings")]
        void ValidateAllSettingsInspector()
        {
            var issues = new System.Collections.Generic.List<string>();
            if (!HasValidMaxEvents()) issues.Add("maxInMemoryEvents must be > 0");
            if (!HasValidMaxLogDays()) issues.Add("maxLogDays must be > 0");
            if (!HasValidMaxDirMB()) issues.Add("maxLogDirectorySizeMB must be > 0");
            if (!HasValidSnippetChars()) issues.Add("maxSnippetChars must be > 0 when text sanitization is active");
            if (!HasValidRelativeLogDir()) issues.Add("relativeLogDirectory cannot be empty");

            string directory = !string.IsNullOrWhiteSpace(overrideAbsoluteLogDirectory)
                ? overrideAbsoluteLogDirectory.Trim()
                : Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(relativeLogDirectory) ? "NPCDialogue/Logs" : relativeLogDirectory.Trim());

            Log(NPCFlowStage.ConfigurationValidation, issues.Count == 0 ? NPCFlowStatus.Success : NPCFlowStatus.Warning,
                NPCFlowLogLevel.Info,
                issues.Count == 0
                    ? "All logger settings valid. Log path: " + Path.Combine(directory, "npc-flow-*.jsonl").Replace('\\', '/')
                    : "Logger settings have " + issues.Count + " issue(s): " + string.Join("; ", issues),
                source: nameof(NPCFlowLogger),
                data: new Dictionary<string, object>
                {
                    ["logToUnityConsole"] = logToUnityConsole,
                    ["logToJsonlFile"] = logToJsonlFile,
                    ["maxInMemoryEvents"] = maxInMemoryEvents,
                    ["maxLogDays"] = maxLogDays,
                    ["maxLogDirectorySizeMB"] = maxLogDirectorySizeMB,
                    ["logDirectory"] = directory,
                    ["issuesFound"] = issues.Count
                });
        }

        public void Flush()
        {
            EnsureLogPath();
        }

        public Dictionary<string, object> SummarizeText(string prefix, string text)
        {
            return NPCFlowTextSanitizer.MergeSummary(
                new Dictionary<string, object>(),
                prefix,
                text,
                includeTextSnippets || includeRawTextPayloads,
                maxSnippetChars
            );
        }

        void ApplyPlatformLoggingOverrides()
        {
            if (SupportsPersistentFileLogging(Application.platform))
            {
                return;
            }

            logToJsonlFile = false;
        }

        public static bool SupportsPersistentFileLogging(RuntimePlatform platform)
        {
            return platform != RuntimePlatform.WebGLPlayer;
        }

        void PrepareEvent(NPCFlowEvent flowEvent)
        {
            flowEvent.schemaVersion = flowEvent.schemaVersion <= 0 ? 1 : flowEvent.schemaVersion;
            if (string.IsNullOrWhiteSpace(flowEvent.timestampUtc)) flowEvent.timestampUtc = DateTime.UtcNow.ToString("o");
            if (string.IsNullOrWhiteSpace(flowEvent.sessionId)) flowEvent.sessionId = SessionId;
            flowEvent.source = flowEvent.source ?? string.Empty;
            flowEvent.requestId = flowEvent.requestId ?? string.Empty;
            flowEvent.npcSlug = flowEvent.npcSlug ?? string.Empty;
            if (string.IsNullOrWhiteSpace(flowEvent.conversationId)) flowEvent.conversationId = flowEvent.npcSlug;
            flowEvent.message = flowEvent.message ?? string.Empty;
            flowEvent.data ??= new Dictionary<string, object>();
        }

        void AddToRingBuffer(NPCFlowEvent flowEvent)
        {
            lock (_sync)
            {
                _recentEvents.Add(flowEvent);
                int maxEvents = Mathf.Max(1, maxInMemoryEvents);
                if (_recentEvents.Count > maxEvents)
                {
                    _recentEvents.RemoveRange(0, _recentEvents.Count - maxEvents);
                }
            }
        }

        void WriteUnityConsole(NPCFlowEvent flowEvent)
        {
            string line = FormatConsoleLine(flowEvent);
            switch (flowEvent.level)
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

        void WriteJsonLine(NPCFlowEvent flowEvent)
        {
            try
            {
                EnsureLogPath();
                string directory = Path.GetDirectoryName(_currentLogPath);
                if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
                File.AppendAllText(_currentLogPath, flowEvent.ToJson() + Environment.NewLine);
            }
            catch (Exception ex)
            {
                if (!_fileFailureWarned)
                {
                    _fileFailureWarned = true;
                    UnityEngine.Debug.LogWarning($"[NPCFlow] Failed to write JSONL log: {ex.Message}");
                }
            }
        }

        void EnsureLogPath()
        {
            if (!string.IsNullOrWhiteSpace(_currentLogPath)) return;

            string directory = !string.IsNullOrWhiteSpace(overrideAbsoluteLogDirectory)
                ? overrideAbsoluteLogDirectory.Trim()
                : Path.Combine(Application.persistentDataPath, string.IsNullOrWhiteSpace(relativeLogDirectory) ? "NPCDialogue/Logs" : relativeLogDirectory.Trim());

            _currentLogPath = Path.Combine(directory, $"npc-flow-{SessionId}.jsonl").Replace('\\', '/');

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
                if (!Directory.Exists(directory)) return;

                int deletedCount = 0;
                long totalBytes = 0;
                var logFiles = new List<System.IO.FileInfo>();

                foreach (string filePath in Directory.GetFiles(directory, "npc-flow-*.jsonl"))
                {
                    var fi = new System.IO.FileInfo(filePath);
                    totalBytes += fi.Length;
                    logFiles.Add(fi);
                }

                // Phase 1: delete files older than maxLogDays
                DateTime cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, maxLogDays));
                foreach (var fi in logFiles)
                {
                    if (fi.LastWriteTimeUtc < cutoff)
                    {
                        try { fi.Delete(); deletedCount++; }
                        catch { /* best effort */ }
                    }
                }

                // Phase 2: if still over size limit, delete oldest until under
                if (maxLogDirectorySizeMB > 0)
                {
                    long maxBytes = (long)maxLogDirectorySizeMB * 1024L * 1024L;
                    long currentBytes = 0;
                    var remaining = new List<System.IO.FileInfo>();
                    foreach (string filePath in Directory.GetFiles(directory, "npc-flow-*.jsonl"))
                    {
                        var fi = new System.IO.FileInfo(filePath);
                        currentBytes += fi.Length;
                        remaining.Add(fi);
                    }

                    if (currentBytes > maxBytes)
                    {
                        remaining.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
                        foreach (var fi in remaining)
                        {
                            if (currentBytes <= maxBytes) break;
                            try
                            {
                                long len = fi.Length;
                                fi.Delete();
                                currentBytes -= len;
                                deletedCount++;
                            }
                            catch { /* best effort */ }
                        }
                    }
                }

                if (deletedCount > 0)
                {
                    Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Success, NPCFlowLogLevel.Debug,
                        $"Cleaned up {deletedCount} old log file(s) from {directory}.",
                        source: nameof(NPCFlowLogger),
                        data: new Dictionary<string, object>
                        {
                            ["directory"] = directory,
                            ["deletedCount"] = deletedCount
                        });
                }
            }
            catch (Exception ex)
            {
                Log(NPCFlowStage.SceneBootstrap, NPCFlowStatus.Warning, NPCFlowLogLevel.Warning,
                    $"Log cleanup failed: {ex.Message}",
                    source: nameof(NPCFlowLogger));
            }
        }

        public static void LogEditorWorkflow(
            NPCFlowStatus status,
            NPCFlowLogLevel level,
            string message,
            string source,
            Dictionary<string, object> data = null)
        {
            var flowEvent = new NPCFlowEvent
            {
                sessionId = $"editor-{DateTime.UtcNow:yyyyMMdd}",
                source = source ?? "EditorWorkflow",
                stage = NPCFlowStage.EditorWorkflow,
                status = status,
                level = level,
                message = message ?? string.Empty,
                data = data ?? new Dictionary<string, object>()
            };

            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
                string directory = Path.Combine(projectRoot, ".hermes", "runtime-logs");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, $"npc-flow-editor-{DateTime.UtcNow:yyyyMMdd}.jsonl");
                File.AppendAllText(path, flowEvent.ToJson() + Environment.NewLine);

                string line = FormatConsoleLine(flowEvent);
                if (level == NPCFlowLogLevel.Error) UnityEngine.Debug.LogError(line);
                else if (level == NPCFlowLogLevel.Warning) UnityEngine.Debug.LogWarning(line);
                else UnityEngine.Debug.Log(line);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning($"[NPCFlow] Failed to write editor workflow log: {ex.Message}");
            }
        }

        static string FormatConsoleLine(NPCFlowEvent flowEvent)
        {
            string request = string.IsNullOrWhiteSpace(flowEvent.requestId) ? "-" : flowEvent.requestId;
            string npc = string.IsNullOrWhiteSpace(flowEvent.npcSlug) ? "-" : flowEvent.npcSlug;
            string source = string.IsNullOrWhiteSpace(flowEvent.source) ? "-" : flowEvent.source;
            return $"[NPCFlow] {flowEvent.stage}/{flowEvent.status} level={flowEvent.level} source={source} request={request} npc={npc} durationMs={flowEvent.durationMs} :: {flowEvent.message}";
        }

        static string CreateSessionId()
        {
            return $"unity-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }


        // ── Validation helpers for EditorAttributes ──
        bool IncludeTextOrRaw() => includeTextSnippets || includeRawTextPayloads;

        bool HasValidSnippetChars() => !IncludeTextOrRaw() || maxSnippetChars > 0;

        bool HasValidMaxEvents() => maxInMemoryEvents > 0;

        bool HasValidRelativeLogDir() => !string.IsNullOrWhiteSpace(relativeLogDirectory);

        bool HasValidMaxLogDays() => maxLogDays > 0;

        bool HasValidMaxDirMB() => maxLogDirectorySizeMB > 0;
    }
}
