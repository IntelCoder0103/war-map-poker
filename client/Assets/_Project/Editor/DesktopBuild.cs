using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace WarPoker.EditorTools
{
    /// <summary>
    /// Builds the game as a desktop app (Linux), for testing in a normal window outside the Editor.
    /// Output: Builds/Linux/WarPoker.x86_64. Only desktop settings are touched; Android settings stay as they are.
    /// Command line: Unity -batchmode -quit -projectPath client -executeMethod WarPoker.EditorTools.DesktopBuild.BuildLinux
    /// </summary>
    public static class DesktopBuild
    {
        public const string Output = "Builds/Linux/WarPoker.x86_64";

        [MenuItem("WarPoker/Build Desktop (Linux)")]
        public static void BuildLinux()
        {
            // A normal, resizable 16:9 window.
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; // keep the socket alive when the window loses focus

            Directory.CreateDirectory(Path.GetDirectoryName(Output));
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Main.unity" },
                locationPathName = Output,
                target = BuildTarget.StandaloneLinux64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError("[WarPoker] Desktop build failed: " + report.summary.result);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            Debug.Log("[WarPoker] Desktop build ready: " + Path.GetFullPath(Output));
        }
    }
}
