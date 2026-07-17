using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace NPCSystem.Tests
{
    public class WebGLBuildSettingsTests
    {
        const string ProjectSettingsPath = "ProjectSettings/ProjectSettings.asset";
        const string DesktopProfilePath = "Assets/Settings/Build Profiles/WebGL - Desktop - Development.asset";
        const string MobileProfilePath = "Assets/Settings/Build Profiles/WebGL - Mobile - Development.asset";
        const string LinuxProfilePath = "Assets/Settings/Build Profiles/Linux.asset";
        const string LinuxServerProfilePath = "Assets/Settings/Build Profiles/Linux Server.asset";

        static readonly string[] WebGLSettingsFiles =
        {
            ProjectSettingsPath,
            DesktopProfilePath,
            MobileProfilePath,
        };

        static readonly string[] ApiCompatibilityFiles =
        {
            ProjectSettingsPath,
            DesktopProfilePath,
            MobileProfilePath,
            LinuxProfilePath,
            LinuxServerProfilePath,
        };

        [Test]
        public void WebGLBuildSettings_KeepBrowserMemoryHeadroomEnabled()
        {
            foreach (string path in WebGLSettingsFiles)
            {
                string contents = ReadProjectFile(path);

                AssertSetting(contents, "webGLMaximumMemorySize", "4096", path);
                AssertSetting(contents, "webGLDataCaching", "1", path);
                AssertSetting(contents, "webGLNameFilesAsHashes", "1", path);
                AssertSetting(contents, "webGLAnalyzeBuildSize", "1", path);
                AssertApiCompatibilityLevel(contents, path);
            }
        }

        [Test]
        public void BuildProfiles_KeepDotNetStandardCompatibility()
        {
            foreach (string path in ApiCompatibilityFiles)
            {
                string contents = ReadProjectFile(path);

                AssertApiCompatibilityLevel(contents, path);
            }
        }

        static string ReadProjectFile(string path)
        {
            string absolutePath = Path.GetFullPath(path);
            Assert.That(File.Exists(absolutePath), Is.True, $"Missing expected project file: {path}");
            return File.ReadAllText(absolutePath);
        }

        static void AssertSetting(string contents, string key, string expectedValue, string path)
        {
            string directSetting = $"{key}: {expectedValue}";
            string buildProfileSetting = $"|   {key}: {expectedValue}";

            Assert.That(
                contents.Contains(directSetting) || contents.Contains(buildProfileSetting),
                Is.True,
                $"{path} must keep {key} at {expectedValue} for browser startup stability."
            );
        }

        static void AssertApiCompatibilityLevel(string contents, string path)
        {
            AssertSetting(contents, "apiCompatibilityLevel", "2", path);
            Assert.That(
                contents.Contains("apiCompatibilityLevel: 6")
                    || contents.Contains("|   apiCompatibilityLevel: 6"),
                Is.False,
                $"{path} must not use .NET apiCompatibilityLevel 6 for player builds."
            );
        }
    }
}
