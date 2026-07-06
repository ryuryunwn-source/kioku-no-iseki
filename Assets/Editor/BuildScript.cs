using UnityEditor;
using UnityEngine;

// コマンドラインからの Windows ビルド用。
// 例: Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BuildScript.BuildWindows
public static class BuildScript
{
    // ── Android APK（2台での実機テスト用） ──
    // メニュー「Kioku/Build Android APK」からクリック、またはバッチ:
    //   Unity.exe -batchmode -quit -projectPath <proj> -executeMethod BuildScript.BuildAndroid
    [MenuItem("Kioku/Build Android APK")]
    public static void BuildAndroid()
    {
        string[] scenes = { "Assets/Scenes/SampleScene.unity" };
        string outApk = "Build/Android/KiokuNoIseki.apk";

        // テスト配布用：カスタムキーストア未設定なら Unity のデバッグキーで署名される（サイドロード可）。
        // 必要な Android モジュール（SDK/NDK/JDK）が Unity にインストール済みであること。
        var opts = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outApk,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(opts);
        var summary = report.summary;
        UnityEngine.Debug.Log($"[BuildScript] Android result={summary.result} totalErrors={summary.totalErrors} size={summary.totalSize} out={summary.outputPath}");

        if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            string full = System.IO.Path.GetFullPath(outApk);
            UnityEngine.Debug.Log($"[BuildScript] APK生成成功: {full}");
            // ビルド先フォルダをエクスプローラで開く（対話ビルド時の受け取りを楽にする）。
            if (!Application.isBatchMode)
            {
                EditorUtility.RevealInFinder(full);
                EditorUtility.DisplayDialog("Android ビルド完了",
                    "APK を書き出しました:\n" + full + "\n\nこのファイルを各端末に転送してインストールしてください。", "OK");
            }
        }
        else
        {
            UnityEngine.Debug.LogError("[BuildScript] Android ビルド失敗。Console のエラーと Android モジュール(SDK/NDK/JDK)導入を確認してください。");
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }
    }

    [MenuItem("Kioku/Build Windows EXE")]
    public static void BuildWindows()
    {
        string[] scenes = { "Assets/Scenes/SampleScene.unity" };
        string outExe = "Build/Windows/KiokuNoIseki.exe";

        // PC1台で2窓オンライン対戦テストができるようにする設定：
        //  ・ウィンドウ表示（2つ並べて見られる）
        //  ・複数起動を許可（同じPCで2インスタンス）
        //  ・非フォーカスでも動作継続（片方を操作しても対戦が止まらない＝オンライン維持に必須）
        PlayerSettings.runInBackground = true;
        PlayerSettings.forceSingleInstance = false;
        PlayerSettings.defaultIsNativeResolution = false;
        PlayerSettings.resizableWindow = true;
        PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
        PlayerSettings.defaultScreenWidth = 1280;
        PlayerSettings.defaultScreenHeight = 720;

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
        if (summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(System.IO.Path.GetFullPath(outExe));
        }
        else if (Application.isBatchMode) EditorApplication.Exit(1);
    }
}
