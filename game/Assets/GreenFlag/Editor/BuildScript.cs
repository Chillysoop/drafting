using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GreenFlag.EditorTools
{
    /// <summary>
    /// Command-line builds. CI (GameCI) calls BuildWindows with -customBuildPath; locally use the
    /// "Green Flag/Build Windows" menu or:
    ///   Unity -batchmode -quit -projectPath game -executeMethod GreenFlag.EditorTools.BuildScript.BuildWindows
    /// </summary>
    public static class BuildScript
    {
        const string DefaultPath = "Builds/Windows/GreenFlagDeck.exe";

        [MenuItem("Green Flag/Build Windows")]
        public static void BuildWindows()
        {
            ProjectBootstrap.Run();

            string version = Arg("-buildVersion") ?? Environment.GetEnvironmentVariable("GREENFLAG_BUILD_VERSION");
            if (!string.IsNullOrEmpty(version)) PlayerSettings.bundleVersion = version;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectBootstrap.RaceScenePath, ProjectBootstrap.BootScenePath },
                locationPathName = Arg("-customBuildPath") ?? DefaultPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[GreenFlag] Windows build {summary.result}: {summary.totalSize / (1024 * 1024)} MB, " +
                      $"{summary.totalErrors} errors, {summary.totalTime.TotalSeconds:F0} s -> {options.locationPathName}");
            if (Application.isBatchMode) EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }

        static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
