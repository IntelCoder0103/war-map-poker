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
            UseBundledAndroidTools();
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

        /// <summary>
        /// Points Unity at the JDK / SDK / NDK installed with its Android module
        /// (Editor/Data/PlaybackEngines/AndroidPlayer/{OpenJDK,SDK,NDK}) when none is set. Unity 2022.1 on
        /// Linux doesn't always detect them on its own. Uses reflection so this file compiles without the module.
        /// </summary>
        static void UseBundledAndroidTools()
        {
            // Hub normally turns these on when it installs the Android module; a manual install leaves
            // them unset, and then 2022.1 reports "JDK not found" even though the bundled JDK is there.
            // Note: Unity 2022.1 needs OpenJDK 8 in AndroidPlayer/OpenJDK (it rejects JDK 11 as "JDK not found").
            foreach (var key in new[] { "JdkUseEmbedded", "SdkUseEmbedded", "NdkUseEmbedded", "GradleUseEmbedded" })
            {
                if (EditorPrefs.HasKey(key)) continue;
                EditorPrefs.SetBool(key, true);
                Debug.Log("[WarPoker] " + key + " = true");
            }
            System.Type type = null;
            foreach (var asm in System.AppDomain.CurrentDomain.GetAssemblies())
            {
                type = asm.GetType("UnityEditor.Android.AndroidExternalToolsSettings");
                if (type != null) break;
            }
            if (type == null) { Debug.LogWarning("[WarPoker] Android tools settings not found (is Android Build Support installed?)"); return; }
            string root = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
            SetIfMissing(type, "jdkRootPath", Path.Combine(root, "OpenJDK"));
            SetIfMissing(type, "sdkRootPath", Path.Combine(root, "SDK"));
            SetIfMissing(type, "ndkRootPath", Path.Combine(root, "NDK"));
        }

        static void SetIfMissing(System.Type type, string property, string path)
        {
            var prop = type.GetProperty(property, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop == null || !prop.CanWrite || !Directory.Exists(path)) return;
            string current = null;
            try { current = prop.GetValue(null) as string; } catch { }
            if (!string.IsNullOrEmpty(current) && Directory.Exists(current)) return;
            prop.SetValue(null, path);
            Debug.Log("[WarPoker] Android " + property + " = " + path);
        }

        static void Fail(string message)
        {
            Debug.LogError("[WarPoker] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
