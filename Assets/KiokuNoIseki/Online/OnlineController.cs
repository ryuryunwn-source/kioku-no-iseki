using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;

namespace KiokuNoIseki.Online
{
    // Phase1：オンライン接続層。Unity Relay の join code でホスト/参加し、接続成立までを担う。
    // 18章のホスト権威モデルの土台（盤面・手札の同期＝Phase2 は次段階）。
    // 別アセンブリ(KiokuNoIseki.Online)なので、ここがコンパイル失敗してもオフライン対戦は無傷。
    public class OnlineController : MonoBehaviour
    {
        static OnlineController s_instance;
        Font jpFont;
        Canvas canvas;
        Transform root;
        Text statusText;
        InputField codeInput;
        string status = "";
        ISession session;

        NetGame net;
        bool inPlay;
        // オンライン対戦中の操作状態（自分視点）
        int selectedIid;            // 選択中の自分の守護者
        int pendingSpellIid;        // 対象選択待ちの想起術（手札iid）
        bool pendingTargetsEnemy;
        enum PMode { Normal, AttackTarget, SpellTarget, Inscribe }
        PMode pmode = PMode.Normal;

        static Dictionary<string, CardData> s_db;
        static CardData Def(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (s_db == null)
            {
                s_db = new Dictionary<string, CardData>();
                foreach (var d in CardDatabase.BuildDeckDefinitions()) s_db[d.id] = d;
            }
            return s_db.TryGetValue(id, out var v) ? v : null;
        }

        static readonly Dictionary<Element, Color> ElemColor = new Dictionary<Element, Color>
        {
            { Element.Honoo, new Color(0.85f,0.30f,0.20f) },
            { Element.Mori,  new Color(0.30f,0.65f,0.30f) },
            { Element.Nagare,new Color(0.25f,0.55f,0.85f) },
            { Element.Hikari,new Color(0.90f,0.80f,0.35f) },
            { Element.Kage,  new Color(0.45f,0.35f,0.60f) },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (s_instance != null) return;
            var go = new GameObject("KiokuNoIseki_Online");
            Object.DontDestroyOnLoad(go);
            s_instance = go.AddComponent<OnlineController>();

            // タイトル画面の「オンラインで対戦」ボタンからここが呼ばれる
            GameUI.LaunchOnline = () => s_instance.ShowMenu();
        }

        void Awake()
        {
            jpFont = LoadJpFont();
        }

        Font LoadJpFont()
        {
            string[] names = { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS Gothic" };
            foreach (var n in names)
            {
                var f = Font.CreateDynamicFontFromOSFont(n, 18);
                if (f != null) return f;
            }
            return Font.CreateDynamicFontFromOSFont("Arial", 18);
        }

        // ───────── メニュー表示 ─────────
        public void ShowMenu()
        {
            if (canvas == null) BuildCanvas();
            canvas.gameObject.SetActive(true);
            status = "ホストになって相手にコードを伝えるか、コードを入力して参加してください。";
            Redraw();
        }

        void Hide()
        {
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        void BuildCanvas()
        {
            var cgo = new GameObject("OnlineCanvas");
            Object.DontDestroyOnLoad(cgo);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100; // 最前面
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.dynamicPixelsPerUnit = 4f;
            cgo.AddComponent<GraphicRaycaster>();
            root = cgo.transform;
        }

        void Redraw()
        {
            // 既存の子を消して作り直す
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);

            var dim = MakePanel(new Color(0.04f, 0.05f, 0.09f, 0.97f));
            Stretch(dim.rectTransform);

            MakeText(root, "オンライン対戦（β）", 0, 230, 760, 50, 34, TextAnchor.MiddleCenter, Color.white);

            // ステータス
            statusText = MakeText(root, status, 0, 150, 980, 70, 20, TextAnchor.MiddleCenter, new Color(0.9f, 0.9f, 0.7f));

            // ホストになる
            var host = MakeButton("ホストになる（コードを発行）", 0, 60, 420, 56, new Color(0.30f, 0.42f, 0.55f));
            host.onClick.AddListener(() => _ = HostAsync());

            // コード入力欄
            codeInput = MakeInput(0, -10, 300, 50, "参加コードを入力");

            // 参加する
            var join = MakeButton("このコードで参加", 0, -78, 300, 50, new Color(0.32f, 0.50f, 0.40f));
            join.onClick.AddListener(() => {
                string code = codeInput != null ? codeInput.text.Trim() : "";
                if (string.IsNullOrEmpty(code)) { status = "コードを入力してください。"; Redraw(); return; }
                _ = JoinAsync(code);
            });

            // 切断/戻る
            var back = MakeButton("切断してタイトルへ", 0, -160, 300, 48, new Color(0.5f, 0.32f, 0.32f));
            back.onClick.AddListener(() => {
                Disconnect();
                Hide();
            });
        }

        void SetStatus(string s) { status = s; if (statusText != null) statusText.text = s; }

        // ───────── 接続処理 ─────────
        async Task InitServicesAsync()
        {
            if (UnityServices.State != ServicesInitializationState.Initialized)
                await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
            {
                // 同一PCで複数インスタンスを動かしても別人として認証されるよう、起動ごとに一意プロファイルを使う
                try { AuthenticationService.Instance.SwitchProfile("p" + UnityEngine.Random.Range(1, 999999)); }
                catch { /* プロファイル切替不可でも続行 */ }
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }
        }

        NetworkManager EnsureNetworkManager()
        {
            if (NetworkManager.Singleton != null) return NetworkManager.Singleton;
            var go = new GameObject("NetworkManager");
            Object.DontDestroyOnLoad(go);
            var nm = go.AddComponent<NetworkManager>();
            var utp = go.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = utp };
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            return nm;
        }

