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

    void Start()
    {
        if (gameEngine == null)
        {
            var gameUI = Object.FindFirstObjectByType<GameUI>();
            if (gameUI != null) gameEngine = gameUI.engine;
        }

        string[] keywords = { "いけっ", "くらえ", "ターンエンド" };
        recognizer = new KeywordRecognizer(keywords);
        recognizer.OnPhraseRecognized += (args) => {
            lock (lockObject)
            {
                requestedCommand = args.text;
            }
        };
        recognizer.Start();
        Debug.Log("🎤 音声認識システムが起動しました！");
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