using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEngine;

namespace NPCSystem.Editor.Tools
{
    /// <summary>
    /// SettingsGuard — holistic project settings verifier.
    ///
    /// CLI usage (batchmode):
    ///   Unity -batchmode -quit -projectPath . -logFile Logs/settings-guard.log \
    ///       -executeMethod NPCSystem.Editor.Tools.SettingsGuard.Verify
    ///
    ///   Unity -batchmode -quit -projectPath . -logFile Logs/settings-guard.log \
    ///       -executeMethod NPCSystem.Editor.Tools.SettingsGuard.FixAndVerify
    ///
    ///   Unity -batchmode -quit -projectPath . -logFile Logs/settings-guard.log \
    ///       -executeMethod NPCSystem.Editor.Tools.SettingsGuard.FixThenBuild
    /// </summary>
    public static class SettingsGuard
    {
        const string ConfigPath = "Assets/Settings/SettingsGuardConfig.asset";
        const string LogPrefix = "[SettingsGuard]";

        // ── Entry points for CLI batchmode ──────────────────────────────────

        /// <summary>CLI: verify only, exit code 0 = pass, 1 = fail</summary>
        public static void Verify()
        {
            var config = LoadOrCreateConfig();
            var report = RunAllChecks(config);
            Debug.Log(report.GetSummary());

            if (report.HasErrors)
            {
                Debug.LogError($"{LogPrefix} VERIFY FAILED — {report.ErrorCount} error(s), {report.WarningCount} warning(s)");
                if (config.autoFixOnVerify)
                {
                    Debug.Log($"{LogPrefix} Auto-fix enabled — attempting repairs...");
                    FixAll(config);
                }
                EditorApplication.Exit(1);
            }
            else
            {
                Debug.Log($"{LogPrefix} VERIFY PASSED — all checks green.");
                EditorApplication.Exit(0);
            }
        }

        /// <summary>CLI: fix then verify, exit code = verify result</summary>
        public static void FixAndVerify()
        {
            var config = LoadOrCreateConfig();
            FixAll(config);
            var report = RunAllChecks(config);
            Debug.Log(report.GetSummary());
            EditorApplication.Exit(report.HasErrors ? 1 : 0);
        }

        /// <summary>CLI: fix, verify, then build if pass</summary>
        public static void FixThenBuild()
        {
            var config = LoadOrCreateConfig();
            FixAll(config);
            var report = RunAllChecks(config);

            if (report.HasErrors)
            {
                Debug.LogError($"{LogPrefix} FixThenBuild ABORTED — {report.ErrorCount} error(s) remain");
                EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"{LogPrefix} All checks passed — triggering build...");
            // Invoke build pipeline with BuildPlayerWithProfileOptions (future work)
            EditorApplication.Exit(0);
        }

        // ── Menu items ──────────────────────────────────────────────────────

        [MenuItem("Tools/SettingsGuard/Verify", false, 100)]
        static void VerifyMenu() => Debug.Log(RunAllChecks(LoadOrCreateConfig()).GetSummary());

        [MenuItem("Tools/SettingsGuard/Fix and Verify", false, 101)]
        static void FixAndVerifyMenu()
        {
            var config = LoadOrCreateConfig();
            FixAll(config);
            Debug.Log(RunAllChecks(config).GetSummary());
        }

        [MenuItem("Tools/SettingsGuard/Create Default Config", false, 102)]
        static void CreateConfigMenu()
        {
            CreateDefaultConfig();
            Debug.Log($"{LogPrefix} Default config created at {ConfigPath}");
        }

        // ── Core logic ──────────────────────────────────────────────────────

        static SettingsGuardConfig LoadOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<SettingsGuardConfig>(ConfigPath);
            if (config != null)
                return config;
            return CreateDefaultConfig();
        }

