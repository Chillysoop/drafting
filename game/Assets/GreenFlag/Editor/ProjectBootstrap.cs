using System.IO;
using GreenFlag.Presentation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GreenFlag.EditorTools
{
    /// <summary>
    /// Makes the project buildable from a bare checkout: URP asset, player settings and the boot scene.
    /// Runs on first open in the editor and before every CI build. Safe to run again; it only fills gaps.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectBootstrap
    {
        public const string SettingsDir = "Assets/GreenFlag/Settings";
        public const string UrpAssetPath = SettingsDir + "/URP.asset";
        public const string RendererPath = SettingsDir + "/URP_Renderer.asset";
        public const string BootScenePath = "Assets/GreenFlag/Scenes/Boot.unity";
        public const string RaceScenePath = "Assets/GreenFlag/Scenes/Race.unity";
        /// <summary>The race view clones this material for every colour, so the URP Lit shader is always in the build.</summary>
        public const string LookBasePath = "Assets/GreenFlag/Resources/GreenFlag/LookBase.mat";

        static ProjectBootstrap()
        {
            if (Application.isBatchMode) return; // CI calls Run() itself
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(BootScenePath) || !File.Exists(RaceScenePath) || GraphicsSettings.defaultRenderPipeline == null) Run();
            };
        }

        [MenuItem("Green Flag/Set Up Project")]
        public static void Run()
        {
            EnsurePlayerSettings();
            EnsureUrp();
            EnsureLookBase();
            EnsureBootScene();
            EnsureRaceScene();
            // the race view opens first; the boot scene (sim fingerprint) stays in the build for checks
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(RaceScenePath, true), new EditorBuildSettingsScene(BootScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[GreenFlag] Project setup checked: URP, player settings, boot and race scenes.");
        }

        static void EnsurePlayerSettings()
        {
            PlayerSettings.companyName = "Chillysoop";
            PlayerSettings.productName = "Green Flag Deck";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            EnsureInputBoth();
        }

        /// <summary>Step 32 reads controllers with the Input System package; the old input manager stays on as well ("Both"),
        /// so nothing that reads it breaks. Unity applies the change after an editor restart.</summary>
        static void EnsureInputBoth()
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (assets == null || assets.Length == 0) return;
            var so = new SerializedObject(assets[0]);
            var p = so.FindProperty("activeInputHandler");
            if (p == null || p.intValue == 2) return;
            p.intValue = 2;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.LogWarning("[GreenFlag] Active Input Handling set to Both for controller play. Restart the editor to apply it.");
        }

        static void EnsureUrp()
        {
            Directory.CreateDirectory(SettingsDir);
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(UrpAssetPath);
            if (asset == null)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    AssetDatabase.CreateAsset(renderer, RendererPath);
                }
                asset = UniversalRenderPipelineAsset.Create(renderer);
                AssetDatabase.CreateAsset(asset, UrpAssetPath);
            }
            if (GraphicsSettings.defaultRenderPipeline != asset) GraphicsSettings.defaultRenderPipeline = asset;
            int level = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(level, false);
        }

        static void EnsureBootScene()
        {
            if (!File.Exists(BootScenePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BootScenePath));
                var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
                }
                new GameObject("BootInfo").AddComponent<BootInfo>();
                EditorSceneManager.SaveScene(scene, BootScenePath);
            }
        }

        static void EnsureLookBase()
        {
            if (File.Exists(LookBasePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(LookBasePath));
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) { Debug.LogWarning("[GreenFlag] URP Lit shader not found; the race view will look shaders up by name."); return; }
            var mat = new Material(shader) { name = "LookBase" };
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_Cull", 0f); // double-sided: generated track meshes don't have to care about winding
            AssetDatabase.CreateAsset(mat, LookBasePath);
        }

        /// <summary>Step 29: the race view scene, a camera, a sun and the RaceView script (which builds everything else at runtime).</summary>
        static void EnsureRaceScene()
        {
            if (File.Exists(RaceScenePath)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(RaceScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            new GameObject("RaceView").AddComponent<RaceView>();
            EditorSceneManager.SaveScene(scene, RaceScenePath);
        }
    }
}
