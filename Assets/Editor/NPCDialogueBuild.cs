using System;
using System.IO;
using UnityEditor;
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
}
