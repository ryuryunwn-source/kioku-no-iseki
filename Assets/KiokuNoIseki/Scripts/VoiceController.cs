using UnityEngine;
using UnityEngine.Windows.Speech;
using UnityEngine.InputSystem;
using System.Collections;
using KiokuNoIseki;

public class VoiceController : MonoBehaviour
{
    public GameEngine gameEngine;
    private KeywordRecognizer recognizer;
    private string requestedCommand = null;
    private readonly object lockObject = new object();
    private bool isProcessing = false;

    // シーンに手動配置していなくても必ず起動するよう自動生成する（GameUI/OnlineControllerと同じ方式）。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (Object.FindFirstObjectByType<VoiceController>() != null) return;
        var go = new GameObject("KiokuNoIseki_Voice");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<VoiceController>();
    }

    void Start()
    {
        if (gameEngine == null)
        {
            var gameUI = Object.FindFirstObjectByType<GameUI>();
            if (gameUI != null) gameEngine = gameUI.engine;
        }

        // Unityがマイクを認識しているか診断（0台ならマイク未接続/権限なし）
        var mics = Microphone.devices;
        if (mics == null || mics.Length == 0)
            Debug.LogError("🎤 マイクが1台も検出されていません。接続とWindowsのマイク権限を確認してください。");
        else
            Debug.Log($"🎤 検出マイク {mics.Length}台: {string.Join(", ", mics)}");

        string[] keywords = { "いけっ", "いけ", "くらえ", "ターンエンド" };
        try
        {
            // 音声システムのエラーを可視化（日本語認識が無い等で失敗しても無言にならないように）
            PhraseRecognitionSystem.OnError += (err) =>
                Debug.LogError($"🎤 音声認識システムのエラー: {err}（日本語の音声パックが未インストールの可能性）");
            PhraseRecognitionSystem.OnStatusChanged += (st) =>
                Debug.Log($"🎤 音声認識システム状態: {st}");

            // ConfidenceLevel.Low で聞き取りやすくする（既定Mediumだと拾えないことがある）
            recognizer = new KeywordRecognizer(keywords, ConfidenceLevel.Low);
            recognizer.OnPhraseRecognized += (args) => {
                lock (lockObject)
                {
                    requestedCommand = args.text;
                }
                Debug.Log($"🎤 認識: 「{args.text}」（信頼度 {args.confidence}）");
            };
            recognizer.Start();
            Debug.Log("🎤 音声認識システムが起動しました！（マイクに向かって「いけっ」「くらえ」「ターンエンド」）");
        }
        catch (System.Exception e)
        {
            // 日本語の音声認識パックが未インストール等でKeywordRecognizerが作れないことがある。
            Debug.LogError("🎤 音声認識の起動に失敗しました。Windowsの『音声認識(日本語)』が有効か確認してください。\n詳細: " + e.Message);
        }
    }

    void Update()
    {
        // ── キーボードによる代替入力（音声が不調でも動作確認・発表できる保険）──
        // V=「いけっ」(攻撃)  B=「くらえ」(本体直接)  N=「ターンエンド」
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.vKey.wasPressedThisFrame) lock (lockObject) requestedCommand = "いけっ";
            if (kb.bKey.wasPressedThisFrame) lock (lockObject) requestedCommand = "くらえ";
            if (kb.nKey.wasPressedThisFrame) lock (lockObject) requestedCommand = "ターンエンド";
        }

        string commandToProcess = null;
        lock (lockObject)
        {
            if (!string.IsNullOrEmpty(requestedCommand))
            {
                commandToProcess = requestedCommand;
                requestedCommand = null;
            }
        }

        if (commandToProcess != null && !isProcessing)
        {
            StartCoroutine(SafeProcessCommand(commandToProcess));
        }
    }

    private IEnumerator SafeProcessCommand(string command)
    {
        // 毎回、現在進行中の対戦エンジンを取り直す（対戦をやり直すと新しいエンジンになるため）
        var gameUI = Object.FindFirstObjectByType<GameUI>();
        if (gameUI != null && gameUI.engine != null) gameEngine = gameUI.engine;

        if (gameEngine == null) yield break;

        if (gameEngine.result != GameResult.Ongoing || gameEngine.phase != TurnPhase.Action)
        {
            yield break;
        }

        isProcessing = true;
        Debug.Log($"🎤 声「{command}」を受け付けました。");
        yield return new WaitForSeconds(0.5f);
        
        gameEngine.ProcessVoiceCommand(command);
        
        yield return new WaitForSeconds(1.5f);
        isProcessing = false;
    }

    void OnDestroy()
    {
        if (recognizer != null)
        {
            if (recognizer.IsRunning) recognizer.Stop();
            recognizer.Dispose();
        }
    }
}