        static SettingsGuardConfig CreateDefaultConfig()
        {
            var config = ScriptableObject.CreateInstance<SettingsGuardConfig>();
            config.name = "SettingsGuardConfig";

            // Default overrides
            config.platformOverrides = new List<PlatformCompatOverride>
            {
                new PlatformCompatOverride { platformName = "WebGL", requiredLevel = ApiCompatibilityLevel.NET_Standard },
                new PlatformCompatOverride { platformName = "Standalone", requiredLevel = ApiCompatibilityLevel.NET_Standard },
                new PlatformCompatOverride { platformName = "Server", requiredLevel = ApiCompatibilityLevel.NET_Standard },
            };

            config.scriptingDefines = new List<PlatformDefines>
            {
                new PlatformDefines { platformName = "WebGL", defines = "SENTIS_ANALYTICS_ENABLED" },
            };

            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        // ── Checks ──────────────────────────────────────────────────────────

        public static SettingsReport RunAllChecks(SettingsGuardConfig config)
        {
            var report = new SettingsReport();

            // 1. ApiCompatibilityLevel default
            CheckApiCompat(report, NamedBuildTarget.Unknown, config.requiredApiCompatibilityLevel);

            // 2. Per-platform overrides
            foreach (var ovr in config.platformOverrides)
            {
                NamedBuildTarget? target = NamedBuildTargetFromString(ovr.platformName);
                if (target.HasValue)
                    CheckApiCompat(report, target.Value, ovr.requiredLevel);
                else
                    report.AddWarning($"Unknown platform '{ovr.platformName}' in config overrides");
            }

            // 3. Editor assemblies level (via dedicated methods in 6000.5)
            CheckEditorAssembliesLevel(report, config.requiredEditorAssembliesLevel);

            // 4. Scripting defines
            foreach (var pd in config.scriptingDefines)
            {
                NamedBuildTarget? target = NamedBuildTargetFromString(pd.platformName);
                if (!target.HasValue)
                    continue;

                var currentDefines = PlayerSettings.GetScriptingDefineSymbols(target.Value);
                var expectedDefines = pd.defines;
                if (!string.IsNullOrEmpty(expectedDefines))
                {
                    var currentSet = new HashSet<string>(currentDefines.Split(';')
                        .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));
                    var expectedSet = new HashSet<string>(expectedDefines.Split(';')
                        .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));

                    var missing = expectedSet.Except(currentSet).ToList();
                    if (missing.Count > 0)
                    {
                        report.AddWarning(
                            $"Platform '{pd.platformName}' missing scripting defines: {string.Join(", ", missing)}"
                        );
                    }
                }
            }

            // 5. Scenes
            var editorScenes = EditorBuildSettings.scenes;
            if (editorScenes == null || editorScenes.Length == 0)
            {
                report.AddWarning("No scenes in EditorBuildSettings");
            }

            // 6. Build Profiles
            if (config.verifyBuildProfileSettings)
            {
                CheckBuildProfiles(report);
            }

