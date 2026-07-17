using System.IO;
using Unity.Web.Stripping.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public sealed class WebGLStripPostBuild : IPreprocessBuildWithReport
{
    const string SettingsPath = "Assets/DefaultSubmoduleStrippingSettings.asset";

    public int callbackOrder => -100;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL)
        {
            return;
        }

        var settings = AssetDatabase.LoadAssetAtPath<SubmoduleStrippingSettings>(SettingsPath);
        if (settings == null)
        {
            throw new BuildFailedException(
                $"WebGL submodule stripping settings were not found at '{SettingsPath}'."
            );
        }

        if (settings.SubmodulesToStrip == null || settings.SubmodulesToStrip.Count == 0)
        {
            throw new BuildFailedException(
                $"WebGL submodule stripping settings at '{SettingsPath}' contain no submodules."
            );
        }

        StrippingProjectSettings.ActiveSettings = settings;
        StrippingProjectSettings.StripAutomaticallyAfterBuild = true;
        Debug.Log(
            $"[WebGLStrip] Enabled package-managed stripping with "
                + $"{settings.SubmodulesToStrip.Count} configured submodule(s)."
        );
    }
}

public sealed class WebGLStripWorkerMetadataPostBuild : IPostprocessBuildWithReport
{
    static readonly string[] MetadataFiles = { "functions.json", "labels.json" };

    public int callbackOrder => 1000;

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL)
        {
            return;
        }

        string buildPath = Path.Combine(report.summary.outputPath, "Build");
        string workerBuildPath = Path.Combine(buildPath, "Build");
        if (!Directory.Exists(buildPath))
        {
            return;
        }

        foreach (string metadataFile in MetadataFiles)
        {
            string sourcePath = Path.Combine(buildPath, metadataFile);
            if (!File.Exists(sourcePath))
            {
                continue;
            }

            Directory.CreateDirectory(workerBuildPath);
            File.Copy(sourcePath, Path.Combine(workerBuildPath, metadataFile), true);
            Debug.Log(
                $"[WebGLStrip] Mirrored {metadataFile} for threaded worker metadata lookup."
            );
        }
    }
}
