using System;
using System.IO;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class NPCDialogueBuild
{
    // Server build for headless dedicated server
    [MenuItem("Build/Server (Headless)")]
    public static void BuildServer()
    {
        string outputPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Builds", "Server", "NPCServer.x86_64"));

        var options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            options = BuildOptions.CompressWithLz4HC
                | BuildOptions.ShowBuiltPlayer
        };

        var defines = new BuildPlayerOptions();
        defines.scenes = GetEnabledScenes();
        defines.locationPathName = outputPath;
        defines.target = BuildTarget.StandaloneLinux64;
        defines.subtarget = (int)StandaloneBuildSubtarget.Server;
        defines.options = BuildOptions.CompressWithLz4HC
            | BuildOptions.ShowBuiltPlayer;

        BuildReport report = BuildPipeline.BuildPlayer(defines);
        HandleBuildReport(report, "Server");
    }

    // Client build for standard standalone
    [MenuItem("Build/Client")]
    public static void BuildClient()
    {
        string outputPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Builds", "Client", "NPCClient.x86_64"));

        var options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = outputPath,
            target = BuildTarget.StandaloneLinux64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.CompressWithLz4HC
                | BuildOptions.ShowBuiltPlayer
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        HandleBuildReport(report, "Client");
    }

    // WebGL build for web client
    [MenuItem("Build/WebGL")]
    public static void BuildWebGL()
    {
        ApplyWebGLReleaseSettings();

        string outputPath = Path.GetFullPath(Path.Combine(
            Application.dataPath, "..", "Builds", "WebGL_client", "LinuxWebGLWS"));

        var options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = outputPath,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        EnsureLinuxDockerReadableArtifacts(outputPath, report.summary.result == BuildResult.Succeeded);
        HandleBuildReport(report, "WebGL");
    }

    // Both
    [MenuItem("Build/Both")]
    public static void BuildBoth()
    {
        BuildServer();
        BuildClient();
    }

    static string[] GetEnabledScenes()
    {
        var scenes = new System.Collections.Generic.List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene.enabled) scenes.Add(scene.path);
        }
        return scenes.ToArray();
    }

    static void HandleBuildReport(BuildReport report, string label)
    {
        BuildSummary summary = report.summary;
        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[NPCBuild] {label} build succeeded: {summary.outputPath} ({summary.totalSize / 1048576} MB)");
        }
        else
        {
            Debug.LogError($"[NPCBuild] {label} build FAILED: {summary.result}");
            Debug.LogError($"[NPCBuild] Errors: {string.Join("\n", report.steps)}");
            EditorApplication.Exit(1);
        }
    }

    // CLI entry points
    public static void PerformServerBuild()
    {
        BuildServer();
    }

    public static void PerformClientBuild()
    {
        BuildClient();
    }

    public static void PerformWebGLBuild()
    {
        BuildWebGL();
    }

    static void ApplyWebGLReleaseSettings()
    {
        var target = NamedBuildTarget.WebGL;

        EditorUserBuildSettings.development = false;
        EditorUserBuildSettings.connectProfiler = false;
        EditorUserBuildSettings.allowDebugging = false;

        PlayerSettings.SetIl2CppCodeGeneration(target, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.SetManagedStrippingLevel(target, ManagedStrippingLevel.High);
        PlayerSettings.stripUnusedMeshComponents = true;

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
        PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
        PlayerSettings.WebGL.maximumMemorySize = 4096;
        UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;

        Debug.Log("[NPCBuild] Applied WebGL release settings: development off, profiler off, Brotli on, debug symbols off, exception support off, max memory 4096 MB.");
    }

    static void EnsureLinuxDockerReadableArtifacts(string outputPath, bool buildSucceeded)
    {
        if (!buildSucceeded || !Directory.Exists(outputPath)) return;

        if (Application.platform != RuntimePlatform.LinuxEditor)
        {
            return;
        }

        try
        {
            using var chmod = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                Arguments = $"-R a+rX \"{outputPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            chmod?.WaitForExit();
            if (chmod is { ExitCode: 0 })
            {
                Debug.Log($"[NPCBuild] Normalized WebGL artifact permissions for Docker hosting: {outputPath}");
                return;
            }

            string error = chmod?.StandardError.ReadToEnd() ?? "unknown chmod error";
            Debug.LogWarning($"[NPCBuild] Failed to normalize WebGL artifact permissions: {error}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[NPCBuild] Failed to normalize WebGL artifact permissions: {ex.Message}");
        }
    }
}