        async Task HostAsync()
        {
            try
            {
                SetStatus("初期化・サインイン中...");
                await InitServicesAsync();
                EnsureNetworkManager();

                SetStatus("セッション作成中（Relay）...");
                var options = new SessionOptions { MaxPlayers = 2 }.WithRelayNetwork();
                session = await MultiplayerService.Instance.CreateSessionAsync(options);

                StartNet(asHost: true);
                SetStatus($"ホスト開始！  参加コード: {session.Code}\n相手にこのコードを伝えてください（接続待機中…）");
            }
            catch (System.Exception e)
            {
                SetStatus("ホスト開始に失敗: " + e.Message);
                Debug.LogException(e);
            }
        }

        async Task JoinAsync(string code)
        {
            try
            {
                SetStatus("初期化・サインイン中...");
                await InitServicesAsync();
                EnsureNetworkManager();

                SetStatus("セッションに参加中（Relay）...");
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code);

                StartNet(asHost: false);
                SetStatus("参加しました。対戦開始を待っています…");
            }
            catch (System.Exception e)
            {
                SetStatus("参加に失敗: " + e.Message);
                Debug.LogException(e);
            }
        }

        void OnClientConnected(ulong clientId)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            if (nm.IsHost && clientId != nm.LocalClientId)
                SetStatus("相手が接続しました！　接続成功（Phase1完了）。");
            else if (nm.IsClient && !nm.IsHost && clientId == nm.LocalClientId)
                SetStatus("ホストに接続成功！（Phase1完了）。");
        }

        void OnClientDisconnected(ulong clientId)
        {
            SetStatus("対戦相手との接続が切れました。");
        }

        void Disconnect()
        {
            inPlay = false;
            if (net != null) { net.Shutdown(); net = null; }
            session = null;
            var nm = NetworkManager.Singleton;
            if (nm != null && (nm.IsListening || nm.IsClient || nm.IsServer))
                nm.Shutdown();
        }

        // ───────── 対戦同期（NetGame）─────────
        void StartNet(bool asHost)
        {
            StartCoroutine(StartNetWhenReady(asHost));
        }

        System.Collections.IEnumerator StartNetWhenReady(bool asHost)
        {
            float t = 0f;
            while ((NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null) && t < 10f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            if (NetworkManager.Singleton == null || NetworkManager.Singleton.CustomMessagingManager == null)
            {
                SetStatus("ネットワーク開始に失敗（NetworkManager未起動）。");
                yield break;
            }
            net = new NetGame();
            net.OnViewUpdated += OnViewUpdated;
            if (asHost) net.StartHost(); else net.StartClient();
        }

        void OnViewUpdated()
        {
            inPlay = true;
            pmode = PMode.Normal; selectedIid = 0; pendingSpellIid = 0;
            RedrawPlay();
        }

        // ───────── 対戦画面（GameViewから描画） ─────────
        void RedrawPlay()
        {
            if (canvas == null) BuildCanvas();
            canvas.gameObject.SetActive(true);
            for (int i = root.childCount - 1; i >= 0; i--) Destroy(root.GetChild(i).gameObject);

            var bg = MakePanel(new Color(0.10f, 0.10f, 0.13f, 1f));
            Stretch(bg.rectTransform);

            var v = net != null ? net.LatestView : null;
            if (v == null)
            {
                MakeText(root, "対戦相手の接続を待っています…", 0, 30, 900, 60, 26, TextAnchor.MiddleCenter, Color.white);
                var b = MakeButton("切断してメニューへ", 0, -90, 320, 50, new Color(0.5f, 0.32f, 0.32f));
                b.onClick.AddListener(() => { Disconnect(); ShowMenu(); });
                return;
            }

            bool myTurn = v.myTurn && v.result == 0;

            // 相手情報（上）
            MakeText(root, $"{v.foe.name}   HP {v.foe.hp}   ゲージ {v.foe.gauge}/{v.foe.gaugeMax}   手札 {v.foe.hand.Length}   記憶 {v.foe.memoryCount}",
                -250, 320, 760, 30, 20, TextAnchor.MiddleLeft, Color.white);
            MakeText(root, $"遺構デッキ 残り {v.deckCount}", 430, 320, 320, 30, 18, TextAnchor.MiddleRight, new Color(0.8f, 0.8f, 0.6f));

            DrawRow(v.foe.board, 175, isEnemy: true, myTurn);
            DrawRow(v.me.board, -25, isEnemy: false, myTurn);

            // ログ（左）
            if (v.log != null && v.log.Length > 0)
                MakeText(root, string.Join("\n", v.log), -430, 40, 380, 200, 13, TextAnchor.LowerLeft, new Color(0.85f, 0.85f, 0.85f));

            // 自分情報（下）
            MakeText(root, $"{v.me.name}   HP {v.me.hp}   ゲージ {v.me.gauge}/{v.me.gaugeMax}   記憶 {v.me.memoryCount}   瓦礫 {v.me.rubble}",
                -250, -210, 800, 30, 20, TextAnchor.MiddleLeft, Color.white);

            DrawHand(v.me.hand, v.me.gauge, myTurn);
            DrawControls(v, myTurn);

            if (v.result != 0)
            {
                string msg = v.result == 1 ? "あなたの勝利！" : "あなたの敗北…";
                MakeText(root, msg, 0, 40, 700, 100, 46, TextAnchor.MiddleCenter, Color.yellow);
                var b = MakeButton("メニューへ戻る", 0, -40, 280, 50, new Color(0.45f, 0.4f, 0.5f));
                b.onClick.AddListener(() => { Disconnect(); ShowMenu(); });
            }
        }

        void DrawRow(CardView[] cards, float cy, bool isEnemy, bool myTurn)
        {
            float startX = -(cards.Length - 1) * 65f;
            for (int i = 0; i < cards.Length; i++)
            {
                var cv = cards[i];
                var btn = MakeCardButton(cv, startX + i * 130f, cy);
                if (btn == null) continue;

                if (pmode == PMode.SpellTarget && pendingSpellIid != 0 && pendingTargetsEnemy == isEnemy)
                {
                    int tid = cv.iid;
                    btn.onClick.AddListener(() => Submit(NetActionType.PlayCard, pendingSpellIid, tid));
                    Outline(btn.gameObject, Color.yellow);
                }
                else if (pmode == PMode.AttackTarget && isEnemy)
                {
                    int tid = cv.iid;
                    btn.onClick.AddListener(() => Submit(NetActionType.Attack, selectedIid, tid));
                    Outline(btn.gameObject, Color.red);
                }
                else if (!isEnemy && myTurn && pmode == PMode.Normal)
                {
                    int sid = cv.iid;
                    btn.onClick.AddListener(() => { selectedIid = sid; RedrawPlay(); });
                    if (selectedIid == cv.iid) Outline(btn.gameObject, Color.cyan);
                }
            }
        }

        void DrawHand(CardView[] hand, int gauge, bool myTurn)
        {
            float startX = -(hand.Length - 1) * 65f;
            for (int i = 0; i < hand.Length; i++)
            {
                var cv = hand[i];
                var btn = MakeCardButton(cv, startX + i * 130f, -300);
                if (btn == null) continue;
                if (!myTurn) continue;

                if (pmode == PMode.Inscribe)
                {
                    int iid = cv.iid;
                    btn.onClick.AddListener(() => Submit(NetActionType.Inscribe, iid));
                    Outline(btn.gameObject, Color.green);
                }
                else if (pmode == PMode.Normal)
                {
                    var def = Def(cv.cardId);
                    bool affordable = def != null && gauge >= def.cost;
                    if (affordable) { var c = cv; btn.onClick.AddListener(() => OnPlayHand(c)); }
                    else btn.interactable = false;
                }
            }
        }

        void OnPlayHand(CardView cv)
        {
            var def = Def(cv.cardId);
            if (def != null && def.kind == CardKind.Recollection && NeedsManualTarget(def.spellEffect))
            {
                pendingSpellIid = cv.iid;
                pendingTargetsEnemy = IsEnemyTargetSpell(def.spellEffect);
                pmode = PMode.SpellTarget;
                RedrawPlay();
            }
            else Submit(NetActionType.PlayCard, cv.iid);
        }

        void DrawControls(GameView v, bool myTurn)
        {
            if (!myTurn) { MakeText(root, "相手の番です…", 430, -300, 260, 36, 20, TextAnchor.MiddleRight, Color.gray); return; }

            var end = MakeButton("ターン終了", 470, -300, 150, 44, new Color(0.5f, 0.3f, 0.3f));
            end.onClick.AddListener(() => Submit(NetActionType.EndTurn, 0));

            if (pmode == PMode.Inscribe)
            {
                var cancel = MakeButton("刻む:手札選択中（やめる）", 300, -250, 230, 40, new Color(0.4f, 0.4f, 0.4f));
                cancel.onClick.AddListener(() => { pmode = PMode.Normal; RedrawPlay(); });
            }
            else if (pmode == PMode.SpellTarget)
            {
                string who = pendingTargetsEnemy ? "相手" : "自分";
                MakeText(root, $"▶ 想起術の対象（{who}の守護者）を選択", -250, -250, 640, 30, 20, TextAnchor.MiddleLeft, Color.yellow);
                var cancel = MakeButton("やめる", 470, -250, 120, 40, new Color(0.4f, 0.4f, 0.4f));
                cancel.onClick.AddListener(() => { pmode = PMode.Normal; pendingSpellIid = 0; RedrawPlay(); });
            }
            else if (pmode == PMode.AttackTarget)
            {
                MakeText(root, "▶ 攻撃する相手の守護者を選択", -250, -250, 640, 30, 20, TextAnchor.MiddleLeft, Color.yellow);
                var face = MakeButton("本体を直接攻撃", 300, -250, 200, 40, new Color(0.6f, 0.45f, 0.3f));
                face.onClick.AddListener(() => Submit(NetActionType.Attack, selectedIid, 0));
                var cancel = MakeButton("やめる", 470, -250, 120, 40, new Color(0.4f, 0.4f, 0.4f));
                cancel.onClick.AddListener(() => { pmode = PMode.Normal; RedrawPlay(); });
            }
            else
            {
                var ins = MakeButton("刻む", 470, -250, 150, 44, new Color(0.35f, 0.5f, 0.35f));
                ins.onClick.AddListener(() => { pmode = PMode.Inscribe; selectedIid = 0; RedrawPlay(); });

                if (selectedIid != 0)
                {
                    var tech = MakeButton("技を発動", 280, -250, 150, 44, new Color(0.4f, 0.4f, 0.6f));
                    tech.onClick.AddListener(() => Submit(NetActionType.Technique, selectedIid));
                    var atk = MakeButton("攻撃", 110, -250, 150, 44, new Color(0.6f, 0.4f, 0.4f));
                    atk.onClick.AddListener(() => {
                        if (v.foe.board.Length == 0) Submit(NetActionType.Attack, selectedIid, 0);
                        else { pmode = PMode.AttackTarget; RedrawPlay(); }
                    });
                }
            }
        }

        void Submit(NetActionType t, int a, int b = 0)
        {
            net?.SubmitAction(new NetAction { type = (int)t, a = a, b = b });
            pmode = PMode.Normal; selectedIid = 0; pendingSpellIid = 0;
        }

        static bool IsEnemyTargetSpell(EffectId e) =>
            e == EffectId.DamageEnemyGuardian || e == EffectId.DestroyEnemyGuardian;
        static bool IsAllyTargetSpell(EffectId e) =>
            e == EffectId.EngraveAlly || e == EffectId.RemoveSicknessAlly;
        static bool NeedsManualTarget(EffectId e) => IsEnemyTargetSpell(e) || IsAllyTargetSpell(e);

        // カード1枚のボタン（裏向きは絵だけ）
        Button MakeCardButton(CardView cv, float cx, float cy)
        {
            var def = cv.faceDown ? null : Def(cv.cardId);
            Color frame = cv.faceDown ? new Color(0.2f, 0.2f, 0.28f)
                        : def != null && def.kind == CardKind.Guardian ? ElemColor[def.element]
                        : def != null && def.kind == CardKind.Recollection ? new Color(0.5f, 0.5f, 0.55f)
                        : new Color(0.55f, 0.5f, 0.4f);

            var go = new GameObject("Card");
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.color = frame;
            var btn = go.AddComponent<Button>();
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy);
            rt.sizeDelta = new Vector2(120, 150);

            if (cv.faceDown || def == null)
            {
                MakeText(go.transform, "？", 0, 0, 120, 150, 40, TextAnchor.MiddleCenter, new Color(0.6f, 0.6f, 0.7f));
                return btn;
            }

            var name = MakeText(go.transform, def.trueName, 0, 20, 116, 40, 13, TextAnchor.MiddleCenter, Color.white);
            name.rectTransform.anchoredPosition = new Vector2(0, 20);
            string stat;
            if (def.kind == CardKind.Guardian)
            {
                string eng = cv.engraving > 0 ? $" 刻{cv.engraving}" : "";
                string sick = cv.sick ? " 酔" : "";
                stat = $"コスト{def.cost}\n{cv.atk}/{cv.def}{eng}{sick}";
            }
            else stat = $"コスト{def.cost}\n{(def.kind == CardKind.Cornerstone ? "礎石" : "術")}";
            var st = MakeText(go.transform, stat, 0, -45, 116, 50, 12, TextAnchor.LowerCenter, new Color(1, 1, 0.85f));
            return btn;
        }

        void Outline(GameObject go, Color c)
        {
            var o = go.AddComponent<UnityEngine.UI.Outline>();
            o.effectColor = c; o.effectDistance = new Vector2(3, 3);
        }

        // ───────── UIヘルパ ─────────
        Image MakePanel(Color c)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            return img;
        }

        Text MakeText(Transform parent, string text, float cx, float cy, float w, float h,
            int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.text = text; t.font = jpFont; t.fontSize = size; t.alignment = anchor; t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy);
            rt.sizeDelta = new Vector2(w, h);
            return t;
        }

        Button MakeButton(string label, float cx, float cy, float w, float h, Color color)
        {
            var go = new GameObject("Button");
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy);
            rt.sizeDelta = new Vector2(w, h);
            var t = MakeText(go.transform, label, 0, 0, w, h, 18, TextAnchor.MiddleCenter, Color.white);
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = Vector2.zero; t.rectTransform.offsetMax = Vector2.zero;
            return btn;
        }

        InputField MakeInput(float cx, float cy, float w, float h, string placeholder)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            img.color = new Color(0.9f, 0.9f, 0.9f);
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy);
            rt.sizeDelta = new Vector2(w, h);

            var field = go.AddComponent<InputField>();

            var ph = MakeText(go.transform, placeholder, 0, 0, w, h, 18, TextAnchor.MiddleLeft, new Color(0.4f, 0.4f, 0.4f));
            ph.rectTransform.anchorMin = Vector2.zero; ph.rectTransform.anchorMax = Vector2.one;
            ph.rectTransform.offsetMin = new Vector2(10, 0); ph.rectTransform.offsetMax = new Vector2(-10, 0);

            var txt = MakeText(go.transform, "", 0, 0, w, h, 18, TextAnchor.MiddleLeft, new Color(0.1f, 0.1f, 0.1f));
            txt.rectTransform.anchorMin = Vector2.zero; txt.rectTransform.anchorMax = Vector2.one;
            txt.rectTransform.offsetMin = new Vector2(10, 0); txt.rectTransform.offsetMax = new Vector2(-10, 0);
            txt.raycastTarget = true;

            field.textComponent = txt;
            field.placeholder = ph;
            field.characterLimit = 12;
            return field;
        }

        void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }
    }
}
