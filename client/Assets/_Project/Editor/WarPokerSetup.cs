using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WarPoker.EditorTools
{
    /// <summary>
    /// One-click project setup: creates Assets/_Project/Scenes/Main.unity (camera + Bootstrap),
    /// adds it to Build Settings and applies the Android player settings.
    /// Runs automatically once when the scene is missing; can be re-run from the menu.
    /// </summary>
    [InitializeOnLoad]
    public static class WarPokerSetup
    {
        public const string ScenePath = "Assets/_Project/Scenes/Main.unity";
        const string AutoRunKey = "WarPoker.SetupAutoRunDone";

        static WarPokerSetup()
        {
            // Auto-run once per editor session if the scene doesn't exist yet.
            if (SessionState.GetBool(AutoRunKey, false)) return;
            SessionState.SetBool(AutoRunKey, true);
            if (File.Exists(ScenePath)) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                if (File.Exists(ScenePath)) return;
                Debug.Log("[WarPoker] Main scene missing - running WarPoker/Setup Project.");
                Setup();
            };
        }

        [MenuItem("WarPoker/Setup Project")]
        public static void Setup()
        {
            CreateScene();
            AddSceneToBuild();
            ApplyPlayerSettings();
            AssetDatabase.SaveAssets();
            Debug.Log("[WarPoker] Setup done: scene " + ScenePath + ", Android player settings applied. " +
                      "Use WarPoker/Switch Platform To Android if the active platform isn't Android yet.");
        }

        [MenuItem("WarPoker/Switch Platform To Android")]
        public static void SwitchToAndroid()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                EditorUtility.DisplayDialog("WarPoker", "Android Build Support is not installed for this Unity version. Add it in Unity Hub.", "OK");
                return;
            }
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        static void CreateScene()
        {
            if (File.Exists(ScenePath))
            {
                // Keep an existing scene; just make sure it has a Bootstrap.
                var existing = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                if (Object.FindObjectOfType<WarPoker.Main.Bootstrap>() == null)
                {
                    new GameObject("Bootstrap").AddComponent<WarPoker.Main.Bootstrap>();
                    EditorSceneManager.MarkSceneDirty(existing);
                    EditorSceneManager.SaveScene(existing);
                }
                return;
            }

            // Don't silently throw away unsaved work in the currently open scene.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            AssetDatabase.Refresh();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.14f, 0.08f);
            cam.orthographic = true;
            cam.cullingMask = 0;

            new GameObject("Bootstrap").AddComponent<WarPoker.Main.Bootstrap>();

            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void AddSceneToBuild()
        {
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        static void ApplyPlayerSettings()
        {
            PlayerSettings.companyName = "WarPoker";
            PlayerSettings.productName = "War-Map Poker";
            PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.warpoker.game");
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.Android.bundleVersionCode = 1;

            // IL2CPP, ARM64 only, min API 24.
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = (AndroidSdkVersions)24;
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Android, ManagedStrippingLevel.Low);

            // Landscape only.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            // Network: internet permission; plain HTTP allowed for development servers.
            // For production use HTTPS/WSS and set this back to NotAllowed.
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;

            PlayerSettings.runInBackground = true;
        }
    }
}