            return report;
        }

        static void CheckApiCompat(SettingsReport report, NamedBuildTarget target, ApiCompatibilityLevel expected)
        {
            try
            {
                var current = PlayerSettings.GetApiCompatibilityLevel(target);
                if (current != expected)
                {
                    var targetName = target.TargetName;
                    report.AddError(
                        $"ApiCompatibilityLevel for '{targetName}' is '{current}' ({(int)current}), expected '{expected}' ({(int)expected}). " +
                        $"This will break Unity Transport/Serialization/RP Core packages on build."
                    );
                }
            }
            catch (Exception ex)
            {
                report.AddWarning($"Could not check ApiCompatibilityLevel for '{target.TargetName}': {ex.Message}");
            }
        }

        static void CheckEditorAssembliesLevel(SettingsReport report, EditorAssembliesCompatibilityLevel expected)
        {
            try
            {
                var current = PlayerSettings.GetEditorAssembliesCompatibilityLevel();
                if (current != expected)
                {
                    report.AddWarning(
                        $"Editor Assemblies Compatibility Level is '{current}', expected '{expected}'. " +
                        "Consider updating SettingsGuardConfig.requiredEditorAssembliesLevel."
                    );
                }
            }
            catch
            {
                // Silently skip — this API isn't available in all Unity versions
            }
        }

        static void CheckBuildProfiles(SettingsReport report)
        {
            try
            {
                var profiles = BuildProfile.GetAllBuildProfiles();
                if (profiles == null || profiles.Count == 0)
                {
                    report.AddWarning("No BuildProfile assets found. Project may be using classic platform profiles.");
                    return;
                }

                foreach (var profile in profiles)
                {
                    var path = AssetDatabase.GetAssetPath(profile);
                    var scenes = profile.GetScenesForBuild();
                    if (scenes == null || scenes.Length == 0)
                    {
                        report.AddWarning($"BuildProfile '{profile.name}' ({path}) has no scenes assigned");
                    }
                }
            }
            catch (Exception ex)
            {
                report.AddWarning($"Could not enumerate BuildProfiles: {ex.Message}");
            }
        }

        // ── Auto-fix ────────────────────────────────────────────────────────

        public static int FixAll(SettingsGuardConfig config)
        {
            int fixes = 0;

            // 1. Fix default ApiCompatibilityLevel
            if (PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.Unknown) != config.requiredApiCompatibilityLevel)
            {
                PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Unknown, config.requiredApiCompatibilityLevel);
                Debug.Log($"{LogPrefix} FIXED: Default ApiCompatibilityLevel → {config.requiredApiCompatibilityLevel}");
                fixes++;
            }

            // 2. Fix per-platform overrides
            foreach (var ovr in config.platformOverrides)
            {
                NamedBuildTarget? target = NamedBuildTargetFromString(ovr.platformName);
                if (!target.HasValue)
                    continue;

                try
                {
                    if (PlayerSettings.GetApiCompatibilityLevel(target.Value) != ovr.requiredLevel)
                    {
                        PlayerSettings.SetApiCompatibilityLevel(target.Value, ovr.requiredLevel);
                        Debug.Log($"{LogPrefix} FIXED: '{ovr.platformName}' ApiCompatibilityLevel → {ovr.requiredLevel}");
                        fixes++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"{LogPrefix} Could not fix '{ovr.platformName}': {ex.Message}");
                }
            }

            // 3. Fix editor assemblies level
            {
                var current = PlayerSettings.GetEditorAssembliesCompatibilityLevel();
                if (current != config.requiredEditorAssembliesLevel)
                {
                    PlayerSettings.SetEditorAssembliesCompatibilityLevel(config.requiredEditorAssembliesLevel);
                    Debug.Log($"{LogPrefix} FIXED: EditorAssembliesCompatibilityLevel → {config.requiredEditorAssembliesLevel}");
                    fixes++;
                }
            }

            // 4. Fix scripting defines
            foreach (var pd in config.scriptingDefines)
            {
                NamedBuildTarget? target = NamedBuildTargetFromString(pd.platformName);
                if (!target.HasValue)
                    continue;

                var currentDefines = PlayerSettings.GetScriptingDefineSymbols(target.Value);
                var currentSet = new HashSet<string>(currentDefines.Split(';')
                    .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));
                var expectedSet = new HashSet<string>(pd.defines.Split(';')
                    .Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s)));

                var missing = expectedSet.Except(currentSet).ToList();
                if (missing.Count > 0)
                {
                    var merged = currentSet.Concat(missing);
                    PlayerSettings.SetScriptingDefineSymbols(target.Value, string.Join(";", merged));
                    Debug.Log($"{LogPrefix} FIXED: Added defines to '{pd.platformName}': {string.Join(", ", missing)}");
                    fixes++;
                }
            }

            AssetDatabase.SaveAssets();
            return fixes;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        static NamedBuildTarget? NamedBuildTargetFromString(string name)
        {
            var type = typeof(NamedBuildTarget);
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.FieldType == typeof(NamedBuildTarget))
            {
                return (NamedBuildTarget)field.GetValue(null);
            }
            return null;
        }
    }

    // ── Report ──────────────────────────────────────────────────────────────

    public class SettingsReport
    {
        readonly List<string> _errors = new List<string>();
        readonly List<string> _warnings = new List<string>();

        public bool HasErrors => _errors.Count > 0;
        public int ErrorCount => _errors.Count;
        public int WarningCount => _warnings.Count;

        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> Warnings => _warnings;

        public void AddError(string message) => _errors.Add(message);
        public void AddWarning(string message) => _warnings.Add(message);

        public string GetSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════");
            sb.AppendLine("  SettingsGuard — Project Settings Report");
            sb.AppendLine("═══════════════════════════════════════════════");

            if (_errors.Count == 0 && _warnings.Count == 0)
            {
                sb.AppendLine("  ✅ ALL CHECKS PASSED");
            }
            else
            {
                if (_errors.Count > 0)
                {
                    sb.AppendLine($"\n  ❌ ERRORS ({_errors.Count}):");
                    for (int i = 0; i < _errors.Count; i++)
                        sb.AppendLine($"    {i + 1}. {_errors[i]}");
                }

                if (_warnings.Count > 0)
                {
                    sb.AppendLine($"\n  ⚠ WARNINGS ({_warnings.Count}):");
                    for (int i = 0; i < _warnings.Count; i++)
                        sb.AppendLine($"    {i + 1}. {_warnings[i]}");
                }
            }

            sb.AppendLine("\n═══════════════════════════════════════════════");
            return sb.ToString();
        }
    }
}
