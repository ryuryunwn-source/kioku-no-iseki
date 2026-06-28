using UnityEditor;
using UnityEngine;

// コマンドラインからの Windows ビルド用。
// 例: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BuildScript.BuildWindows
public static class BuildScript
{
    public static void BuildWindows()
    {
        string[] scenes = { "Assets/Scenes/SampleScene.unity" };
        string outExe = "Build/Windows/KiokuNoIseki.exe";

        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outExe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;
        Debug.Log($"[BuildScript] result={summary.result} totalErrors={summary.totalErrors} size={summary.totalSize} out={summary.outputPath}");
        if (summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
