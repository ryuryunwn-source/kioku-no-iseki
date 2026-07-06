using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace KiokuNoIseki
{
    // 写真を見て写し身の「真名」を生成する（15-5）。
    // 方式：Google Vision でラベル抽出 → Groq(LLM) でカッコいい日本語名を1つ生成。
    // APIキーは Resources/writeshi_api.txt（2行：1行目=Vision, 2行目=Groq）から読む。
    //   → キーをソース/gitに含めないための外部化。ファイルが無ければ即オフライン命名にフォールバックする。
    // オフライン/失敗時は CardGenerator 側の候補名（PhotoWriteshi.Build に null を渡す）で決定論的に命名する。
    public static class WriteshiNamingService
    {
        [Serializable] class GroqMessage { public string content; }
        [Serializable] class GroqChoice { public GroqMessage message; }
        [Serializable] class GroqResponse { public GroqChoice[] choices; }

        static bool s_keysLoaded;
        static string s_visionKey, s_groqKey;

        static void EnsureKeys()
        {
            if (s_keysLoaded) return;
            s_keysLoaded = true;
            var ta = Resources.Load<TextAsset>("writeshi_api");
            if (ta == null) return;
            var lines = ta.text.Replace("\r", "").Split('\n');
            if (lines.Length > 0) s_visionKey = lines[0].Trim();
            if (lines.Length > 1) s_groqKey = lines[1].Trim();
        }

        public static bool HasKeys()
        {
            EnsureKeys();
            return !string.IsNullOrEmpty(s_visionKey) && !string.IsNullOrEmpty(s_groqKey);
        }

        // 真名を非同期取得。取れれば onDone(name)、取れなければ onDone(null)（呼び出し側でフォールバック）。
        public static IEnumerator RequestTrueName(Texture2D tex, Action<string> onDone)
        {
            EnsureKeys();
            if (tex == null || !HasKeys()) { onDone?.Invoke(null); yield break; }

            byte[] jpg;
            try { jpg = tex.EncodeToJPG(); }
            catch { onDone?.Invoke(null); yield break; }

            // 1) Vision: ラベル検出
            List<string> labels = null;
            yield return PostVision(jpg, res => labels = res);
            if (labels == null || labels.Count == 0) { onDone?.Invoke(null); yield break; }

            // 2) Groq: 名前生成
            string name = null;
            yield return PostGroq(BuildPrompt(labels), res => name = res);
            onDone?.Invoke(string.IsNullOrEmpty(name) ? null : name);
        }

        static IEnumerator PostVision(byte[] imageBytes, Action<List<string>> onDone)
        {
            string url = "https://vision.googleapis.com/v1/images:annotate?key=" + s_visionKey;
            string b64 = Convert.ToBase64String(imageBytes);
            string json =
                "{\"requests\":[{\"image\":{\"content\":\"" + b64 + "\"}," +
                "\"features\":[{\"type\":\"LABEL_DETECTION\",\"maxResults\":5}]}]}";

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[Writeshi] Vision失敗: " + req.error);
                    onDone(null); yield break;
                }
                onDone(ExtractLabels(req.downloadHandler.text));
            }
        }

        static IEnumerator PostGroq(string prompt, Action<string> onDone)
        {
            string url = "https://api.groq.com/openai/v1/chat/completions";
            string safe = prompt.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
            string json = "{\"model\":\"llama-3.3-70b-versatile\",\"messages\":[" +
                          "{\"role\":\"user\",\"content\":\"" + safe + "\"}]}";

            using (var req = new UnityWebRequest(url, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.SetRequestHeader("Authorization", "Bearer " + s_groqKey);
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning("[Writeshi] Groq失敗: " + req.error);
                    onDone(null); yield break;
                }
                try
                {
                    var r = JsonUtility.FromJson<GroqResponse>(req.downloadHandler.text);
                    string name = (r != null && r.choices != null && r.choices.Length > 0)
                        ? r.choices[0].message.content.Trim() : null;
                    onDone(Sanitize(name));
                }
                catch (Exception e) { Debug.LogWarning("[Writeshi] Groq解析失敗: " + e.Message); onDone(null); }
            }
        }

        static List<string> ExtractLabels(string json)
        {
            var labels = new List<string>();
            var parts = json.Split(new[] { "\"description\":" }, StringSplitOptions.None);
            for (int i = 1; i < parts.Length && i <= 5; i++)
            {
                var seg = parts[i].Split('"');
                if (seg.Length > 1) labels.Add(seg[1]);
            }
            return labels;
        }

        static string BuildPrompt(List<string> labels) =>
            "以下の特徴を持つモンスターの名前を1つだけ考えてください。\n" +
            "条件：ゲーム向け・かっこいい・日本語・12文字以内・説明不要・名前のみ出力。\n" +
            "特徴: " + string.Join(", ", labels);

        // 改行や余計な記号を落とし、長すぎる場合は切り詰める（12文字）。
        static string Sanitize(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            name = name.Replace("\r", "").Replace("\n", " ").Trim().Trim('「', '」', '"', '。', ' ');
            if (name.Length > 12) name = name.Substring(0, 12);
            return name.Length == 0 ? null : name;
        }
    }
}
