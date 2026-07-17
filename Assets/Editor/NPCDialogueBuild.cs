using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Profile;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

public static class NPCDialogueBuild
{
    static readonly string ProfileDir = "Assets/Settings/Build Profiles";
    static readonly string WebGLProfilePath = $"{ProfileDir}/WebGL - Desktop - Development.asset";
    static readonly string LinuxProfilePath = $"{ProfileDir}/Linux.asset";
    static readonly string LinuxServerProfilePath = $"{ProfileDir}/Linux Server.asset";

    static string ResolvePath(string relative) =>
        Path.GetFullPath(Path.Combine(Application.dataPath, "..", relative));

    static BuildProfile LoadProfile(string path)
    {
        var profile = AssetDatabase.LoadAssetAtPath<BuildProfile>(path);
        if (profile == null)
            throw new BuildFailedException($"Build profile not found: {path}");
        return profile;
    }

    // Server build for headless dedicated server
    [MenuItem("Build/Server (Headless)")]
    public static void BuildServer()
    {
        var options = new BuildPlayerWithProfileOptions
        {
            buildProfile = LoadProfile(LinuxServerProfilePath),
            locationPathName = ResolvePath("Builds/Server/NPCServer.x86_64"),
            options = BuildOptions.CompressWithLz4HC | BuildOptions.ShowBuiltPlayer,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        HandleBuildReport(report, "Server");
    }

    // Client build for standard standalone
    [MenuItem("Build/Client")]
    public static void BuildClient()
    {
        var options = new BuildPlayerWithProfileOptions
        {
            buildProfile = LoadProfile(LinuxProfilePath),
            locationPathName = ResolvePath("Builds/Client/NPCClient.x86_64"),
            options = BuildOptions.CompressWithLz4HC | BuildOptions.ShowBuiltPlayer,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        HandleBuildReport(report, "Client");
    }

    // WebGL build for web client
    [MenuItem("Build/WebGL")]
    public static void BuildWebGL()
    {
        // Force-disable Addressables auto-build during player build to avoid SBP cache errors
        var settings = UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject.Settings;
        var prevValue = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
        if (settings != null)
        {
            prevValue = settings.BuildAddressablesWithPlayerBuild;
            settings.BuildAddressablesWithPlayerBuild = UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.PlayerBuildOption.DoNotBuildWithPlayer;
            UnityEditor.EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[NPCBuild] Temporarily disabled Addressables auto-build (was {prevValue}).");
        }

        string outputPath = ResolvePath("Builds/WebGL_client/WebGL");

        EnsureWebGLApiCompatibility(WebGLProfilePath);

        // Clean stale build artifacts that can cause type tree mismatches
        // after apiCompatibilityLevel changes (e.g. old Addressables bundles
        // from .NET 6 builds are incompatible with .NET Standard 2.1 builds).
        CleanStaleBuildArtifacts(outputPath);

        var options = new BuildPlayerWithProfileOptions
        {
            buildProfile = LoadProfile(WebGLProfilePath),
            locationPathName = outputPath,
            options = BuildOptions.None,
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        EnsureLinuxDockerReadableArtifacts(
            outputPath,
            report.summary.result == BuildResult.Succeeded
        );
        HandleBuildReport(report, "WebGL");

        // Restore Addressables auto-build setting
        if (settings != null)
        {
            settings.BuildAddressablesWithPlayerBuild = prevValue;
            UnityEditor.EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log($"[NPCBuild] Restored Addressables auto-build to {prevValue}.");
        }

        EnsureWebGLApiCompatibility(WebGLProfilePath);
    }

    /// <summary>
    /// Delete stale build artifacts from previous builds that can cause
    /// runtime errors after build configuration changes.
    /// Stale Addressables bundles are incompatible when apiCompatibilityLevel
    /// changes (type tree mismatches → memory corruption).
    /// </summary>
    static void CleanStaleBuildArtifacts(string outputPath)
    {
        if (!Directory.Exists(outputPath))
            return;

        string streamingAssets = Path.Combine(outputPath, "StreamingAssets");
        string burstDebug = Path.Combine(outputPath, "Unity_Linux_LLM_BurstDebugInformation_DoNotShip");

        // Delete old Addressables bundles in StreamingAssets/aa/
        // These are NOT regenerated when m_BuildAddressablesWithPlayerBuild=0
        // and will contain stale type tree data from previous builds.
        string aaDir = Path.Combine(streamingAssets, "aa");
        if (Directory.Exists(aaDir))
        {
            Directory.Delete(aaDir, true);
            Debug.Log($"[NPCBuild] Cleaned stale Addressables bundles: {aaDir}");
        }

        // Clean Burst debug info (not needed at runtime)
        if (Directory.Exists(burstDebug))
        {
            Directory.Delete(burstDebug, true);
            Debug.Log($"[NPCBuild] Cleaned Burst debug info: {burstDebug}");
        }
    }

    // Build both Linux targets
    [MenuItem("Build/Both")]
    public static void BuildBoth()
    {
        BuildServer();
        BuildClient();
    }

    // Restart Docker containers serving the latest build
    [MenuItem("Build/Restart Docker Containers")]
    public static void RestartDocker()
    {
        string composeDir = ResolvePath("docker_webgl_client");

        try
        {
            using var proc = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "docker",
                    Arguments = $"compose -f \"{composeDir}/docker-compose.yml\" restart",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            proc?.WaitForExit(30000);
            string stdout = proc?.StandardOutput.ReadToEnd() ?? "";
            string stderr = proc?.StandardError.ReadToEnd() ?? "";
            Debug.Log($"[NPCBuild] Docker restart: {stdout.Trim()}");
            if (proc?.ExitCode != 0)
                Debug.LogWarning($"[NPCBuild] Docker restart stderr: {stderr.Trim()}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NPCBuild] Docker restart failed: {ex.Message}");
        }
    }

    static void HandleBuildReport(BuildReport report, string label)
    {
        BuildSummary summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log(
                $"[NPCBuild] {label} build succeeded: {summary.outputPath} ({summary.totalSize / 1048576} MB)"
            );
        }
        else
        {
            Debug.LogError($"[NPCBuild] {label} build FAILED: {summary.result}");
            Debug.LogError($"[NPCBuild] Errors: {string.Join("\n", report.steps)}");
            EditorApplication.Exit(1);
        }
    }

    // CLI entry points
    public static void PerformServerBuild() => BuildServer();

    public static void PerformClientBuild() => BuildClient();

    public static void PerformWebGLBuild() => BuildWebGL();

    static void EnsureWebGLApiCompatibility(string profilePath)
    {
        var currentLevel = PlayerSettings.GetApiCompatibilityLevel(NamedBuildTarget.WebGL);
        if (currentLevel != ApiCompatibilityLevel.NET_Standard)
        {
            PlayerSettings.SetApiCompatibilityLevel(
                NamedBuildTarget.WebGL,
                ApiCompatibilityLevel.NET_Standard
            );
            Debug.Log(
                $"[NPCBuild] Corrected WebGL API compatibility from {currentLevel} to "
                    + $"{ApiCompatibilityLevel.NET_Standard}."
            );
        }

        string fullProfilePath = ResolvePath(profilePath);
        if (!File.Exists(fullProfilePath))
        {
            return;
        }

        string contents = File.ReadAllText(fullProfilePath);
        string corrected = contents.Replace(
            "|   apiCompatibilityLevel: 6",
            "|   apiCompatibilityLevel: 2"
        );
        if (corrected == contents)
        {
            return;
        }

        File.WriteAllText(fullProfilePath, corrected);
        AssetDatabase.ImportAsset(profilePath);
        Debug.Log($"[NPCBuild] Corrected WebGL build profile API compatibility: {profilePath}");
    }

    static void EnsureLinuxDockerReadableArtifacts(string outputPath, bool buildSucceeded)
    {
        if (!buildSucceeded || !Directory.Exists(outputPath))
            return;
        if (Application.platform != RuntimePlatform.LinuxEditor)
            return;

        try
        {
            using var chmod = Process.Start(
                new ProcessStartInfo
                {
                    FileName = "chmod",
                    Arguments = $"-R a+rX \"{outputPath}\"",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );

            chmod?.WaitForExit();
            if (chmod is { ExitCode: 0 })
            {
                Debug.Log($"[NPCBuild] Normalized WebGL artifact permissions: {outputPath}");
                return;
            }

            string error = chmod?.StandardError.ReadToEnd() ?? "unknown chmod error";
            Debug.LogWarning($"[NPCBuild] Failed to normalize WebGL artifact permissions: {error}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning(
                $"[NPCBuild] Failed to normalize WebGL artifact permissions: {ex.Message}"
            );
        }
    }
}
