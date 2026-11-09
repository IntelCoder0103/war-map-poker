using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WarPoker.EditorTools
{
    /// <summary>
    /// Builds an installable test APK: Builds/Android/WarPoker.apk (IL2CPP, ARM64, signed with the debug key).
    /// Needs Unity's Android Build Support (with its SDK, NDK and OpenJDK).
    /// Command line: Unity -batchmode -quit -buildTarget Android -projectPath client
    ///               -executeMethod WarPoker.EditorTools.AndroidBuild.BuildApk
    /// </summary>
    public static class AndroidBuild
    {
        public const string Output = "Builds/Android/WarPoker.apk";

        [MenuItem("WarPoker/Build Android APK")]
        public static void BuildApk()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                Fail("Android Build Support is not installed for this Unity version.");
                return;
            }
            WarPokerSetup.ApplyPlayerSettings();
            EditorUserBuildSettings.buildAppBundle = false; // a plain APK to install directly
            EditorUserBuildSettings.development = false;
            PlayerSettings.Android.useCustomKeystore = false; // debug-signed: fine for testing, not for the store

            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Main.unity" },
                locationPathName = Output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            });
            if (report.summary.result == BuildResult.Succeeded)
                Debug.Log("[WarPoker] APK built: " + Path.GetFullPath(Output) + " (" + report.summary.totalSize / (1024 * 1024) + " MB)");
            else
                Fail("Android build failed: " + report.summary.result);
        }

        static void Fail(string message)
        {
            Debug.LogError("[WarPoker] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
