using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace KiokuNoIseki
{
    // 14章のUI方針に従い「枠（固定テンプレート）＋イラスト窓（v1はプレースホルダー単色）」で
    // カードを描画する。シーン編集不要：Play押下で自動生成される。
    public class GameUI : MonoBehaviour
    {
     public GameEngine engine;
        Font jpFont;

        // 系統ごとの枠の縁色（14-3：5バリエーション）
        static readonly Dictionary<Element, Color> ElementColor = new Dictionary<Element, Color>
        {
            { Element.Honoo, new Color(0.85f,0.30f,0.20f) },
            { Element.Mori,  new Color(0.30f,0.65f,0.30f) },
            { Element.Nagare,new Color(0.25f,0.55f,0.85f) },
            { Element.Hikari,new Color(0.90f,0.80f,0.35f) },
            { Element.Kage,  new Color(0.45f,0.35f,0.60f) },
        };

        // UI状態
        enum Mode { Normal, Inscribe, SelectAttackTarget, SelectSpellTarget }
        Mode mode = Mode.Normal;
        CardInstance selectedAttacker;
        CardInstance selectedTarget;        // 音声「いけ」で攻撃する相手モンスター（クリックで選択）
        bool resultSfxDone;                 // 勝敗の効果音を1回だけ鳴らすためのフラグ
        CardInstance pendingSpell;          // 対象選択待ちの魔法
        bool pendingSpellTargetsEnemy;      // true=相手のモンスターを選ぶ / false=自分のモンスター
        bool showRules;
        bool showWriteshiForge;    // マイモン工房オーバーレイ表示中
        bool writeshiBusy;         // 命名リクエスト処理中（多重起動防止）
        GameObject cardDetail; // ホバー/選択中カードのスキル詳細パネル

        // オンライン拡張（別アセンブリ）への接続点。拡張が読み込まれると設定される。
        // null の場合はオンライン機能が未ロード（コンパイル失敗時など）。
        public static System.Action LaunchOnline;
        public static System.Action LeaveOnlineToTitle; // 設定画面「タイトルに戻る」用：オンライン中なら切断してUIを閉じる（OnlineControllerが設定）

        // 設定画面などからタイトル画面へ戻す。
        public void ShowTitle()
        {
            inTitle = true; awaitingPass = false; showRules = false;
            selectedAttacker = null; selectedTarget = null; mode = Mode.Normal;
            HideCardDetail();
            Redraw();
        }

        // 画面・モード状態
        bool inTitle = true;     // タイトル画面表示中
        bool isLocalPvP;         // ローカル対人戦か（false=AI戦）
        bool lastWasAI = true;   // リマッチ用に直前のモードを記憶
        bool awaitingPass;       // 交代プレイの目隠し画面表示中

        // 表示視点プレイヤー：AI戦は常に人間(0)、対人戦は現在の手番
        int Vp => isLocalPvP ? engine.currentPlayer : 0;
        // 現在、視点プレイヤーが人間として操作できる手番か
        bool MyActiveTurn => engine != null && !inTitle && !awaitingPass
            && engine.result == GameResult.Ongoing
            && engine.currentPlayer == Vp && !engine.players[Vp].isAI;

        // ゲーム内ルールブック（v1の要点）。ルールブック .md の抜粋・要約。
        // オンライン側(別アセンブリ)からも参照するため public。
        public const string RulesText =
@"『マイモン』 ルール（v1）

■ 目的（勝利条件）
・相手のHPを0にする（通常勝利）
・刻印3のモンスターを2体、殿堂に集めると勝利（殿堂勝利）
　モンスターを盤面で守り抜くと刻印が貯まり、刻印3のモンスターが
　さらに破壊される（4つ目の刻印がつく）と殿堂へ。第2の勝ち筋。
　※殿堂の進捗は両者に公開。あと1体でリーチ警告が出る。
・デッキが尽きたとき、殿堂の枚数が多い方が勝利（枯渇勝利）

■ 基本
・デッキは中央の『デッキ』48枚を2人で共有して掘り合う。
・HPは20、ゲージは初期2/2。手札は最大7枚。
・場：モンスター5スロット／魔法石3スロット。

■ ターンの流れ（5フェイズ）
1. 減衰：ゲージが自動で-1（前ターンに生贄を行った場合はスキップ）。
2. ドロー：デッキから1枚引く。デッキの一番上は常に両者に公開される
   （次に掘れるカードを見て、取るか・相手に渡すかの駆け引きが生まれる）。
3. 生贄（任意）：手札1枚を捧げてゲージ上限+1＆全回復。1ターン1回。
   生贄を行うと次の減衰はスキップされる。
4. 行動：カードのプレイ・攻撃・技の発動を自由な順で。
5. 終了：手札に長く残ったカードに劣化が溜まる。

■ カードの種類
・モンスター：コスト/攻撃/防御を持つカード。出した直後は召喚酔いで攻撃不可
  （技はタップで使用可）。各モンスターは固有の『技』を持つ。
・魔法：使い切りのカード。使用後はデッキの一番下へ。
・魔法石：場に残る永続効果カード。

■ 守護
・一部のモンスターは「守護」を持つ。相手の場に守護がいる間は、その本体(HP)を直接攻撃できない。
・本体を狙うには先に守護を全部倒す（守護以外のモンスターは普通に殴れる）。
・守護は除去や全体ダメージで処理できる。出した直後（召喚酔い中）でも守護は機能する。

■ 技の発動（タップ）
・自分の行動フェイズに、自分のモンスターをタップ→『技』ボタンで発動。
・詠唱コストをゲージから支払う。1体につき1ターン1回まで。

■ 劣化
・3ターン以上手札に残るとカウンターが増え、3で消滅する
  （消滅したカードはゲームから完全に除外）。

■ 転生（共有デッキの肝）
・破壊されたモンスターは墓地ではなくデッキにシャッフルで戻る。
・戻る際に『刻印』が1つ増え、刻印1つにつき攻撃力/防御力+1。
・真名・技はカード固有なので、相手が掘り当てれば相手がその技を使える。
・刻印3のモンスターがさらに破壊されると、破壊した側の殿堂へ送られる。

■ 操作方法
・手札カードをクリック：プレイ（対象は自動選択）。
・自分のモンスターをクリック：選択→『技』『攻撃』。
・攻撃：相手モンスターをクリックで対象指定、または本体を直接攻撃。
・『生贄』→捧げる手札をクリック。
・『ターン終了』でAIの番へ。";

        Transform root;
        Text logText;
        readonly List<string> logLines = new List<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (FindAnyObjectByType<GameUI>() != null) return;
            var go = new GameObject("KiokuNoIseki_Game");
            DontDestroyOnLoad(go);
            go.AddComponent<GameUI>();
        }

        void Start()

        {
            jpFont = LoadJpFont();
            EnsureEventSystem();
            BuildCanvas();
            Redraw(); // タイトル画面から開始
        }

        Font LoadJpFont()
        {
            string[] candidates = { "Yu Gothic UI", "Yu Gothic", "Meiryo UI", "Meiryo", "MS Gothic", "MS UI Gothic" };
            foreach (var name in candidates)
            {
                var f = Font.CreateDynamicFontFromOSFont(name, 16);
                if (f != null) return f;
            }
            return Font.CreateDynamicFontFromOSFont(Font.GetOSInstalledFontNames().Length > 0
                ? Font.GetOSInstalledFontNames()[0] : "Arial", 16);
        }

        void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            es.AddComponent<StandaloneInputModule>();
#endif
        }

        Canvas canvas;
        void BuildCanvas()
        {
            var cgo = new GameObject("Canvas");
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            scaler.dynamicPixelsPerUnit = 4f; // 動的フォントを高解像度でラスタライズ＝文字くっきり
            cgo.AddComponent<GraphicRaycaster>();
            root = cgo.transform;

            // 背景（battle_bg があれば画像、無ければ無地）。UI視認性のため暗めに敷く。
            var bg = MakePanel(root, new Color(0.10f, 0.10f, 0.13f), "BG");
            Stretch(bg.rectTransform);
            var battleBg = Resources.Load<Sprite>("Backgrounds/battle_bg");
            if (battleBg != null) { bg.sprite = battleBg; bg.color = new Color(0.42f, 0.42f, 0.48f); bg.preserveAspect = false; }
        }

        // ───────── 再描画（イミディエイト方式：毎回作り直す） ─────────
        void Redraw()
        {
            // 既存の動的UIを消す（BG以外）
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var ch = root.GetChild(i);
                if (ch.name == "BG" || ch.name == "FxLayer") continue; // 背景と演出層は作り直さない
                Destroy(ch.gameObject);
            }
            cardDetail = null; // 上のDestroyで詳細パネルも消えるため参照をクリア

            // タイトル画面
            if (inTitle || engine == null)
            {
                DrawTitle();
                if (showRules) DrawRulesOverlay();
                if (showWriteshiForge) DrawWriteshiForge();
                return;
            }

            // 交代プレイの目隠し画面（手札を隠して引き継ぐ）
            if (awaitingPass)
            {
                DrawPassScreen();
                if (showRules) DrawRulesOverlay();
                return;
            }

            int vp = Vp;
            var me = engine.players[vp];
            var foe = engine.players[1 - vp];
            bool ongoing = engine.result == GameResult.Ongoing;
            // 勝敗の効果音（1回だけ）。あなた=プレイヤー0視点で勝ち/負けを鳴らす。
            if (ongoing) resultSfxDone = false;
            else if (!resultSfxDone) { resultSfxDone = true; AudioManager.Sfx(engine.result == GameResult.Player0Win ? "sfx_win" : "sfx_lose"); }

            // 殿堂進捗（完全刻印の体数）。両者に常時公開＝妨害判断の材料にする。
            int myPact = PactCount(me), foePact = PactCount(foe);

            // 上部：相手情報＋盤面
            MakeLabel(root, $"{foe.name}   HP {foe.hp}   ゲージ {foe.recallGauge}/{foe.recallGaugeMax}   殿堂 {foe.memoryZone.Count}   殿堂勝利 {foePact}/{GameEngine.PactWinCount}",
                new Vector2(20, -16), new Vector2(700, 28), 20, TextAnchor.MiddleLeft, Color.white);
            // デッキ（裏面スタック＋残数）
            MakeCardBack(root, new Vector2(-150, -44), new Vector2(44, 63), fromTop:true, anchorRight:true);
            MakeLabel(root, $"デッキ\n残り {engine.deck.Count} 枚",
                new Vector2(-20, -52), new Vector2(120, 44), 16, TextAnchor.MiddleRight, new Color(0.8f,0.8f,0.6f), anchorRight:true);
            // 次のドロー（デッキトップ公開＝共有デッキの駆け引きの中心）
            if (engine.deck.Count > 0)
            {
                var top = engine.deck.cards[0];
                string topEng = top.engravingCount > 0 ? $"（刻{top.engravingCount}）" : "";
                var topT = MakeLabel(root, $"次のドロー ▶「{top.definition.trueName}」{topEng}",
                    new Vector2(-20, -104), new Vector2(300, 24), 15, TextAnchor.MiddleRight,
                    new Color(0.98f, 0.88f, 0.55f), anchorRight: true);
                OutlineText(topT);
            }
            // 殿堂リーチ警告（あと1体で勝利＝妨害判断を迫る）
            int pactReach = GameEngine.PactWinCount - 1;
            if (ongoing && (myPact >= pactReach || foePact >= pactReach))
            {
                string who = foePact >= myPact ? foe.name : me.name;
                var warnT = MakeLabel(root, $"⚠ 古き盟^約：{who} はあと {GameEngine.PactWinCount - Mathf.Max(myPact, foePact)} 体で勝利",
                    new Vector2(290, -78), new Vector2(700, 30), 20, TextAnchor.MiddleCenter,
                    new Color(1f, 0.45f, 0.35f));
                OutlineText(warnT);
            }

            // 相手の手札（伏せカードの扇）
            int foeHand = foe.hand.Count;
            if (foeHand > 0)
            {
                float bw = 28f, step = 20f;
                float total = (foeHand - 1) * step + bw;
                float startX = 640f - total / 2f;
                for (int i = 0; i < foeHand; i++)
                    MakeCardBack(root, new Vector2(startX + i * step, -5), new Vector2(bw, 40), fromTop:true);
            }

            // ルール表示ボタン（常時表示・右上）
            var rulesBtn = MakeButton(root, "ルール", new Vector2(-20, -14), new Vector2(110, 32),
                new Color(0.30f,0.34f,0.45f), fromTop:true, anchorRight:true);
            rulesBtn.onClick.AddListener(() => { showRules = true; Redraw(); });

            // ログ（カードより先に描く＝後ろに回す）
            DrawLogPanel();

            DrawBoardRow(foe.board, y: -46, owner: foe, isOpponent: true);
            DrawBoardRow(me.board, y: -238, owner: me, isOpponent: false);

            // 下部：手番プレイヤー情報（自分の盤面カードと重ならないよう手札寄りに配置）
            MakeLabel(root, $"{me.name}   HP {me.hp}   ゲージ {me.recallGauge}/{me.recallGaugeMax}   殿堂 {me.memoryZone.Count}   殿堂勝利 {myPact}/{GameEngine.PactWinCount}",
                new Vector2(20, 250), new Vector2(800, 28), 20, TextAnchor.MiddleLeft, Color.white, fromBottom:true);

            // 手札
            DrawHand(me, interactable: MyActiveTurn);

            // 操作ボタン群
            DrawControls(MyActiveTurn);

            // 勝敗
            if (!ongoing) DrawGameOver();

            // 選択中のモンスターがあればスキル詳細を出しておく
            if (selectedAttacker != null && me.board.Contains(selectedAttacker))
                ShowCardDetail(selectedAttacker);

            // ルール画面（最後に描画＝最前面に重なる）
            if (showRules) DrawRulesOverlay();
        }

        static string ElementName(Element e)
        {
            switch (e)
            {
                case Element.Honoo: return "焔";
                case Element.Mori: return "森";
                case Element.Nagare: return "流";
                case Element.Hikari: return "光";
                default: return "影";
            }
        }

        string BuildCardDesc(CardInstance c)
        {
            var d = c.definition;
            switch (d.kind)
            {
                case CardKind.Guardian:
                    string eng = c.engravingCount > 0 ? $"  刻印{c.engravingCount}" : "";
                    return $"【モンスター】{d.trueName}　系統:{ElementName(d.element)}\n" +
                           $"コスト{d.cost}　攻撃{c.CurrentAttack} / 防御{c.RemainingDefense}{eng}\n" +
                           $"━ 技「{d.techniqueName}」（詠唱コスト{d.incantationCost}）━\n" +
                           $"{d.effectText}";
                case CardKind.Recollection:
                    return $"【魔法】{d.trueName}　コスト{d.cost}\n{d.effectText}";
                default:
                    return $"【魔法石】{d.trueName}　コスト{d.cost}（設置・永続）\n{d.effectText}";
            }
        }

        // カードのスキル詳細パネル（ホバー/選択時）。手札の上あたりに表示する。
        void ShowCardDetail(CardInstance c)
        {
            if (cardDetail != null) Destroy(cardDetail);
            var panel = MakePanel(root, new Color(0.05f, 0.05f, 0.09f, 0.96f), "DetailPanel");
            cardDetail = panel.gameObject;
            var rt = panel.rectTransform;
            rt.anchorMin = new Vector2(0.5f, 0); rt.anchorMax = new Vector2(0.5f, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(0, 200);
            rt.sizeDelta = new Vector2(680, 150);

            Color edge = c.definition.kind == CardKind.Guardian ? ElementColor[c.definition.element]
                       : new Color(0.6f, 0.6f, 0.65f);
            Outline(panel.gameObject, edge);

            var t = MakeChildText(panel.transform, BuildCardDesc(c), 17, TextAnchor.UpperLeft, new Color(0.95f, 0.95f, 0.95f));
            t.lineSpacing = 1.25f;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            Stretch(t.rectTransform, 14);

            panel.transform.SetAsLastSibling(); // 最前面へ
        }

        void HideCardDetail()
        {
            if (cardDetail != null) Destroy(cardDetail);
            cardDetail = null;
        }

        // スクロール可能なルール表示オーバーレイ
        void DrawRulesOverlay()
        {
            // 全画面の暗幕
            var dim = MakePanel(root, new Color(0f, 0f, 0f, 0.85f), "RulesOverlay");
            Stretch(dim.rectTransform);

            // タイトル
            MakeLabel(dim.transform, "ルールブック", new Vector2(0, -16), new Vector2(400, 36), 26,
                TextAnchor.UpperCenter, Color.white);

            // 閉じるボタン
            var close = MakeButton(dim.transform, "✕ 閉じる", new Vector2(-24, -14), new Vector2(120, 40),
                new Color(0.5f,0.3f,0.3f), fromTop:true, anchorRight:true);
            close.onClick.AddListener(() => { showRules = false; Redraw(); });

            // スクロールビュー領域
            var scrollGo = new GameObject("Scroll");
            scrollGo.transform.SetParent(dim.transform, false);
            var scrollImg = scrollGo.AddComponent<Image>();
            scrollImg.color = new Color(0.12f, 0.12f, 0.16f, 0.95f);
            var scroll = scrollGo.AddComponent<ScrollRect>();
            var srt = scrollImg.rectTransform;
            srt.anchorMin = new Vector2(0, 0); srt.anchorMax = new Vector2(1, 1);
            srt.pivot = new Vector2(0.5f, 0.5f);
            // 上60px(タイトル/閉じる)・下20px・左右40pxの余白を空けて全画面に広げる
            srt.offsetMin = new Vector2(40, 20);
            srt.offsetMax = new Vector2(-40, -60);

            // viewport（クリップ用）
            var vpGo = new GameObject("Viewport");
            vpGo.transform.SetParent(scrollGo.transform, false);
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(1, 1, 1, 0.01f);
            vpGo.AddComponent<RectMask2D>();
            Stretch(vpImg.rectTransform, 6);

            // content（テキスト）。高さは行数から明示計算（ContentSizeFitterのリビルド遅延を回避）
            int fontSize = 18;
            int lineCount = RulesText.Split('\n').Length;
            float contentHeight = (lineCount + 2) * (fontSize + 8); // 行高の概算＋余白

            var contentGo = new GameObject("Content");
            contentGo.transform.SetParent(vpGo.transform, false);
            var contentRt = contentGo.AddComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1); contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.offsetMin = new Vector2(0, 0); contentRt.offsetMax = new Vector2(0, 0);
            contentRt.sizeDelta = new Vector2(0, contentHeight);
            contentRt.anchoredPosition = Vector2.zero;

            // テキストは content の子として左右パディングを取って配置
            var txtGo = new GameObject("Text");
            txtGo.transform.SetParent(contentGo.transform, false);
            var txt = txtGo.AddComponent<Text>();
            txt.text = RulesText;
            txt.font = jpFont; txt.fontSize = fontSize; txt.color = new Color(0.92f, 0.92f, 0.92f);
            txt.alignment = TextAnchor.UpperLeft;
            txt.lineSpacing = 1.2f;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            var trt = txt.rectTransform;
            trt.anchorMin = new Vector2(0, 0); trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = new Vector2(24, 8); trt.offsetMax = new Vector2(-24, -8);

            scroll.content = contentRt;
            scroll.viewport = vpImg.rectTransform;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 24;
            scroll.movementType = ScrollRect.MovementType.Clamped;
        }

        void DrawBoardRow(List<CardInstance> board, float y, RecallerState owner, bool isOpponent)
        {
            float x = 18;
            foreach (var c in board)
            {
                var card = c;
                var btn = MakeCard(root, card, new Vector2(x, y), fromTop:true);
                bool yourTurn = MyActiveTurn;

                if (mode == Mode.SelectSpellTarget && pendingSpell != null && pendingSpellTargetsEnemy == isOpponent)
                {
                    // 魔法の対象として選べる
                    btn.onClick.AddListener(() => {
                        engine.PlayCard(pendingSpell, card);
                        pendingSpell = null; mode = Mode.Normal; AfterHumanAction();
                    });
                    Outline(btn.gameObject, Color.yellow);
                }
                else if (mode == Mode.SelectAttackTarget && isOpponent)
                {
                    // 攻撃対象として選べる
                    btn.onClick.AddListener(() => {
                        engine.Attack(selectedAttacker, card);
                        mode = Mode.Normal; selectedAttacker = null; AfterHumanAction();
                    });
                    Outline(btn.gameObject, Color.red);
                }
                else if (mode == Mode.Normal && !isOpponent && yourTurn)
                {
                    // 自分のモンスター：選択して技/攻撃
                    btn.onClick.AddListener(() => OnSelectOwnGuardian(card));
                    if (selectedAttacker == card) Outline(btn.gameObject, Color.cyan);
                }
                else if (mode == Mode.Normal && isOpponent && yourTurn)
                {
                    // 相手のモンスター：クリックで攻撃対象として選択（音声「いけ」で確定攻撃）
                    btn.onClick.AddListener(() => { selectedTarget = (selectedTarget == card) ? null : card; Redraw(); });
                    if (selectedTarget == card) Outline(btn.gameObject, Color.red);
                }
                x += 140;
            }
        }

        void OnSelectOwnGuardian(CardInstance card)
        {
            selectedAttacker = card;
            mode = Mode.Normal;
            Redraw();
        }

        // 魔法で手動対象が必要なら対象選択モードへ。それ以外は即プレイ。
        void OnPlayHandCard(CardInstance card)
        {
            var me = engine.players[Vp];
            var foe = engine.players[1 - Vp];
            if (card.definition.kind == CardKind.Recollection &&
                NeedsManualTarget(card.definition.spellEffect, me, foe))
            {
                pendingSpell = card;
                pendingSpellTargetsEnemy = IsEnemyTargetSpell(card.definition.spellEffect);
                selectedAttacker = null;
                mode = Mode.SelectSpellTarget;
                Redraw();
            }
            else { engine.PlayCard(card); AfterHumanAction(); }
        }

        static bool IsEnemyTargetSpell(EffectId e) =>
            e == EffectId.DamageEnemyGuardian || e == EffectId.DestroyEnemyGuardian;
        static bool IsAllyTargetSpell(EffectId e) =>
            e == EffectId.EngraveAlly || e == EffectId.RemoveSicknessAlly;
        bool NeedsManualTarget(EffectId e, RecallerState me, RecallerState foe)
        {
            if (IsEnemyTargetSpell(e)) return foe.board.Count > 0;
            if (IsAllyTargetSpell(e)) return me.board.Count > 0;
            return false;
        }

        void DrawHand(RecallerState you, bool interactable)
        {
            float x = 18;
            foreach (var c in you.hand)
            {
                var card = c;
                var btn = MakeCard(root, card, new Vector2(x, 14), fromBottom:true);
                if (interactable)
                {
                    if (mode == Mode.Inscribe)
                    {
                        btn.onClick.AddListener(() => { AudioManager.Sfx("sfx_inscribe"); engine.Inscribe(card); mode = Mode.Normal; AfterHumanAction(); });
                        Outline(btn.gameObject, Color.green);
                    }
                    else
                    {
                        bool affordable = you.recallGauge >= card.definition.cost;
                        if (affordable)
                            btn.onClick.AddListener(() => OnPlayHandCard(card));
                        else
                            btn.interactable = false;
                    }
                }
                x += 140;
            }
        }

        void DrawControls(bool yourTurn)
        {
            if (engine.result != GameResult.Ongoing) return;
            if (!yourTurn)
            {
                MakeLabel(root, "AIの番…", new Vector2(-30, 30), new Vector2(200, 36), 20,
                    TextAnchor.MiddleRight, Color.gray, fromBottom:true, anchorRight:true);
                return;
            }

            var me = engine.players[Vp];
            var foe = engine.players[1 - Vp];
            float bx = -30;

            // ターン終了
            var endBtn = MakeButton(root, "ターン終了", new Vector2(bx, 30), new Vector2(140, 44),
                new Color(0.5f,0.3f,0.3f), fromBottom:true, anchorRight:true);
            endBtn.onClick.AddListener(() => { mode = Mode.Normal; selectedAttacker = null; selectedTarget = null; engine.EndTurn(); AdvanceTurn(); });
            bx -= 150;

            // 生贄
            if (!me.inscribedThisTurn && me.hand.Count > 0)
            {
                var insBtn = MakeButton(root, mode == Mode.Inscribe ? "生贄:手札選択" : "生贄",
                    new Vector2(bx, 30), new Vector2(140, 44), new Color(0.35f,0.5f,0.35f), fromBottom:true, anchorRight:true);
                insBtn.onClick.AddListener(() => { mode = mode == Mode.Inscribe ? Mode.Normal : Mode.Inscribe; selectedAttacker = null; Redraw(); });
                bx -= 150;
            }

            // 選択中のモンスターの行動
           // 選択中のモンスターの行動
            if (selectedAttacker != null)
            {
                var g = selectedAttacker;
                // 技発動
                if (!g.techniqueUsedThisTurn)
                {
                    var tBtn = MakeButton(root, $"技:{g.definition.techniqueName}", new Vector2(bx, 84),
                        new Vector2(200, 40), new Color(0.4f,0.4f,0.6f), fromBottom:true, anchorRight:true);
                    tBtn.onClick.AddListener(() => {
                        if (TechniqueActivator.TryActivate(engine, g, out var reason)) { AudioManager.Sfx("sfx_technique"); AfterHumanAction(); }
                        else { AddLog($"技不可: {reason}"); Redraw(); }
                    });
                }
                // 攻撃
                if (!g.summoningSick && !g.attackedThisTurn && g.CurrentAttack > 0)
                {
                    var aBtn = MakeButton(root, "攻撃（対象選択）", new Vector2(bx, 128),
                        new Vector2(200, 40), new Color(0.6f,0.4f,0.4f), fromBottom:true, anchorRight:true);
                    aBtn.onClick.AddListener(() => {
                        if (foe.board.Count == 0)
                        { engine.Attack(g, null); selectedAttacker = null; AfterHumanAction(); }
                        else { mode = Mode.SelectAttackTarget; Redraw(); }
                    });
                    bool foeHasGuard = foe.board.Any(c => c.definition.guard && c.RemainingDefense > 0);
                    if (!foeHasGuard)
                    {
                        var fBtn = MakeButton(root, "本体を直接攻撃", new Vector2(bx, 172),
                            new Vector2(200, 40), new Color(0.6f,0.45f,0.3f), fromBottom:true, anchorRight:true);
                        fBtn.onClick.AddListener(() => { engine.Attack(g, null); selectedAttacker = null; AfterHumanAction(); });
                    }
                    else
                    {
                        MakeLabel(root, "守護がいるため本体を攻撃できない", new Vector2(-20, 172),
                            new Vector2(260, 40), 13, TextAnchor.MiddleRight, new Color(0.85f,0.72f,0.5f),
                            fromBottom:true, anchorRight:true);
                    }
                }
            }

            if (mode == Mode.SelectAttackTarget)
            {
                var c = MakeButton(root, "攻撃キャンセル", new Vector2(bx, 84), new Vector2(160, 40),
                    new Color(0.4f,0.4f,0.4f), fromBottom:true, anchorRight:true);
                c.onClick.AddListener(() => { mode = Mode.Normal; Redraw(); });
            }

            if (mode == Mode.SelectSpellTarget && pendingSpell != null)
            {
                string who = pendingSpellTargetsEnemy ? "相手" : "自分";
                MakeLabel(root, $"▶「{pendingSpell.definition.trueName}」の対象（{who}のモンスター）を選択",
                    new Vector2(20, 188), new Vector2(640, 30), 20, TextAnchor.MiddleLeft, Color.yellow, fromBottom:true);
                var c = MakeButton(root, "対象選択をやめる", new Vector2(bx, 84), new Vector2(180, 40),
                    new Color(0.4f,0.4f,0.4f), fromBottom:true, anchorRight:true);
                c.onClick.AddListener(() => { pendingSpell = null; mode = Mode.Normal; Redraw(); });
            }
        }

        void AfterHumanAction()
        {
            if (mode != Mode.Inscribe) mode = Mode.Normal;
            pendingSpell = null;
            Redraw();
        }

        // ───────── 攻撃演出 ─────────
        // Redraw() は root 直下を毎回作り直す（BG/FxLayer は除外）。演出は永続の FxLayer に出す。
        Transform fxLayer;
        Transform GetFxLayer()
        {
            if (fxLayer == null && root != null)
            {
                var go = new GameObject("FxLayer", typeof(RectTransform));
                go.transform.SetParent(root, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
                fxLayer = go.transform;
            }
            return fxLayer;
        }

        void OnAttackFx(CardInstance attacker, CardInstance target)
        {
            AudioManager.Sfx("sfx_attack"); // 効果音（Resources/Audio/sfx_attack が無ければ無音）
            if (root == null) return;
            // 本体への直接攻撃=赤、モンスター同士の戦闘=黄白
            Color c = target == null ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.92f, 0.55f);
            StartCoroutine(FlashFx(c));
        }

        System.Collections.IEnumerator FlashFx(Color color)
        {
            var layer = GetFxLayer();
            if (layer == null) yield break;

            var go = new GameObject("AttackFx", typeof(RectTransform));
            go.transform.SetParent(layer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.raycastTarget = false;

            float t = 0f; const float dur = 0.22f;
            while (t < dur)
            {
                t += Time.deltaTime;
                layer.SetAsLastSibling(); // 作り直された盤面より前面を維持
                float a = Mathf.Lerp(0.40f, 0f, t / dur);
                img.color = new Color(color.r, color.g, color.b, a);
                yield return null;
            }
            if (go != null) Destroy(go);
        }

        // AIの番なら自動進行
        void AfterAiMaybe()
        {
            Redraw();
            // AIの手番をまとめて処理
            int safety = 0;
            while (engine.result == GameResult.Ongoing && engine.currentPlayer == 1 && safety++ < 5)
            {
                AIController.TakeTurn(engine);
            }
            Redraw();
        }

        // ───────── モード開始・進行・画面 ─────────
        void StartGame(bool ai)
        {
            lastWasAI = ai; isLocalPvP = !ai;
            inTitle = false; awaitingPass = false; showRules = false;
            selectedAttacker = null; selectedTarget = null; mode = Mode.Normal;
            logLines.Clear();
            
            // ★バグ修正：古いイベントを解除（もしあれば）してから新しいエンジンを作る
            if (engine != null)
            {
                engine.OnLog -= AddLog;
                engine.OnStateChanged -= Redraw;
                engine.OnVoiceAttackRequest -= OnVoiceAttackCommandReceived;
                engine.OnVoiceDirectAttackRequest -= OnVoiceDirectAttackCommandReceived;
            }

            engine = new GameEngine();
            engine.OnLog += AddLog;
            engine.OnStateChanged += Redraw;
            engine.OnVoiceAttackRequest += OnVoiceAttackCommandReceived;             // 「いけっ」：敵モンスターへ攻撃実行
            engine.OnVoiceDirectAttackRequest += OnVoiceDirectAttackCommandReceived; // 「くらえ」：本体へ直接攻撃実行
            engine.OnAttack += OnAttackFx; // 攻撃演出

            // マイモンがあればデッキに合流させる（同数の固定モンスターと置き換わる）。
            engine.injectedWriteshi = WriteshiCollection.Count > 0 ? WriteshiCollection.Snapshot() : null;
            engine.NewGame(player1IsAI: ai);
            AudioManager.Battle(); // 戦闘BGMに切り替え
            AddLog(ai ? "=== 対戦開始：あなた vs AI ==="
                      : "=== ローカル対人戦 開始：プレイヤー1 vs プレイヤー2 ===");
            Redraw();
        }

        // 「ターン終了」後の進行：AIなら自動、対人戦なら目隠し画面を挟む
        void AdvanceTurn()
        {
            selectedAttacker = null; selectedTarget = null; mode = Mode.Normal; HideCardDetail();
            if (engine.result != GameResult.Ongoing) { Redraw(); return; }
            if (engine.players[engine.currentPlayer].isAI) { AfterAiMaybe(); return; }
            if (isLocalPvP) { awaitingPass = true; Redraw(); return; }
            Redraw();
        }

        static TitleView s_titlePrefab; static bool s_titlePrefabLoaded;
        TitleView GetTitlePrefab()
        {
            if (!s_titlePrefabLoaded) { s_titlePrefab = Resources.Load<TitleView>("Title"); s_titlePrefabLoaded = true; }
            return s_titlePrefab;
        }

        void DrawTitle()
        {
            AudioManager.Title(); // タイトルBGM（同じ曲なら鳴らし直さない）
            var titlePrefab = GetTitlePrefab();
            if (titlePrefab != null) { DrawTitleFromPrefab(titlePrefab); return; }

            var logoSprite = Resources.Load<Sprite>("Title/title_logo");
            if (logoSprite != null)
            {
                // ロゴ画像があれば文字の代わりに画像を表示
                var logoGo = new GameObject("TitleLogo", typeof(RectTransform), typeof(Image));
                logoGo.transform.SetParent(root, false);
                var img = logoGo.GetComponent<Image>();
                img.sprite = logoSprite; img.preserveAspect = true; img.color = Color.white;
                var lrt = img.rectTransform;
                lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = new Vector2(0, 175); lrt.sizeDelta = new Vector2(560, 220);
            }
            else
            {
                var titleT = MakeChildText(root, "マイモン", 60, TextAnchor.MiddleCenter, Color.white);
                var trt = titleT.rectTransform;
                trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
                trt.anchoredPosition = new Vector2(0, 190); trt.sizeDelta = new Vector2(800, 90);

                var subT = MakeChildText(root, "― MyMon ―", 22, TextAnchor.MiddleCenter, new Color(0.7f, 0.7f, 0.75f));
                var srt = subT.rectTransform;
                srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 0.5f);
                srt.anchoredPosition = new Vector2(0, 138); srt.sizeDelta = new Vector2(800, 40);
            }

            var b1 = MakeCenterButton("AIと対戦", new Vector2(0, 40), new Vector2(340, 60), new Color(0.30f, 0.40f, 0.55f));
            b1.onClick.AddListener(() => StartGame(true));
            var b2 = MakeCenterButton("2人で対戦（ローカル）", new Vector2(0, -32), new Vector2(340, 56), new Color(0.32f, 0.50f, 0.40f));
            b2.onClick.AddListener(() => StartGame(false));
            var b3 = MakeCenterButton("オンラインで対戦（β）", new Vector2(0, -98), new Vector2(340, 56), new Color(0.35f, 0.38f, 0.55f));
            b3.onClick.AddListener(() => {
                if (LaunchOnline != null) LaunchOnline.Invoke();
                else AddLog("オンライン機能が読み込まれていません（パッケージ/コンパイルを確認）。");
            });
            var b4 = MakeCenterButton("ルールを見る", new Vector2(0, -164), new Vector2(340, 56), new Color(0.40f, 0.40f, 0.48f));
            b4.onClick.AddListener(() => { showRules = true; Redraw(); });

            var b5 = MakeCenterButton(WriteshiButtonLabel(), new Vector2(0, -230), new Vector2(340, 56), new Color(0.48f, 0.38f, 0.30f));
            b5.onClick.AddListener(() => { showWriteshiForge = true; Redraw(); });
        }

        void DrawTitleFromPrefab(TitleView prefab)
        {
            var tv = Object.Instantiate(prefab, root);
            tv.ApplyFont(jpFont);
            tv.ApplyBackground(Resources.Load<Sprite>("Backgrounds/title_bg"));
            tv.ApplyLogo(Resources.Load<Sprite>("Title/title_logo")); // ロゴ画像があれば文字の代わりに表示
            if (tv.aiButton != null) tv.aiButton.onClick.AddListener(() => StartGame(true));
            if (tv.localButton != null) tv.localButton.onClick.AddListener(() => StartGame(false));
            if (tv.onlineButton != null) tv.onlineButton.onClick.AddListener(() =>
            {
                if (LaunchOnline != null) LaunchOnline.Invoke();
                else AddLog("オンライン機能が読み込まれていません（パッケージ/コンパイルを確認）。");
            });
            if (tv.rulesButton != null) tv.rulesButton.onClick.AddListener(() => { showRules = true; Redraw(); });

            // プレハブ側にはマイモン工房ボタンが無いため、コードで下部に重ねて追加する。
            var forgeBtn = MakeCenterButton(WriteshiButtonLabel(), new Vector2(0, -250), new Vector2(340, 52), new Color(0.48f, 0.38f, 0.30f));
            forgeBtn.onClick.AddListener(() => { showWriteshiForge = true; Redraw(); });
        }

        string WriteshiButtonLabel() =>
            WriteshiCollection.Count > 0 ? $"マイモン工房（{WriteshiCollection.Count}体）" : "マイモン工房（写真からカード生成）";

        // マイモン工房オーバーレイ：写真からマイモンを生成し、対戦のデッキに混ぜる。
        void DrawWriteshiForge()
        {
            var dim = MakePanel(root, new Color(0.03f, 0.03f, 0.05f, 0.92f), "WriteshiForge");
            Stretch(dim.rectTransform);

            var title = MakeChildText(dim.transform, "マイモン工房", 34, TextAnchor.MiddleCenter, new Color(0.95f, 0.86f, 0.72f));
            var trt = title.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0, 235); trt.sizeDelta = new Vector2(700, 60);

            string apiState = WriteshiNamingService.HasKeys() ? "AI命名: 有効" : "AI命名: 無効（オフライン名で生成）";
            string info =
                $"写真からモンスターカード（マイモン）を作り、対戦のデッキに混ぜます。\n" +
                $"作成したマイモン: {WriteshiCollection.Count} 体 / {apiState}\n" +
                (writeshiBusy ? "…命名中…" : "同じ写真からは常に同じカードが生成されます。");
            var infoT = MakeChildText(dim.transform, info, 18, TextAnchor.MiddleCenter, new Color(0.85f, 0.85f, 0.88f));
            var irt = infoT.rectTransform;
            irt.anchorMin = irt.anchorMax = irt.pivot = new Vector2(0.5f, 0.5f);
            irt.anchoredPosition = new Vector2(0, 140); irt.sizeDelta = new Vector2(760, 110);

            // アルバムから選ぶ（実機のみ／プラグイン必要）
            var pick = MakeCenterButton("アルバムから選ぶ", new Vector2(0, 60), new Vector2(360, 56), new Color(0.34f, 0.44f, 0.55f));
            pick.onClick.AddListener(() => PickFromGallery());

            // カメラで撮る（実機のみ／プラグイン必要）
            var cam = MakeCenterButton("カメラで撮る", new Vector2(0, -6), new Vector2(360, 56), new Color(0.34f, 0.50f, 0.44f));
            cam.onClick.AddListener(() => TakePhoto());

            // テスト用：プラグイン無しでもマイモン生成を試せる（ランダム画像から）
            var test = MakeCenterButton("テストマイモンを追加（画像なし）", new Vector2(0, -72), new Vector2(360, 52), new Color(0.42f, 0.40f, 0.52f));
            test.onClick.AddListener(() => AddTestWriteshi());

            var clear = MakeCenterButton("全部消す", new Vector2(-95, -140), new Vector2(170, 50), new Color(0.5f, 0.34f, 0.34f));
            clear.onClick.AddListener(() => { WriteshiCollection.Clear(); Redraw(); });

            var close = MakeCenterButton("閉じる", new Vector2(95, -140), new Vector2(170, 50), new Color(0.4f, 0.4f, 0.48f));
            close.onClick.AddListener(() => { showWriteshiForge = false; Redraw(); });
        }

        // 写真テクスチャ→（AI命名 or オフライン名）→マイモンをコレクションに追加。
        void ProcessPhoto(Texture2D tex)
        {
            if (tex == null) { AddLog("画像の読み込みに失敗しました。"); return; }
            if (writeshiBusy) return;
            writeshiBusy = true; Redraw();
            StartCoroutine(WriteshiNamingService.RequestTrueName(tex, name =>
            {
                var inst = PhotoWriteshi.Build(tex, name); // name が null ならオフライン候補で決定論的に命名
                WriteshiCollection.Add(inst);
                AddLog($"マイモン「{inst.definition.trueName}」を生成（{ElemJp(inst.definition.element)}/コスト{inst.definition.cost}/{inst.definition.attack}・{inst.definition.defense}）。");
                writeshiBusy = false;
                Redraw();
            }));
        }

        static string ElemJp(Element e) => e switch
        {
            Element.Honoo => "焔", Element.Mori => "森", Element.Nagare => "流",
            Element.Hikari => "光", Element.Kage => "影", _ => "?"
        };

        void PickFromGallery()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // PC（Windows）は標準のファイル選択ダイアログ。NativeGallery はWindowsビルドで動かないため。
            string path = StandaloneFilePicker.PickImagePath();
            if (string.IsNullOrEmpty(path)) { AddLog("写真が選択されませんでした。"); return; }
            var tex = StandaloneFilePicker.LoadTexture(path, 512);
            ProcessPhoto(tex);
