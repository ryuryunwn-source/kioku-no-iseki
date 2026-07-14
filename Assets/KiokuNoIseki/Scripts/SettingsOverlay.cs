using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace KiokuNoIseki
{
    // Escapeで開閉する設定オーバーレイ。音量調整・BGM ON/OFF・タイトルに戻る・デスクトップに戻る。
    // 常駐（自動起動）で、オフライン/オンラインどちらの画面でも最前面に開く。
    public class SettingsOverlay : MonoBehaviour
    {
        Canvas canvas;
        Transform root;
        Font font;
        bool open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<SettingsOverlay>() != null) return;
            var go = new GameObject("KiokuNoIseki_Settings");
            DontDestroyOnLoad(go);
            go.AddComponent<SettingsOverlay>();
        }

        void Awake() { font = LoadJpFont(); }

        Font LoadJpFont()
        {
            string[] names = { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS Gothic" };
            foreach (var n in names) { var f = Font.CreateDynamicFontFromOSFont(n, 20); if (f != null) return f; }
            return Font.CreateDynamicFontFromOSFont("Arial", 20);
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Toggle();
        }

        void Toggle() { if (open) Close(); else Open(); }

        void Open()
        {
            open = true;
            if (canvas == null) Build();
            canvas.gameObject.SetActive(true);
            Redraw();
        }

        void Close()
        {
            open = false;
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        void Build()
        {
            var cgo = new GameObject("SettingsCanvas");
            DontDestroyOnLoad(cgo);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500; // 最前面（オンラインUI=100 より上）
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 1f;
            cgo.AddComponent<GraphicRaycaster>();
            root = cgo.transform;
        }

        void Redraw()
        {
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);

            // 全画面の暗幕（クリックを裏に通さない）
            var dim = Panel(new Color(0.03f, 0.04f, 0.07f, 0.85f), Vector2.zero, Vector2.zero, true);

            // パネル
            Panel(new Color(0.10f, 0.11f, 0.16f, 0.99f), Vector2.zero, new Vector2(540, 480));

            Text("設定", 0, 200, 400, 44, 30, Color.white, TextAnchor.MiddleCenter);

            var am = AudioManager.Instance;

            // BGM音量
            Text("BGM音量", -170, 120, 220, 34, 20, new Color(0.9f, 0.9f, 0.95f), TextAnchor.MiddleLeft);
            StepRow(120, () => am != null ? am.BgmVolume : 0.5f, v => am?.SetBgmVolume(v));

            // 効果音音量
            Text("効果音音量", -170, 60, 220, 34, 20, new Color(0.9f, 0.9f, 0.95f), TextAnchor.MiddleLeft);
            StepRow(60, () => am != null ? am.SfxVolume : 0.8f, v => am?.SetSfxVolume(v));

            // BGM ON/OFF
            bool muted = am != null && am.BgmMuted;
            Text("BGM", -170, 0, 220, 34, 20, new Color(0.9f, 0.9f, 0.95f), TextAnchor.MiddleLeft);
            var muteBtn = Button(muted ? "OFF" : "ON", 120, 0, 120, 40,
                muted ? new Color(0.5f, 0.33f, 0.33f) : new Color(0.30f, 0.5f, 0.40f));
            muteBtn.onClick.AddListener(() => { am?.ToggleBgmMute(); Redraw(); });

            // 操作
            Button("閉じる（Esc）", 0, -70, 320, 46, new Color(0.34f, 0.40f, 0.52f)).onClick.AddListener(Close);
            Button("タイトルに戻る", 0, -128, 320, 46, new Color(0.46f, 0.40f, 0.30f)).onClick.AddListener(ReturnToTitle);
            Button("デスクトップに戻る（終了）", 0, -186, 320, 46, new Color(0.5f, 0.30f, 0.30f)).onClick.AddListener(QuitToDesktop);
        }

        // −  NN%  ＋ の音量調整行
        void StepRow(float y, System.Func<float> get, System.Action<float> set)
        {
            const float step = 0.1f;
            var minus = Button("−", 40, y, 44, 34, new Color(0.30f, 0.35f, 0.45f));
            minus.onClick.AddListener(() => { set(Mathf.Clamp01(get() - step)); Redraw(); });
            Text($"{Mathf.RoundToInt(get() * 100)}%", 120, y, 90, 34, 20, Color.white, TextAnchor.MiddleCenter);
            var plus = Button("＋", 200, y, 44, 34, new Color(0.30f, 0.35f, 0.45f));
            plus.onClick.AddListener(() => { set(Mathf.Clamp01(get() + step)); Redraw(); });
        }

        void ReturnToTitle()
        {
            Close();
            GameUI.LeaveOnlineToTitle?.Invoke();          // オンライン中なら切断してオンラインUIを閉じる
            var ui = Object.FindFirstObjectByType<GameUI>();
            if (ui != null) ui.ShowTitle();               // オフラインのタイトル画面へ
        }

        void QuitToDesktop()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ───────── UIヘルパー ─────────
        Image Panel(Color c, Vector2 pos, Vector2 size, bool stretch = false)
        {
            var go = new GameObject("Panel"); go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>(); img.color = c;
            var rt = img.rectTransform;
            if (stretch)
            {
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            }
            else
            {
                rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos; rt.sizeDelta = size;
            }
            return img;
        }

        Text Text(string s, float cx, float cy, float w, float h, int size, Color color, TextAnchor anchor)
        {
            var go = new GameObject("Text"); go.transform.SetParent(root, false);
            var t = go.AddComponent<Text>();
            t.text = s; t.font = font; t.fontSize = size; t.alignment = anchor; t.color = color; t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy); rt.sizeDelta = new Vector2(w, h);
            return t;
        }

        Button Button(string label, float cx, float cy, float w, float h, Color color)
        {
            var go = new GameObject("Button"); go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>(); img.color = color;
            var btn = go.AddComponent<Button>();
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy); rt.sizeDelta = new Vector2(w, h);
            // ラベル（ボタンの子・全面ストレッチ）
            var lgo = new GameObject("Label"); lgo.transform.SetParent(go.transform, false);
            var t = lgo.AddComponent<Text>();
            t.text = label; t.font = font; t.fontSize = 20; t.alignment = TextAnchor.MiddleCenter; t.color = Color.white; t.raycastTarget = false;
            var lrt = t.rectTransform;
            lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one; lrt.offsetMin = Vector2.zero; lrt.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
