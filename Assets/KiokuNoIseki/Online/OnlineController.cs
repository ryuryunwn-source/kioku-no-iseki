using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
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
        int selectedIid;            // 選択中の自分のモンスター
        int selectedTargetIid;      // 音声「いけ」で攻撃する相手モンスター（クリックで選択）
        int pendingSpellIid;        // 対象選択待ちの魔法（手札iid）
        bool pendingTargetsEnemy;
        enum PMode { Normal, AttackTarget, SpellTarget, Inscribe }
        PMode pmode = PMode.Normal;
        GameObject onlineDetail;   // スキル詳細パネル
        bool showRules;            // ルール表示中

        static Dictionary<string, CardData> s_db;
        // マイモン（gen_）の定義。ネット経由で受け取ったGenCardInfoから復元して保持する。
        static readonly Dictionary<string, CardData> s_genDb = new Dictionary<string, CardData>();
        static CardData Def(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (s_db == null)
            {
                s_db = new Dictionary<string, CardData>();
                foreach (var d in CardDatabase.BuildDeckDefinitions()) s_db[d.id] = d;
            }
            if (s_db.TryGetValue(id, out var v)) return v;
            return s_genDb.TryGetValue(id, out var g) ? g : null;
        }

        // ネット経由で受け取ったマイモン定義を登録（写真は含まれない＝描画は自分の手元画像 or プレースホルダ）。
        static void RegisterGen(GenCardInfo[] infos)
        {
            if (infos == null) return;
            foreach (var info in infos)
                if (info != null && !string.IsNullOrEmpty(info.cardId))
                    s_genDb[info.cardId] = info.ToCardData();
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

        // ───────── 🎤 オンライン音声操作（Windows専用・対戦中のみ有効） ─────────
        public static OnlineController Instance => s_instance;
        public bool IsOnlineMatchActive => inPlay && net != null && net.LatestView != null;

        // 音声認識器は VoiceController（オフライン側）が1つだけ持ち、認識結果をイベント配信する。
        // オンラインはそれを購読して受け取る（同一キーワードで認識器を二重生成すると衝突するため）。
        void StartVoice()
        {
            VoiceController.OnVoiceCommand -= HandleVoice;  // 二重購読防止
            VoiceController.OnVoiceCommand += HandleVoice;
            Debug.Log("🎤 オンライン音声操作を有効化しました。");
        }

        void StopVoice()
        {
            VoiceController.OnVoiceCommand -= HandleVoice;
        }

        void OnDestroy() { StopVoice(); }

        // 認識した語を対戦操作へ（対戦中・自分の行動フェイズのみ実行）。
        void HandleVoice(string cmd)
        {
            if (!IsOnlineMatchActive) return;
            switch (cmd)
            {
                case "ターンエンド": VoiceEndTurn(); break;
                case "くらえ":       VoiceDirectAttack(); break;
                case "いけっ":
                case "いけ":         VoiceAttack(); break;
            }
        }

        bool VoiceReady(out GameView v)
        {
            v = net != null ? net.LatestView : null;
            return inPlay && v != null && v.myTurn && v.result == 0 && v.phase == (int)TurnPhase.Action;
        }

        bool HasFoeGuard(GameView v)
        {
            foreach (var cv in v.foe.board) { var d = Def(cv.cardId); if (d != null && d.guard && cv.def > 0) return true; }
            return false;
        }

        // 選択中が有効ならそれ。なければ召喚酔いでない自分のモンスターを先頭から採用。
        int ResolveVoiceAttacker(GameView v)
        {
            if (selectedIid != 0)
                foreach (var c in v.me.board) if (c.iid == selectedIid && !c.sick) return selectedIid;
            foreach (var c in v.me.board) if (!c.sick) return c.iid;
            return 0;
        }

        // 「ターンエンド」：手番を終える。
        public void VoiceEndTurn()
        {
            if (!VoiceReady(out _)) return;
            Submit(NetActionType.EndTurn, 0);
            RedrawPlay();
            Debug.Log("🎤 ターンエンド");
        }

        // 「いけっ」：相手モンスター（守護優先）へ攻撃。相手モンスターがいなければ本体へ。
        public void VoiceAttack()
        {
            if (!VoiceReady(out var v)) return;
            int attacker = ResolveVoiceAttacker(v);
            if (attacker == 0) { Debug.Log("🎤 攻撃できるモンスターがいません。"); return; }
            int target = 0; // 既定=本体
            // 1) プレイヤーがクリックで選んだ敵を最優先（まだ場にいる場合）
            if (selectedTargetIid != 0)
                foreach (var cv in v.foe.board) if (cv.iid == selectedTargetIid) { target = selectedTargetIid; break; }
            // 2) 未選択なら守護優先→先頭
            if (target == 0)
                foreach (var cv in v.foe.board) { var d = Def(cv.cardId); if (d != null && d.guard && cv.def > 0) { target = cv.iid; break; } }
            if (target == 0 && v.foe.board.Length > 0) target = v.foe.board[0].iid;
            Submit(NetActionType.Attack, attacker, target);
            RedrawPlay();
            Debug.Log($"🎤 攻撃 iid={attacker} → {target}");
        }

        // 「くらえ」：本体を直接攻撃（守護がいると不可）。
        public void VoiceDirectAttack()
        {
            if (!VoiceReady(out var v)) return;
            if (HasFoeGuard(v)) { Debug.Log("🎤 守護がいるため本体を攻撃できません。"); return; }
            int attacker = ResolveVoiceAttacker(v);
            if (attacker == 0) { Debug.Log("🎤 攻撃できるモンスターがいません。"); return; }
            Submit(NetActionType.Attack, attacker, 0);
            RedrawPlay();
            Debug.Log($"🎤 本体攻撃 iid={attacker}");
        }
        // ───────── 🎤 ここまで ─────────

        // ───────── メニュー表示 ─────────
        public void ShowMenu()
        {
            if (canvas == null) BuildCanvas();
            canvas.gameObject.SetActive(true);
            showRules = false;
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
            scaler.matchWidthOrHeight = 1f; // 高さ基準：解像度/縦横比が違っても縦のレイアウトを一定に保つ
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
            utp.UseWebSockets = true; // RelayをWSSで確立するため、Transport側もWebSocketを有効化（必須）
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = utp };
            nm.OnClientConnectedCallback += OnClientConnected;
            nm.OnClientDisconnectCallback += OnClientDisconnected;
            nm.OnTransportFailure += OnTransportFailure;
            return nm;
        }

        async Task HostAsync()
        {
            try
            {
                SetStatus("初期化・サインイン中...");
                await InitServicesAsync();
                CleanupNet();           // 前回の接続状態を破棄してまっさらに
                EnsureNetworkManager();

                SetStatus("セッション作成中（Relay）...");
                // RelayをWSS（Secure WebSocket/TCP443）で確立。既定のDTLS(UDP)を塞ぐ回線
                // （学校・会社・一部モバイル等）でも接続できるようにする。設定はセッションに
                // 保存され、参加側も同じプロトコルに追従する。
                var options = new SessionOptions { MaxPlayers = 2 }
                    .WithRelayNetwork()   // 先にRelayモジュールを登録してから
                    .WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.WSS }); // WSSプロトコルを確定させる
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
                CleanupNet();           // 前回の接続状態を破棄してまっさらに
                EnsureNetworkManager();

                SetStatus("セッションに参加中（Relay）...");
                // 参加側もWSSに合わせる（ホストと同じプロトコルでないとRelayデータとTransportが食い違う）
                var joinOptions = new JoinSessionOptions()
                    .WithNetworkOptions(new NetworkOptions { RelayProtocol = RelayProtocol.WSS });
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, joinOptions);

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

        // Relay/トランスポート障害（UDP遮断・回線不安定など）。無言で固まらないよう通知して後始末する。
        void OnTransportFailure()
        {
            SetStatus("接続に失敗しました（回線がRelayをブロックしている可能性）。\nもう一度ホスト作成／参加をお試しください。");
            inPlay = false;
            StopVoice();
        }

        void Disconnect()
        {
            session = null;
            CleanupNet();
        }

        // 接続状態を完全に破棄し、次の接続を必ずまっさらな NetworkManager で始められるようにする。
        // （前回の接続で残った Transport/driver 設定=UDP/WSS が次回と食い違うのを防ぐ）
        void CleanupNet()
        {
            inPlay = false;
            StopVoice();
            if (net != null) { net.Shutdown(); net = null; }
            var nm = NetworkManager.Singleton;
            if (nm != null)
            {
                if (nm.IsListening || nm.IsClient || nm.IsServer) nm.Shutdown();
                nm.OnClientConnectedCallback -= OnClientConnected;
                nm.OnClientDisconnectCallback -= OnClientDisconnected;
                nm.OnTransportFailure -= OnTransportFailure;
                Object.DestroyImmediate(nm.gameObject);
            }
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
            if (net != null && net.LatestView != null) RegisterGen(net.LatestView.genCards);
            inPlay = true;
            StartVoice(); // 対戦画面に入ったら音声認識を起動（Windowsのみ・二重起動ガード）
            pmode = PMode.Normal; selectedIid = 0; selectedTargetIid = 0; pendingSpellIid = 0;
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
            var battleBg = Resources.Load<Sprite>("Backgrounds/battle_bg");
            if (battleBg != null) { bg.sprite = battleBg; bg.color = new Color(0.42f, 0.42f, 0.48f); bg.preserveAspect = false; }

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
            MakeText(root, $"{v.foe.name}   HP {v.foe.hp}   ゲージ {v.foe.gauge}/{v.foe.gaugeMax}   殿堂 {v.foe.memoryCount}   殿堂勝利 {v.foe.pactCount}/{GameEngine.PactWinCount}",
                -250, 320, 760, 30, 20, TextAnchor.MiddleLeft, Color.white);

            // デッキ（裏面＋残数）
            MakeBack(480, 292, 30, 44);
            MakeText(root, $"デッキ {v.deckCount}", 480, 256, 130, 24, 14, TextAnchor.MiddleCenter, new Color(0.8f, 0.8f, 0.6f));
            // 次のドロー（デッキトップ公開＝共有デッキの駆け引きの中心）
            if (!string.IsNullOrEmpty(v.deckTopId))
            {
                var topDef = Def(v.deckTopId);
                if (topDef != null)
                {
                    string topEng = v.deckTopEng > 0 ? $"（刻{v.deckTopEng}）" : "";
                    var topT = MakeText(root, $"次▶「{topDef.trueName}」{topEng}",
                        455, 230, 220, 22, 13, TextAnchor.MiddleCenter, new Color(0.98f, 0.88f, 0.55f));
                    var to = topT.gameObject.AddComponent<UnityEngine.UI.Outline>();
                    to.effectColor = new Color(0, 0, 0, 0.9f); to.effectDistance = new Vector2(1.2f, -1.2f);
                }
            }
            // 殿堂リーチ警告（あと1体で勝利＝妨害判断を迫る）
            int pactReach = GameEngine.PactWinCount - 1;
            if (v.result == 0 && (v.me.pactCount >= pactReach || v.foe.pactCount >= pactReach))
            {
                string who = v.foe.pactCount >= v.me.pactCount ? v.foe.name : v.me.name;
                int max = Mathf.Max(v.me.pactCount, v.foe.pactCount);
                var warnT = MakeText(root, $"⚠ 殿堂：{who} はあと {GameEngine.PactWinCount - max} 体で勝利",
                    0, 228, 620, 26, 18, TextAnchor.MiddleCenter, new Color(1f, 0.45f, 0.35f));
                var wo = warnT.gameObject.AddComponent<UnityEngine.UI.Outline>();
                wo.effectColor = new Color(0, 0, 0, 0.9f); wo.effectDistance = new Vector2(1.2f, -1.2f);
            }

            // 相手の手札（伏せ札の列）
            int fhand = v.foe.hand != null ? v.foe.hand.Length : 0;
            float hs = -(fhand - 1) * 22f;
            for (int i = 0; i < fhand; i++) MakeBack(hs + i * 22f, 285, 34, 36);

            // ルールボタン（右上）
            var rulesBtn = MakeButton("ルール", 545, 332, 110, 32, new Color(0.30f, 0.34f, 0.45f));
            rulesBtn.onClick.AddListener(() => { showRules = true; RedrawPlay(); });

            DrawRow(v.foe.board, 175, isEnemy: true, myTurn);
            DrawRow(v.me.board, -25, isEnemy: false, myTurn);

            // ログ（右）
            if (v.log != null && v.log.Length > 0)
                MakeText(root, string.Join("\n", v.log), 430, 60, 360, 200, 13, TextAnchor.LowerRight, new Color(0.85f, 0.85f, 0.85f));

            // 選択中のモンスターはスキル詳細を出しておく
            if (selectedIid != 0)
            {
                var sel = FindCard(v, selectedIid);
                if (sel != null) ShowDetail(sel);
            }

            // 自分情報（下）
            MakeText(root, $"{v.me.name}   HP {v.me.hp}   ゲージ {v.me.gauge}/{v.me.gaugeMax}   殿堂 {v.me.memoryCount}   殿堂勝利 {v.me.pactCount}/{GameEngine.PactWinCount}",
                -250, -140, 800, 30, 20, TextAnchor.MiddleLeft, Color.white);

            DrawHand(v.me.hand, v.me.gauge, myTurn);
            DrawControls(v, myTurn);

            if (v.result != 0)
            {
                string msg = v.result == 1 ? "あなたの勝利！" : "あなたの敗北…";
                MakeText(root, msg, 0, 40, 700, 100, 46, TextAnchor.MiddleCenter, Color.yellow);
                var b = MakeButton("メニューへ戻る", 0, -40, 280, 50, new Color(0.45f, 0.4f, 0.5f));
                b.onClick.AddListener(() => { Disconnect(); ShowMenu(); });
            }

            if (showRules) DrawRulesOverlay();
        }

        // ルールブック（スクロール表示）。文面はオフラインと共有(GameUI.RulesText)。
        void DrawRulesOverlay()
        {
            var dim = MakePanel(new Color(0.03f, 0.04f, 0.07f, 0.98f));
            Stretch(dim.rectTransform);

            MakeText(dim.transform, "ルールブック", 0, 332, 400, 36, 26, TextAnchor.MiddleCenter, Color.white);

            // スクロールビュー
            var svGo = new GameObject("RulesScroll");
            svGo.transform.SetParent(dim.transform, false);
            var svImg = svGo.AddComponent<Image>(); svImg.color = new Color(0, 0, 0, 0.30f);
            var sv = svGo.AddComponent<ScrollRect>();
            svGo.AddComponent<RectMask2D>();
            var svRt = svImg.rectTransform;
            svRt.anchorMin = svRt.anchorMax = svRt.pivot = new Vector2(0.5f, 0.5f);
            svRt.sizeDelta = new Vector2(940, 560); svRt.anchoredPosition = new Vector2(0, -8);

            var content = new GameObject("Content");
            content.transform.SetParent(svGo.transform, false);
            var t = content.AddComponent<Text>();
            t.font = jpFont; t.fontSize = 16; t.color = new Color(0.93f, 0.93f, 0.93f);
            t.text = GameUI.RulesText; t.alignment = TextAnchor.UpperLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            t.lineSpacing = 1.2f;
            var cRt = t.rectTransform;
            cRt.anchorMin = new Vector2(0, 1); cRt.anchorMax = new Vector2(1, 1); cRt.pivot = new Vector2(0.5f, 1);
            cRt.offsetMin = new Vector2(16, 0); cRt.offsetMax = new Vector2(-16, 0);
            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            sv.content = cRt; sv.horizontal = false; sv.vertical = true;
            sv.movementType = ScrollRect.MovementType.Clamped; sv.scrollSensitivity = 30f;

            var close = MakeButton("閉じる", 0, -332, 220, 48, new Color(0.5f, 0.32f, 0.32f));
            close.onClick.AddListener(() => { showRules = false; RedrawPlay(); });
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
                else if (isEnemy && myTurn && pmode == PMode.Normal)
                {
                    // 相手モンスター：クリックで攻撃対象に選択（音声「いけ」で確定攻撃）
                    int tid = cv.iid;
                    btn.onClick.AddListener(() => { selectedTargetIid = (selectedTargetIid == tid) ? 0 : tid; RedrawPlay(); });
                    if (selectedTargetIid == cv.iid) Outline(btn.gameObject, Color.red);
                }
            }
        }

        void DrawHand(CardView[] hand, int gauge, bool myTurn)
        {
            float startX = -(hand.Length - 1) * 65f;
            for (int i = 0; i < hand.Length; i++)
            {
                var cv = hand[i];
                var btn = MakeCardButton(cv, startX + i * 130f, -258);
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

        // ボタン等を画面の右下隅にアンカー固定する（解像度/縦横比が違っても手札=中央 と重ならない）。
        void PinBottomRight(RectTransform rt, float right, float bottom)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.anchoredPosition = new Vector2(-right, bottom);
        }

        void DrawControls(GameView v, bool myTurn)
        {
            const float RX = 24f; // 右端からの余白
            if (!myTurn)
            {
                var wait = MakeText(root, "相手の番です…", 0, 0, 240, 36, 20, TextAnchor.MiddleRight, Color.gray);
                PinBottomRight(wait.rectTransform, RX, 30);
                return;
            }

            // ターン終了（常時・最下段）
            var end = MakeButton("ターン終了", 0, 0, 150, 44, new Color(0.5f, 0.3f, 0.3f));
            PinBottomRight((RectTransform)end.transform, RX, 22);
            end.onClick.AddListener(() => Submit(NetActionType.EndTurn, 0));

            if (pmode == PMode.Inscribe)
            {
                var cancel = MakeButton("生贄:手札選択中（やめる）", 0, 0, 230, 40, new Color(0.4f, 0.4f, 0.4f));
                PinBottomRight((RectTransform)cancel.transform, RX, 74);
                cancel.onClick.AddListener(() => { pmode = PMode.Normal; RedrawPlay(); });
            }
            else if (pmode == PMode.SpellTarget)
            {
                string who = pendingTargetsEnemy ? "相手" : "自分";
                MakeText(root, $"▶ 魔法の対象（{who}のモンスター）を選択", -250, -150, 640, 30, 20, TextAnchor.MiddleLeft, Color.yellow);
                var cancel = MakeButton("やめる", 0, 0, 120, 40, new Color(0.4f, 0.4f, 0.4f));
                PinBottomRight((RectTransform)cancel.transform, RX, 74);
                cancel.onClick.AddListener(() => { pmode = PMode.Normal; pendingSpellIid = 0; RedrawPlay(); });
            }
            else if (pmode == PMode.AttackTarget)
            {
                MakeText(root, "▶ 攻撃する相手のモンスターを選択", -250, -150, 640, 30, 20, TextAnchor.MiddleLeft, Color.yellow);
                bool foeGuard = false;
                foreach (var cv in v.foe.board) { var d = Def(cv.cardId); if (d != null && d.guard && cv.def > 0) { foeGuard = true; break; } }
                if (!foeGuard)
                {
                    var face = MakeButton("本体を直接攻撃", 0, 0, 200, 40, new Color(0.6f, 0.45f, 0.3f));
                    PinBottomRight((RectTransform)face.transform, RX, 126);
                    face.onClick.AddListener(() => Submit(NetActionType.Attack, selectedIid, 0));
                }
                else
                {
                    var g2 = MakeText(root, "守護がいるため本体を攻撃不可", 0, 0, 240, 30, 15, TextAnchor.MiddleRight, new Color(0.85f, 0.72f, 0.5f));
                    PinBottomRight(g2.rectTransform, RX, 130);
                }
                var cancel = MakeButton("やめる", 0, 0, 120, 40, new Color(0.4f, 0.4f, 0.4f));
                PinBottomRight((RectTransform)cancel.transform, RX, 74);
                cancel.onClick.AddListener(() => { pmode = PMode.Normal; RedrawPlay(); });
            }
            else
            {
                var ins = MakeButton("生贄", 0, 0, 150, 44, new Color(0.35f, 0.5f, 0.35f));
                PinBottomRight((RectTransform)ins.transform, RX, 74);
                ins.onClick.AddListener(() => { pmode = PMode.Inscribe; selectedIid = 0; RedrawPlay(); });

                if (selectedIid != 0)
                {
                    var tech = MakeButton("技を発動", 0, 0, 150, 44, new Color(0.4f, 0.4f, 0.6f));
                    PinBottomRight((RectTransform)tech.transform, RX, 126);
                    tech.onClick.AddListener(() => Submit(NetActionType.Technique, selectedIid));
                    var atk = MakeButton("攻撃", 0, 0, 150, 44, new Color(0.6f, 0.4f, 0.4f));
                    PinBottomRight((RectTransform)atk.transform, RX, 178);
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
            pmode = PMode.Normal; selectedIid = 0; selectedTargetIid = 0; pendingSpellIid = 0;
        }

        static bool IsEnemyTargetSpell(EffectId e) =>
            e == EffectId.DamageEnemyGuardian || e == EffectId.DestroyEnemyGuardian;
        static bool IsAllyTargetSpell(EffectId e) =>
            e == EffectId.EngraveAlly || e == EffectId.RemoveSicknessAlly;
        static bool NeedsManualTarget(EffectId e) => IsEnemyTargetSpell(e) || IsAllyTargetSpell(e);

        // カード1枚のボタン（裏向きは絵だけ）
        static readonly Dictionary<string, Sprite> s_artCache = new Dictionary<string, Sprite>();
        static Sprite s_frame; static bool s_frameLoaded;
        static Sprite GetFrame()
        {
            if (!s_frameLoaded) { s_frame = Resources.Load<Sprite>("Frames/frame_base"); s_frameLoaded = true; }
            return s_frame;
        }
        static KiokuNoIseki.CardView s_cardPrefab; static bool s_cardPrefabLoaded;
        static KiokuNoIseki.CardView GetCardPrefab()
        {
            if (!s_cardPrefabLoaded) { s_cardPrefab = Resources.Load<KiokuNoIseki.CardView>("Card"); s_cardPrefabLoaded = true; }
            return s_cardPrefab;
        }
        static Sprite GetArt(CardData def)
        {
            if (def == null) return null;
            if (s_artCache.TryGetValue(def.id, out var c)) return c;
            if (KiokuNoIseki.GeneratedArt.IsGenerated(def.id))
            {
                // 自分が同じ写真を持っていれば実画像。無ければ（相手のマイモン）系統色のプレースホルダ。
                var g = KiokuNoIseki.GeneratedArt.Get(def.id);
                if (g == null) g = MakeGenPlaceholder(def);
                s_artCache[def.id] = g;
                return g;
            }
            Sprite sp = null;
            if (def.kind == CardKind.Guardian && def.id.Length > 1 && int.TryParse(def.id.Substring(1), out int gi))
                sp = Resources.Load<Sprite>($"CardArt/guardian_{gi:000}");
            if (sp == null) sp = Resources.Load<Sprite>($"CardArt/{def.id}");
            s_artCache[def.id] = sp;
            return sp;
        }

        // 相手のマイモン（写真が手元に無い）用のプレースホルダ。系統色のグラデ＋中央菱形。
        static Sprite MakeGenPlaceholder(CardData def)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            Color tint = ElemColor.TryGetValue(def.element, out var col) ? col : new Color(0.4f, 0.4f, 0.45f);
            Color top = tint * 0.75f; top.a = 1f;
            Color bottom = tint * 0.22f; bottom.a = 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - size * 0.5f) / (size * 0.5f);
                    float dy = (y - size * 0.5f) / (size * 0.5f);
                    Color c = Color.Lerp(bottom, top, (float)y / size);
                    if (Mathf.Abs(dx) + Mathf.Abs(dy) < 0.55f) c = Color.Lerp(c, Color.white, 0.25f); // 中央菱形
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        }

        static string ElemName(Element e) => e switch
        {
            Element.Honoo => "焔", Element.Mori => "森", Element.Nagare => "流",
            Element.Hikari => "光", Element.Kage => "影", _ => ""
        };

        Button MakeCardButton(CardView cv, float cx, float cy)
        {
            var def = cv.faceDown ? null : Def(cv.cardId);

            // 通常カードはプレハブから生成（レイアウトはエディタで調整可能）
            var prefab = GetCardPrefab();
            if (prefab != null && !cv.faceDown && def != null)
                return MakeCardButtonFromPrefab(prefab, cv, def, cx, cy);

            var go = new GameObject("Card");
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy);
            rt.sizeDelta = new Vector2(120, 168);

            // 局所ヘルパ：範囲テキスト／一点テキスト（黒縁付き）
            void PlaceRange(string s, Vector2 aMin, Vector2 aMax, int fs, Color col, bool shade)
            {
                var t = MakeText(go.transform, s, 0, 0, 10, 10, fs, TextAnchor.MiddleCenter, col);
                var r = t.rectTransform; r.anchorMin = aMin; r.anchorMax = aMax;
                r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
                if (shade) { var o = t.gameObject.AddComponent<UnityEngine.UI.Outline>(); o.effectColor = new Color(0,0,0,0.9f); o.effectDistance = new Vector2(1.2f,-1.2f); }
            }
            void PlaceAt(string s, Vector2 a, int fs, Color col)
            {
                var t = MakeText(go.transform, s, 0, 0, 22, 22, fs, TextAnchor.MiddleCenter, col);
                var r = t.rectTransform; r.anchorMin = r.anchorMax = a; r.pivot = new Vector2(0.5f,0.5f);
                r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(22,22);
                var o = t.gameObject.AddComponent<UnityEngine.UI.Outline>(); o.effectColor = new Color(0,0,0,0.9f); o.effectDistance = new Vector2(1.2f,-1.2f);
            }

            var frame = GetFrame();

            if (cv.faceDown || def == null)
            {
                if (frame != null) { img.sprite = frame; img.color = new Color(0.55f,0.55f,0.6f); }
                else img.color = new Color(0.2f, 0.2f, 0.28f);
                PlaceRange("？", new Vector2(0.16f,0.53f), new Vector2(0.83f,0.89f), 36, new Color(0.85f,0.85f,0.9f), false);
                return btn;
            }

            Color edge = def.kind == CardKind.Guardian ? ElemColor[def.element]
                       : def.kind == CardKind.Recollection ? new Color(0.5f,0.5f,0.55f)
                       : new Color(0.6f,0.52f,0.38f);
            if (frame != null) { img.sprite = frame; img.color = Color.white; }
            else img.color = edge;

            // イラスト（窓）
            var artGo = new GameObject("Art"); artGo.transform.SetParent(go.transform, false);
            var ai = artGo.AddComponent<Image>(); ai.raycastTarget = false;
            var sp = GetArt(def);
            if (sp != null) { ai.sprite = sp; ai.color = Color.white; ai.preserveAspect = false; }
            else ai.color = new Color(edge.r*0.4f, edge.g*0.4f, edge.b*0.4f);
            var ar = ai.rectTransform; ar.anchorMin = new Vector2(0.16f,0.53f); ar.anchorMax = new Vector2(0.83f,0.89f);
            ar.offsetMin = Vector2.zero; ar.offsetMax = Vector2.zero;

            // 名前バナー
            PlaceRange(def.trueName, new Vector2(0.31f,0.46f), new Vector2(0.71f,0.51f), 10, new Color(0.97f,0.92f,0.80f), true);
            // 守護バッジ（暗い下地付きで必ず読める）
            if (def.guard)
            {
                var bg = new GameObject("GuardBadge"); bg.transform.SetParent(go.transform, false);
                var bgImg = bg.AddComponent<Image>(); bgImg.color = new Color(0.05f,0.09f,0.16f,0.88f); bgImg.raycastTarget = false;
                var br = bgImg.rectTransform;
                br.anchorMin = new Vector2(0.17f,0.795f); br.anchorMax = new Vector2(0.47f,0.875f);
                br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;
                var bo = bg.AddComponent<UnityEngine.UI.Outline>(); bo.effectColor = new Color(0.45f,0.75f,1f,0.95f); bo.effectDistance = new Vector2(1f,-1f);
                PlaceRange("守護", new Vector2(0.17f,0.795f), new Vector2(0.47f,0.875f), 11, new Color(0.78f,0.93f,1f), true);
            }
            // 技/種別（下部パネル）
            string sub = def.kind == CardKind.Guardian ? def.techniqueName : def.kind == CardKind.Recollection ? "魔法" : "魔法石";
            PlaceRange(sub, new Vector2(0.12f,0.05f), new Vector2(0.88f,0.17f), 10, new Color(0.95f,0.89f,0.75f), true);
            // コスト
            PlaceAt(def.cost.ToString(), new Vector2(0.148f,0.487f), 14, new Color(1f,0.86f,0.42f));

            if (def.kind == CardKind.Guardian)
            {
                PlaceAt(ElemName(def.element), new Vector2(0.865f,0.477f), 11, new Color(1f,0.97f,0.88f));
                PlaceAt(cv.atk.ToString(),  new Vector2(0.833f,0.266f), 14, new Color(1f,0.55f,0.42f)); // 攻（右）
                PlaceAt(cv.def.ToString(),  new Vector2(0.283f,0.266f), 14, new Color(0.58f,0.82f,1f)); // 防（左）
                string flags = (cv.engraving > 0 ? $"刻{cv.engraving} " : "") + (cv.sick ? "酔" : "");
                if (flags.Length > 0)
                    PlaceRange(flags, new Vector2(0.28f,0.36f), new Vector2(0.72f,0.44f), 9, new Color(0.96f,0.86f,0.52f), true);
            }

            // ホバーでスキル詳細を表示
            var trig = go.AddComponent<EventTrigger>();
            var cvc = cv;
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener((_) => ShowDetail(cvc));
            trig.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener((_) =>
            {
                var lv = net != null ? net.LatestView : null;
                if (selectedIid != 0 && lv != null) { var s = FindCard(lv, selectedIid); if (s != null) { ShowDetail(s); return; } }
                HideDetail();
            });
            trig.triggers.Add(exit);

            return btn;
        }

        // プレハブ版：通常カード（表向き）をInstantiate+Bindで描画。
        Button MakeCardButtonFromPrefab(KiokuNoIseki.CardView prefab, CardView cv, CardData def, float cx, float cy)
        {
            var view = Object.Instantiate(prefab, root);
            var rt = view.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(cx, cy); // 大きさはプレハブ準拠

            bool isGuardian = def.kind == CardKind.Guardian;
            string sub = isGuardian ? def.techniqueName : def.kind == CardKind.Recollection ? "魔法" : "魔法石";
            string flags = isGuardian ? ((cv.engraving > 0 ? $"刻{cv.engraving} " : "") + (cv.sick ? "酔" : "")) : "";
            view.Bind(jpFont, GetFrame(), GetArt(def), def.trueName, def.cost.ToString(), isGuardian,
                ElemName(def.element), cv.atk.ToString(), cv.def.ToString(), sub, flags, def.guard);

            // ホバーでスキル詳細を表示
            var trig = view.gameObject.AddComponent<EventTrigger>();
            var cvc = cv;
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener((_) => ShowDetail(cvc));
            trig.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener((_) =>
            {
                var lv = net != null ? net.LatestView : null;
                if (selectedIid != 0 && lv != null) { var s = FindCard(lv, selectedIid); if (s != null) { ShowDetail(s); return; } }
                HideDetail();
            });
            trig.triggers.Add(exit);

            return view.button;
        }

        void MakeBack(float cx, float cy, float w, float h)
        {
            // オフラインと同じ裏面デザイン（石板＋銅トリム＋同心円＋菱形＋「記」）を共有ビルダーで生成
            CardBackArt.Build(root, new Vector2(cx, cy), new Vector2(w, h), jpFont);
        }

        CardView FindCard(GameView v, int iid)
        {
            if (v == null || iid == 0) return null;
            if (v.me != null && v.me.board != null) foreach (var c in v.me.board) if (c.iid == iid) return c;
            if (v.me != null && v.me.hand != null) foreach (var c in v.me.hand) if (c.iid == iid) return c;
            if (v.foe != null && v.foe.board != null) foreach (var c in v.foe.board) if (c.iid == iid) return c;
            return null;
        }

        string BuildDesc(CardView cv)
        {
            var d = Def(cv.cardId);
            if (d == null) return "";
            switch (d.kind)
            {
                case CardKind.Guardian:
                    string eng = cv.engraving > 0 ? $"  刻印{cv.engraving}" : "";
                    return $"【モンスター】{d.trueName}　系統:{ElemName(d.element)}\n" +
                           $"コスト{d.cost}　攻撃{cv.atk} / 防御{cv.def}{eng}\n" +
                           $"━ 技「{d.techniqueName}」（詠唱コスト{d.incantationCost}）━\n{d.effectText}";
                case CardKind.Recollection:
                    return $"【魔法】{d.trueName}　コスト{d.cost}\n{d.effectText}";
                default:
                    return $"【魔法石】{d.trueName}　コスト{d.cost}（設置・永続）\n{d.effectText}";
            }
        }

        void ShowDetail(CardView cv)
        {
            if (cv == null || cv.faceDown) return;
            var d = Def(cv.cardId); if (d == null) return;
            if (onlineDetail != null) Destroy(onlineDetail);
            var p = MakePanel(new Color(0.05f, 0.05f, 0.09f, 0.96f));
            onlineDetail = p.gameObject;
            var rt = p.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0, 175); rt.sizeDelta = new Vector2(680, 150);
            Color edge = d.kind == CardKind.Guardian ? ElemColor[d.element] : new Color(0.6f, 0.6f, 0.65f);
            Outline(p.gameObject, edge);
            var t = MakeText(p.transform, BuildDesc(cv), 0, 0, 660, 140, 17, TextAnchor.UpperLeft, new Color(0.95f, 0.95f, 0.95f));
            t.lineSpacing = 1.25f;
            t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
            t.rectTransform.offsetMin = new Vector2(12, 12); t.rectTransform.offsetMax = new Vector2(-12, -12);
            p.transform.SetAsLastSibling();
        }

        void HideDetail()
        {
            if (onlineDetail != null) Destroy(onlineDetail);
            onlineDetail = null;
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