#else
            // スマホ（Android/iOS）はアルバムを開く。
            NativeGallery.GetImageFromGallery(p =>
            {
                if (string.IsNullOrEmpty(p)) { AddLog("写真が選択されませんでした。"); return; }
                var t = NativeGallery.LoadImageAtPath(p, 512, false);
                ProcessPhoto(t);
            }, "写真を選択");
#endif
        }

        void TakePhoto()
        {
            // NativeCamera はエディタでは何も起きない（実機専用）。実機ではカメラを起動。
            if (!NativeCamera.DeviceHasCamera())
            {
                AddLog("この端末（またはエディタ）ではカメラを使えません。実機で試すか、アルバム/テストマイモンを使ってください。");
                return;
            }
            NativeCamera.TakePicture(path =>
            {
                if (string.IsNullOrEmpty(path)) { AddLog("撮影がキャンセルされました。"); return; }
                var tex = NativeCamera.LoadImageAtPath(path, 512, false);
                ProcessPhoto(tex);
            }, 512);
        }

        // プラグイン無しでもパイプラインを確認するため、ランダムな単色ノイズ画像からマイモンを作る。
        void AddTestWriteshi()
        {
            int n = WriteshiCollection.Count;
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var rng = new System.Random(unchecked(System.Environment.TickCount + n * 7919));
            Color baseCol = new Color((float)rng.NextDouble(), (float)rng.NextDouble(), (float)rng.NextDouble());
            var px = new Color32[64 * 64];
            for (int i = 0; i < px.Length; i++)
            {
                float j = (float)rng.NextDouble() * 0.2f;
                px[i] = new Color(Mathf.Clamp01(baseCol.r + j), Mathf.Clamp01(baseCol.g + j), Mathf.Clamp01(baseCol.b + j));
            }
            tex.SetPixels32(px); tex.Apply();
            // テストはAPIを叩かずオフライン名で即生成
            var inst = PhotoWriteshi.Build(tex, null);
            WriteshiCollection.Add(inst);
            AddLog($"テストマイモン「{inst.definition.trueName}」を生成（{ElemJp(inst.definition.element)}/コスト{inst.definition.cost}/{inst.definition.attack}・{inst.definition.defense}）。");
            Redraw();
        }

        void DrawPassScreen()
        {
            var next = engine.players[engine.currentPlayer];
            var dim = MakePanel(root, new Color(0.03f, 0.03f, 0.05f, 1f), "PassScreen");
            Stretch(dim.rectTransform);

            var t = MakeChildText(dim.transform, $"{next.name} の番です\n\n画面を {next.name} に渡してください", 30, TextAnchor.MiddleCenter, Color.white);
            var trt = t.rectTransform;
            trt.anchorMin = trt.anchorMax = trt.pivot = new Vector2(0.5f, 0.5f);
            trt.anchoredPosition = new Vector2(0, 60); trt.sizeDelta = new Vector2(900, 200);

            var b = MakeCenterButton("めくる（自分の番を開始）", new Vector2(0, -90), new Vector2(400, 64), new Color(0.35f, 0.45f, 0.6f));
            b.onClick.AddListener(() => { awaitingPass = false; Redraw(); });
        }

        void DrawGameOver()
        {
            var winner = engine.result == GameResult.Player0Win ? engine.players[0] : engine.players[1];
            string msg = isLocalPvP ? $"{winner.name} の勝利！"
                                    : (engine.result == GameResult.Player0Win ? "あなたの勝利！" : "あなたの敗北…");
            var banner = MakeChildText(root, msg, 48, TextAnchor.MiddleCenter, Color.yellow);
            var brt = banner.rectTransform;
            brt.anchorMin = brt.anchorMax = brt.pivot = new Vector2(0.5f, 0.5f);
            brt.anchoredPosition = new Vector2(0, 60); brt.sizeDelta = new Vector2(700, 100);

            var again = MakeCenterButton("もう一度", new Vector2(-95, -30), new Vector2(170, 54), new Color(0.32f, 0.5f, 0.4f));
            again.onClick.AddListener(() => StartGame(lastWasAI));
            var toTitle = MakeCenterButton("タイトルへ", new Vector2(95, -30), new Vector2(170, 54), new Color(0.45f, 0.4f, 0.5f));
            toTitle.onClick.AddListener(() => { inTitle = true; selectedAttacker = null; HideCardDetail(); Redraw(); });
        }

        Button MakeCenterButton(string label, Vector2 centerPos, Vector2 size, Color color)
        {
            var go = new GameObject("CButton");
            go.transform.SetParent(root, false);
            var img = go.AddComponent<Image>(); img.color = color;
            var btn = go.AddComponent<Button>();
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = centerPos; rt.sizeDelta = size;
            var t = MakeChildText(go.transform, label, 20, TextAnchor.MiddleCenter, Color.white);
            Stretch(t.rectTransform);
            return btn;
        }

        // ───────── UI部品 ─────────
        void DrawLogPanel()
        {
            var panel = MakePanel(root, new Color(0,0,0,0.45f), "LogPanel");
            panel.raycastTarget = false; // クリックをカードへ透過（ログは表示専用）
            var rt = panel.rectTransform;
            // 右側中央（ボードのカードは左から並ぶので重ならない）
            rt.anchorMin = new Vector2(1, 0.5f); rt.anchorMax = new Vector2(1, 0.5f);
            rt.pivot = new Vector2(1, 0.5f);
            rt.anchoredPosition = new Vector2(-16, 0);
            rt.sizeDelta = new Vector2(360, 170);

            logText = MakeChildText(panel.transform, string.Join("\n", logLines), 14, TextAnchor.LowerLeft, new Color(0.86f,0.86f,0.86f));
            Stretch(logText.rectTransform, 8);
        }

        void AddLog(string s)
        {
            logLines.Add(s);
            while (logLines.Count > 9) logLines.RemoveAt(0);
            if (logText != null) logText.text = string.Join("\n", logLines);
        }

        // 石板/遺跡テーマのカード枠（C案）。系統色は枠の縁、技は簡易テキスト、詳細はホバーで表示。
        // カード裏面（石板に彫り込んだ「マイモン」の紋章）。相手の手札・デッキに使う。
        GameObject MakeCardBack(Transform parent, Vector2 pos, Vector2 size, bool fromTop=false, bool fromBottom=false, bool anchorRight=false)
        {
            float s = size.x / 128f; // 128幅を基準にした拡縮
            Color edge = new Color(0.33f, 0.27f, 0.42f);   // 記憶＝深い紫
            Color bronzeCol = new Color(0.62f, 0.49f, 0.27f);
            Color stone = new Color(0.13f, 0.12f, 0.15f);

            var go = new GameObject("CardBack");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = edge; img.raycastTarget = false;
            SetAnchor(img.rectTransform, pos, size, fromTop, fromBottom, anchorRight);

            // 銅トリム
            var bronze = new GameObject("Bronze"); bronze.transform.SetParent(go.transform, false);
            var bi = bronze.AddComponent<Image>(); bi.color = bronzeCol; bi.raycastTarget = false;
            var brt = bi.rectTransform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(3*s,3*s); brt.offsetMax = new Vector2(-3*s,-3*s);

            // 石板の面
            var face = new GameObject("Face"); face.transform.SetParent(go.transform, false);
            var fi = face.AddComponent<Image>(); fi.color = stone; fi.raycastTarget = false;
            var frt = fi.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(5*s,5*s); frt.offsetMax = new Vector2(-5*s,-5*s);

            // 中央の同心円彫紋
            Vector2 ctr = new Vector2(0.5f, 0.5f);
            MakeCircle(go.transform, bronzeCol,                       ctr, Vector2.zero, 78*s);
            MakeCircle(go.transform, stone,                           ctr, Vector2.zero, 70*s);
            MakeCircle(go.transform, bronzeCol * 0.85f,               ctr, Vector2.zero, 50*s);
            MakeCircle(go.transform, new Color(0.10f,0.09f,0.11f),    ctr, Vector2.zero, 44*s);

            // 中央の回転菱形（系統を表さない統一の宝珠）
            void Diamond(Vector2 center, float sz, Color col)
            {
                var d = new GameObject("Diamond"); d.transform.SetParent(go.transform, false);
                var di = d.AddComponent<Image>(); di.color = col; di.raycastTarget = false;
                var drt = di.rectTransform;
                drt.anchorMin = drt.anchorMax = new Vector2(0.5f,0.5f); drt.pivot = new Vector2(0.5f,0.5f);
                drt.anchoredPosition = center; drt.sizeDelta = new Vector2(sz, sz);
                d.transform.localEulerAngles = new Vector3(0,0,45);
                var o = d.AddComponent<Outline>(); o.effectColor = bronzeCol; o.effectDistance = new Vector2(1f*s,-1f*s);
            }
            Diamond(Vector2.zero, 30*s, edge * 1.2f);

            // 中央の「記」字
            var glyph = MakeChildText(go.transform, "記", Mathf.Max(8, Mathf.RoundToInt(20*s)),
                TextAnchor.MiddleCenter, new Color(0.93f,0.87f,0.73f));
            var grt2 = glyph.rectTransform;
            grt2.anchorMin = grt2.anchorMax = new Vector2(0.5f,0.5f); grt2.pivot = new Vector2(0.5f,0.5f);
            grt2.anchoredPosition = Vector2.zero; grt2.sizeDelta = new Vector2(30*s,30*s);

            // 四隅の小菱形
            float cx = size.x*0.5f - 13*s, cy = size.y*0.5f - 13*s;
            Diamond(new Vector2(-cx,  cy), 9*s, bronzeCol);
            Diamond(new Vector2( cx,  cy), 9*s, bronzeCol);
            Diamond(new Vector2(-cx, -cy), 9*s, bronzeCol);
            Diamond(new Vector2( cx, -cy), 9*s, bronzeCol);

            return go;
        }

        static Sprite s_frame; static bool s_frameLoaded;
        Sprite GetFrameSprite()
        {
            if (!s_frameLoaded) { s_frame = Resources.Load<Sprite>("Frames/frame_base"); s_frameLoaded = true; }
            return s_frame;
        }

        // カードプレハブ（Assets/Resources/Card.prefab）。あればプレハブ方式、無ければ旧コード生成にフォールバック
        static CardView s_cardPrefab; static bool s_cardPrefabLoaded;
        CardView GetCardPrefab()
        {
            if (!s_cardPrefabLoaded) { s_cardPrefab = Resources.Load<CardView>("Card"); s_cardPrefabLoaded = true; }
            return s_cardPrefab;
        }

        Button MakeCardFromPrefab(CardView prefab, Transform parent, CardInstance c, CardData def,
            bool isGuardian, Color edge, Vector2 pos, bool fromTop, bool fromBottom)
        {
            var cv = Object.Instantiate(prefab, parent);
            var rt = cv.GetComponent<RectTransform>();
            SetAnchor(rt, pos, rt.sizeDelta, fromTop, fromBottom, false); // 大きさはプレハブ準拠

            string sub = isGuardian ? def.techniqueName
                       : def.kind == CardKind.Recollection ? "魔法" : "魔法石";
            string flags = isGuardian
                ? ((c.engravingCount > 0 ? $"刻{c.engravingCount} " : "") + (c.summoningSick ? "酔" : ""))
                : "";

            cv.Bind(jpFont, GetFrameSprite(), GetCardArt(def, edge),
                def.trueName, def.cost.ToString(), isGuardian,
                ElementName(def.element), c.CurrentAttack.ToString(), c.RemainingDefense.ToString(),
                sub, flags, def.guard);

            var go = cv.gameObject;
            var trig = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener((_) => ShowCardDetail(c));
            trig.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener((_) => { if (selectedAttacker != null) ShowCardDetail(selectedAttacker); else HideCardDetail(); });
            trig.triggers.Add(exit);

            return cv.button;
        }

        Button MakeCard(Transform parent, CardInstance c, Vector2 pos, bool fromTop=false, bool fromBottom=false)
        {
            var def = c.definition;
            bool isGuardian = def.kind == CardKind.Guardian;
            Color edge = isGuardian ? ElementColor[def.element]
                       : def.kind == CardKind.Recollection ? new Color(0.5f,0.5f,0.55f)
                       : new Color(0.6f,0.52f,0.38f);

            // プレハブがあればそちらで描画（エディタで調整可能）
            var cardPrefab = GetCardPrefab();
            if (cardPrefab != null)
                return MakeCardFromPrefab(cardPrefab, parent, c, def, isGuardian, edge, pos, fromTop, fromBottom);

            // 一点アンカー配置の小ヘルパー
            void PlaceAt(RectTransform rt, Vector2 a, Vector2 sz)
            {
                rt.anchorMin = rt.anchorMax = a; rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero; rt.sizeDelta = sz;
            }
            // 文字に黒縁取り（フレーム上でも読めるように）
            void Shade(Graphic g)
            {
                var o = g.gameObject.AddComponent<Outline>();
                o.effectColor = new Color(0,0,0,0.9f); o.effectDistance = new Vector2(1.2f,-1.2f);
            }

            Vector2 cardSize = new Vector2(128, 180); // フレーム画像(864x1216≒5:7)に合わせた比率

            // ルート＝フレーム画像＋クリック領域
            var go = new GameObject("Card_" + def.id);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            var btn = go.AddComponent<Button>();
            SetAnchor(img.rectTransform, pos, cardSize, fromTop, fromBottom, false);
            var frameSprite = GetFrameSprite();
            if (frameSprite != null) { img.sprite = frameSprite; img.color = Color.white; }
            else img.color = new Color(0.20f, 0.18f, 0.15f);

            // イラスト（窓の中。窓は正方形に近いので絵は幅いっぱい＋上下に石面が覗く）
            var art = new GameObject("Art"); art.transform.SetParent(go.transform, false);
            var artImg = art.AddComponent<Image>(); artImg.raycastTarget = false;
            var sprite = GetCardArt(def, edge);
            if (sprite != null) { artImg.sprite = sprite; artImg.color = Color.white; artImg.preserveAspect = false; }
            else artImg.color = new Color(edge.r*0.4f, edge.g*0.4f, edge.b*0.4f);
            var art_rt = artImg.rectTransform;
            art_rt.anchorMin = new Vector2(0.16f, 0.53f); art_rt.anchorMax = new Vector2(0.83f, 0.89f);
            art_rt.offsetMin = Vector2.zero; art_rt.offsetMax = Vector2.zero;

            // 名前バナー（クリーム文字＋黒縁でどの地でも読める）
            var nameT = MakeChildText(go.transform, def.trueName, 11, TextAnchor.MiddleCenter, new Color(0.97f,0.92f,0.80f));
            var nrt = nameT.rectTransform;
            nrt.anchorMin = new Vector2(0.31f,0.46f); nrt.anchorMax = new Vector2(0.71f,0.51f);
            nrt.offsetMin = Vector2.zero; nrt.offsetMax = Vector2.zero;
            Shade(nameT);

            // 守護バッジ（イラスト窓の左上・暗い下地付きで必ず読める）
            if (def.guard)
            {
                var bg = new GameObject("GuardBadge"); bg.transform.SetParent(go.transform, false);
                var bgImg = bg.AddComponent<Image>(); bgImg.color = new Color(0.05f,0.09f,0.16f,0.88f); bgImg.raycastTarget = false;
                var br = bgImg.rectTransform;
                br.anchorMin = new Vector2(0.17f,0.795f); br.anchorMax = new Vector2(0.47f,0.875f);
                br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;
                var bo = bg.AddComponent<Outline>(); bo.effectColor = new Color(0.45f,0.75f,1f,0.95f); bo.effectDistance = new Vector2(1f,-1f);
                var gT = MakeChildText(go.transform, "守護", 11, TextAnchor.MiddleCenter, new Color(0.78f,0.93f,1f));
                var gr = gT.rectTransform;
                gr.anchorMin = new Vector2(0.17f,0.795f); gr.anchorMax = new Vector2(0.47f,0.875f);
                gr.offsetMin = Vector2.zero; gr.offsetMax = Vector2.zero;
                Shade(gT);
            }

            // 技/種別テキスト（最下部パネル）
            string sub = isGuardian ? def.techniqueName
                       : def.kind == CardKind.Recollection ? "魔法" : "魔法石";
            var subT = MakeChildText(go.transform, sub, 11, TextAnchor.MiddleCenter, new Color(0.95f,0.89f,0.75f));
            var subrt = subT.rectTransform;
            subrt.anchorMin = new Vector2(0.12f,0.05f); subrt.anchorMax = new Vector2(0.88f,0.17f);
            subrt.offsetMin = Vector2.zero; subrt.offsetMax = Vector2.zero;
            Shade(subT);

            // コスト（名前帯の左の円・金）
            var costT = MakeChildText(go.transform, def.cost.ToString(), 15, TextAnchor.MiddleCenter, new Color(1f,0.86f,0.42f));
            PlaceAt(costT.rectTransform, new Vector2(0.148f,0.487f), new Vector2(22,22));
            Shade(costT);

            if (isGuardian)
            {
                // 系統文字（名前帯の右の菱形・白）
                var elT = MakeChildText(go.transform, ElementName(def.element), 11, TextAnchor.MiddleCenter, new Color(1f,0.97f,0.88f));
                PlaceAt(elT.rectTransform, new Vector2(0.865f,0.477f), new Vector2(20,20));
                Shade(elT);

                // 攻撃（左の丸ソケット・赤）／ 防御（右の丸ソケット・青）
                var atkT = MakeChildText(go.transform, c.CurrentAttack.ToString(), 15, TextAnchor.MiddleCenter, new Color(1f,0.55f,0.42f));
                PlaceAt(atkT.rectTransform, new Vector2(0.810f,0.266f), new Vector2(22,22));
                Shade(atkT);
                var defT = MakeChildText(go.transform, c.RemainingDefense.ToString(), 15, TextAnchor.MiddleCenter, new Color(0.58f,0.82f,1f));
                PlaceAt(defT.rectTransform, new Vector2(0.189f,0.266f), new Vector2(22,22));
                Shade(defT);

                // 刻印 / 召喚酔い（中央の装飾帯）
                string flags = (c.engravingCount > 0 ? $"刻{c.engravingCount} " : "") + (c.summoningSick ? "酔" : "");
                if (flags.Length > 0)
                {
                    var fT = MakeChildText(go.transform, flags, 9, TextAnchor.MiddleCenter, new Color(0.96f,0.86f,0.52f));
                    PlaceAt(fT.rectTransform, new Vector2(0.5f,0.40f), new Vector2(64,14));
                    Shade(fT);
                }
            }

            // ホバーでスキル詳細を表示
            var trig = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener((_) => ShowCardDetail(c));
            trig.triggers.Add(enter);
            var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
            exit.callback.AddListener((_) => { if (selectedAttacker != null) ShowCardDetail(selectedAttacker); else HideCardDetail(); });
            trig.triggers.Add(exit);

            return btn;
        }

        // 彫り込み円バッジ用の円スプライト（コスト・攻撃・防御）
        static Sprite s_circle;
        Sprite CircleSprite()
        {
            if (s_circle != null) return s_circle;
            int sz = 48;
            var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            float c = sz / 2f - 0.5f, r = sz / 2f - 1f;
            for (int y = 0; y < sz; y++)
                for (int x = 0; x < sz; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    tex.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp01(r - d)));
                }
            tex.Apply();
            s_circle = Sprite.Create(tex, new Rect(0, 0, sz, sz), new Vector2(0.5f, 0.5f));
            return s_circle;
        }

        Image MakeCircle(Transform parent, Color color, Vector2 anchor, Vector2 anchoredPos, float size)
        {
            var go = new GameObject("Circle");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = CircleSprite(); img.color = color; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f); // 中心基準（anchoredPos=円の中心位置）
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = new Vector2(size, size);
            return img;
        }

        // 盾型スプライト（攻撃/防御バッジ用）。上は平ら、下は中央へ尖る紋章形。
        static Sprite s_shield;
        Sprite ShieldSprite()
        {
            if (s_shield != null) return s_shield;
            int w = 44, h = 52;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int hit = 0;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                        {
                            float nx = (x + 0.25f + sx * 0.5f) / w;
                            float ny = (y + 0.25f + sy * 0.5f) / h; // ny=0 が下
                            float half;
                            if (ny >= 0.40f) half = 0.44f;                 // 上部：直線の側辺
                            else half = 0.44f * (ny / 0.40f);              // 下部：中央へ尖る
                            if (Mathf.Abs(nx - 0.5f) < half && ny <= 0.98f) hit++;
                        }
                    tex.SetPixel(x, y, new Color(1, 1, 1, hit / 4f));
                }
            tex.Apply();
            s_shield = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
            return s_shield;
        }

        // 盾バッジ（攻撃/防御）：盾スプライト＋縁色＋数字
        void MakeShield(Transform parent, Vector2 anchor, Vector2 anchoredPos, Color rim, string num, Color numColor)
        {
            var go = new GameObject("Shield");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = ShieldSprite(); img.color = new Color(0.10f,0.09f,0.075f); img.raycastTarget = false;
            var rt = img.rectTransform; rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = anchoredPos; rt.sizeDelta = new Vector2(30, 36);
            var o = go.AddComponent<Outline>(); o.effectColor = rim; o.effectDistance = new Vector2(1.5f, -1.5f);
            var t = MakeChildText(go.transform, num, 15, TextAnchor.MiddleCenter, numColor);
            var trt = t.rectTransform;
            trt.anchorMin = new Vector2(0, 0.22f); trt.anchorMax = new Vector2(1, 1);
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;
        }

        // ───────── カードアート（読み込み or 自動生成） ─────────
        static readonly Dictionary<string, Sprite> s_artCache = new Dictionary<string, Sprite>();

        Sprite GetCardArt(CardData def, Color tint)
        {
            if (s_artCache.TryGetValue(def.id, out var cached)) return cached;

            // 0) マイモンは登録済みの写真スプライトを最優先
            if (GeneratedArt.IsGenerated(def.id))
            {
                var g = GeneratedArt.Get(def.id);
                if (g != null) { s_artCache[def.id] = g; return g; }
            }

            Sprite sp = null;
            // 1) ユーザー画像を優先（Resources/CardArt/ 配下）
            //    モンスターは guardian_001〜030.png、それ以外は {id}.png でも可
            if (def.kind == CardKind.Guardian && def.id.Length > 1 && int.TryParse(def.id.Substring(1), out int gi))
                sp = Resources.Load<Sprite>($"CardArt/guardian_{gi:000}");
            if (sp == null) sp = Resources.Load<Sprite>($"CardArt/{def.id}");
            // 2) 無ければカードIDから決定論的に自動生成（系統カラーの抽象エンブレム）
            if (sp == null) sp = GenerateProceduralArt(def, tint);

            s_artCache[def.id] = sp;
            return sp;
        }

        Sprite GenerateProceduralArt(CardData def, Color tint)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            uint h = Fnv(def.id);
            var rng = new System.Random(unchecked((int)h));

            Color top = tint * 0.6f; top.a = 1f;
            Color bottom = tint * 0.18f; bottom.a = 1f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, Color.Lerp(bottom, top, (float)y / size));

            // 決定論的な幾何模様（カードごとに異なる）
            int shapes = 4 + (int)(h % 5);
            for (int i = 0; i < shapes; i++)
            {
                int cx = rng.Next(size), cy = rng.Next(size), r = 4 + rng.Next(13);
                Color col = tint * (0.7f + (float)rng.NextDouble() * 0.7f); col.a = 1f;
                for (int yy = -r; yy <= r; yy++)
                    for (int xx = -r; xx <= r; xx++)
                    {
                        if (xx * xx + yy * yy > r * r) continue;
                        int px = cx + xx, py = cy + yy;
                        if (px >= 0 && px < size && py >= 0 && py < size) tex.SetPixel(px, py, col);
                    }
            }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }

        static uint Fnv(string s)
        {
            uint h = 2166136261;
            foreach (char c in s) { h ^= c; h *= 16777619; }
            return h;
        }

        Image MakePanel(Transform parent, Color color, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            return img;
        }

        // 殿堂の進捗（完全刻印のモンスターが殿堂に何体か）
        static int PactCount(RecallerState p) => p.memoryZone.Count(c => c.engravingCount >= GameEngine.PactEngraving);

        // テキストに黒縁取り（背景画像の上でも読めるように）
        static void OutlineText(Text t)
        {
            var o = t.gameObject.AddComponent<Outline>();
            o.effectColor = new Color(0, 0, 0, 0.9f); o.effectDistance = new Vector2(1.2f, -1.2f);
        }

        Text MakeLabel(Transform parent, string text, Vector2 pos, Vector2 size, int fontSize,
            TextAnchor anchor, Color color, bool fromTop=true, bool fromBottom=false, bool anchorRight=false)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            ApplyText(t, text, fontSize, anchor, color);
            SetAnchor(t.rectTransform, pos, size, fromTop, fromBottom, anchorRight);
            return t;
        }

        Button MakeButton(Transform parent, string label, Vector2 pos, Vector2 size, Color color,
            bool fromTop=false, bool fromBottom=false, bool anchorRight=false)
        {
            var go = new GameObject("Button");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = color;
            var btn = go.AddComponent<Button>();
            SetAnchor(img.rectTransform, pos, size, fromTop, fromBottom, anchorRight);
            var t = MakeChildText(go.transform, label, 16, TextAnchor.MiddleCenter, Color.white);
            Stretch(t.rectTransform);
            return btn;
        }

        Text MakeChildText(Transform parent, string text, int fontSize, TextAnchor anchor, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            ApplyText(t, text, fontSize, anchor, color);
            t.raycastTarget = false;
            return t;
        }

        void ApplyText(Text t, string text, int fontSize, TextAnchor anchor, Color color)
        {
            t.text = text; t.font = jpFont; t.fontSize = fontSize; t.alignment = anchor;
            t.color = color; t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
        }

        // ──── レイアウトヘルパ ────
        void SetAnchor(RectTransform rt, Vector2 pos, Vector2 size, bool fromTop, bool fromBottom, bool anchorRight)
        {
            float ax = anchorRight ? 1 : 0;
            float ay = fromBottom ? 0 : 1;
            rt.anchorMin = new Vector2(ax, ay); rt.anchorMax = new Vector2(ax, ay);
            rt.pivot = new Vector2(ax, ay);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        void Stretch(RectTransform rt, float pad = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad); rt.offsetMax = new Vector2(-pad, -pad);
        }
        void Center(RectTransform rt)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
        }

        void Outline(GameObject go, Color c)
        {
            var o = go.AddComponent<Outline>();
            o.effectColor = c; o.effectDistance = new Vector2(3, 3);
        }

        // ───────── 🎤 ここから音声認識の連動コード ─────────
        private void OnEnable()
        {
            if (engine != null)
            {
                engine.OnVoiceAttackRequest += OnVoiceAttackCommandReceived;
                engine.OnVoiceDirectAttackRequest += OnVoiceDirectAttackCommandReceived;
            }
        }

        private void OnDisable()
        {
            if (engine != null)
            {
                engine.OnVoiceAttackRequest -= OnVoiceAttackCommandReceived;
                engine.OnVoiceDirectAttackRequest -= OnVoiceDirectAttackCommandReceived;
            }
        }

        // 選択中モンスターが今すぐ攻撃できるか（カード選択は手動、攻撃実行だけ音声で行う）
        private bool VoiceAttackerReady(out CardInstance g)
        {
            g = selectedAttacker;
            if (engine == null || !MyActiveTurn) return false;
            if (g == null) { AddLog("⚔️ ボイス：先に攻撃する自分のモンスターを選んでください。"); Redraw(); return false; }
            if (g.summoningSick || g.attackedThisTurn || g.CurrentAttack <= 0)
            { AddLog("⚔️ ボイス：そのモンスターは今攻撃できません。"); Redraw(); return false; }
            return true;
        }

        // 🎤「いけっ」：選択中モンスターで敵モンスターへ攻撃（守護優先・いなければ本体）。実行まで行う。
        private void OnVoiceAttackCommandReceived()
        {
            if (!VoiceAttackerReady(out var g)) return;
            var foe = engine.Opp;
            CardInstance target = null;
            // 1) プレイヤーがクリックで選んだ敵を最優先
            if (selectedTarget != null && foe.board.Contains(selectedTarget)) target = selectedTarget;
            // 2) 未選択なら守護優先→先頭（守護がいれば守護しか殴れない）
            if (target == null) target = foe.board.FirstOrDefault(c => c.definition.guard && c.RemainingDefense > 0);
            if (target == null && foe.board.Count > 0) target = foe.board[0];
            Debug.Log($"⚔️ [ボイス] {g.definition.trueName} → {(target != null ? target.definition.trueName : "本体")} 攻撃実行");
            engine.Attack(g, target);
            selectedAttacker = null; selectedTarget = null; mode = Mode.Normal;
            AfterHumanAction();
        }

        // 🎤「くらえ」：選択中モンスターで本体を直接攻撃（守護がいると不可）。実行まで行う。
        private void OnVoiceDirectAttackCommandReceived()
        {
            if (!VoiceAttackerReady(out var g)) return;
            var foe = engine.Opp;
            if (foe.board.Any(c => c.definition.guard && c.RemainingDefense > 0))
            { AddLog("⚔️ ボイス：守護がいるため本体を攻撃できません。"); Redraw(); return; }
            Debug.Log($"⚔️ [ボイス] {g.definition.trueName} → 本体 直接攻撃実行");
            engine.Attack(g, null);
            selectedAttacker = null; mode = Mode.Normal;
            AfterHumanAction();
        }
        // ───────── 🎤 ここまで音声認識の連動コード ─────────

    } // 💡 GameUIクラスを閉じる
} // 💡 namespaceを閉じる     