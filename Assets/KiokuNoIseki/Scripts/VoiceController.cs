using UnityEngine;
using UnityEngine.Windows.Speech;
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

        string[] keywords = { "いけっ", "いけ", "くらえ", "ターンエンド" };
        try
        {
            recognizer = new KeywordRecognizer(keywords);
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
        if (gameEngine == null)
        {
            var gameUI = Object.FindFirstObjectByType<GameUI>();
            if (gameUI != null) gameEngine = gameUI.engine;
        }

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