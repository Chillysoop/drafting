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

        static ProjectBootstrap()
        {
            if (Application.isBatchMode) return; // CI calls Run() itself
            EditorApplication.delayCall += () =>
            {
                if (!File.Exists(BootScenePath) || GraphicsSettings.defaultRenderPipeline == null) Run();
            };
        }

        [MenuItem("Green Flag/Set Up Project")]
        public static void Run()
        {
            EnsurePlayerSettings();
            EnsureUrp();
            EnsureBootScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[GreenFlag] Project setup checked: URP, player settings, boot scene.");
        }

        static void EnsurePlayerSettings()
        {
            PlayerSettings.companyName = "Chillysoop";
            PlayerSettings.productName = "Green Flag Deck";
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
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
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
        }
    }
}
