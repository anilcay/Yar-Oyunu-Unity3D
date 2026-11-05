using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LugeRush.Editor
{
    /// <summary>
    /// Windows standalone build to C:\Dev\YarisOyunu\Builds\LugeRush (outside git).
    /// Batch: -executeMethod LugeRush.Editor.MenuBuildPlayer.BuildWindows
    /// </summary>
    public static class MenuBuildPlayer
    {
        public static string OutputDir => Path.GetFullPath(Path.Combine(Application.dataPath, "../../Builds/LugeRush"));

        /// <summary>Rebuild menus/network assets, then the player (one batch launch).</summary>
        public static void BuildMenusAndWindows()
        {
            MenuSceneBuilder.BuildAll();
            BuildWindows();
        }

        [MenuItem("Luge Rush/Build Windows Player")]
        public static void BuildWindows()
        {
            MenuSceneBuilder.ApplyBuildOrder();
            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            Directory.CreateDirectory(OutputDir);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDir, "LugeRush.exe"),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            Debug.Log($"[MenuBuildPlayer] {s.result}: {s.outputPath} ({s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime.TotalSeconds:0}s) scenes: {string.Join(", ", scenes)}");
            if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
        }
    }
}
