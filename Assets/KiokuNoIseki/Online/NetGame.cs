using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using KiokuNoIseki;

namespace KiokuNoIseki.Online
{
    // 18章：ホスト権威の対戦同期。NGOの Named Message を使い、NetworkObject/プレハブ無しで実装する。
    //  - 正規の GameEngine はホストだけが保持
    //  - クライアントは操作要求(NetAction)を送るだけ。ホストが検証・実行し、視点ごとのGameViewを配信
    //  - 相手の手札は裏向き、デッキは残数のみ（隠し情報保護）
    public class NetGame
    {
        public static NetGame Instance;

        const string MSG_STATE = "KI_State";
        const string MSG_ACTION = "KI_Action";
        const string MSG_WRITESHI = "KI_Writeshi";  // client→host：自分のマイモン（写真なし）

        readonly List<GenCardInfo> clientWriteshi = new List<GenCardInfo>();
        bool clientWriteshiReceived;

        public bool IsHost;
        public GameView LatestView;                 // 受信者の現在ビュー（UIが描画）
        public System.Action OnViewUpdated;

        GameEngine engine;                          // ホストのみ
        readonly List<string> hostLog = new List<string>();

        NetworkManager NM => NetworkManager.Singleton;

        // ───────── 開始 ─────────
        public void StartHost()
        {
            IsHost = true;
            Instance = this;
            NM.CustomMessagingManager.RegisterNamedMessageHandler(MSG_ACTION, OnActionReceivedHost);
            NM.CustomMessagingManager.RegisterNamedMessageHandler(MSG_WRITESHI, OnWriteshiReceivedHost);
            NM.OnClientConnectedCallback += OnPeerConnectedHost;
            TryStartGame();
        }

        public void StartClient()
        {
            IsHost = false;
            Instance = this;
            NM.CustomMessagingManager.RegisterNamedMessageHandler(MSG_STATE, OnStateReceivedClient);
            NM.OnClientConnectedCallback += OnLocalConnectedClient;
            // すでに接続済みなら即送信（登録タイミングの取りこぼし対策）
            if (NM.IsConnectedClient) SendWriteshi();
        }

        // クライアント：自分の接続確立時に、自分のマイモン（写真なし）をホストへ送る。
        void OnLocalConnectedClient(ulong clientId)
        {
            if (clientId != NM.LocalClientId) return;
            SendWriteshi();
        }

        void SendWriteshi()
        {
            var wrap = new GenCardList
            {
                items = WriteshiCollection.Cards
                    .Select(c => GenCardInfo.From(c.definition)).ToArray()
            };
            SendBytes(MSG_WRITESHI, NetworkManager.ServerClientId,
                Encoding.UTF8.GetBytes(UnityEngine.JsonUtility.ToJson(wrap)), NetworkDelivery.Reliable);
        }

        // ホスト：クライアントのマイモンを受信 → 記録して開始判定
        void OnWriteshiReceivedHost(ulong sender, FastBufferReader reader)
        {
            var wrap = UnityEngine.JsonUtility.FromJson<GenCardList>(Encoding.UTF8.GetString(ReadBytes(reader)));
            clientWriteshi.Clear();
            if (wrap != null && wrap.items != null) clientWriteshi.AddRange(wrap.items);
            clientWriteshiReceived = true;
            TryStartGame();
        }

        public void Shutdown()
        {
            if (NM != null && NM.CustomMessagingManager != null)
            {
                NM.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_STATE);
                NM.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_ACTION);
            }
            if (NM != null)
            {
                NM.OnClientConnectedCallback -= OnPeerConnectedHost;
                NM.OnClientConnectedCallback -= OnLocalConnectedClient;
                if (NM.CustomMessagingManager != null)
                    NM.CustomMessagingManager.UnregisterNamedMessageHandler(MSG_WRITESHI);
            }
            engine = null;
            Instance = null;
        }

        void OnPeerConnectedHost(ulong clientId)
        {
            if (clientId == NM.LocalClientId) return; // 自分(ホスト)は無視
            TryStartGame();
        }

        void TryStartGame()
        {
            if (engine != null) return;
            if (NM.ConnectedClientsIds.Count < 2) return; // ホスト＋参加者の2人が必要
            if (!clientWriteshiReceived) return;          // 参加者のマイモン（空でも可）を受け取ってから開始

            engine = new GameEngine();
            engine.OnLog += s => { hostLog.Add(s); while (hostLog.Count > 8) hostLog.RemoveAt(0); };
            engine.OnStateChanged += BroadcastAll;

            // 両者のマイモンをデッキに合流させる（写真はホストには送られていない＝相手のは画像なし）。
            var injected = new List<CardInstance>();
            injected.AddRange(WriteshiCollection.Snapshot());                     // ホスト自身（写真は手元にある）
            foreach (var g in clientWriteshi) injected.Add(new CardInstance(g.ToCardData())); // 参加者（写真なし）
            engine.injectedWriteshi = injected.Count > 0 ? injected : null;

            engine.NewGame(player1IsAI: false); // host=player0, client=player1
            hostLog.Add("=== オンライン対戦 開始 ===");
            BroadcastAll();
        }

        // ───────── 操作（クライアント or ホストUIから呼ぶ） ─────────
        public void SubmitAction(NetAction a)
        {
            if (IsHost) { ExecuteAction(a, 0); return; }
            SendBytes(MSG_ACTION, NetworkManager.ServerClientId, Encoding.UTF8.GetBytes(NetJson.ToJson(a)),
                NetworkDelivery.Reliable);
        }

        void OnActionReceivedHost(ulong sender, FastBufferReader reader)
        {
            var a = NetJson.ActionFromJson(Encoding.UTF8.GetString(ReadBytes(reader)));
            ExecuteAction(a, 1); // 送信者は参加者=player1
        }

        void ExecuteAction(NetAction action, int owner)
        {
            if (engine == null || action == null) return;
            if (engine.result != GameResult.Ongoing) return;
            if (engine.currentPlayer != owner) return; // 自分の手番のみ有効（検証）

            switch ((NetActionType)action.type)
            {
                case NetActionType.EndTurn:
                    engine.EndTurn();
                    break;
                case NetActionType.PlayCard:
                {
                    var c = Find(action.a);
                    if (c != null) engine.PlayCard(c, action.b != 0 ? Find(action.b) : null);
                    break;
                }
                case NetActionType.Attack:
                {
                    var atk = Find(action.a);
                    var tgt = action.b == 0 ? null : Find(action.b);
                    if (atk != null) engine.Attack(atk, tgt);
                    break;
                }
                case NetActionType.Inscribe:
                {
                    var c = Find(action.a);
                    if (c != null) engine.Inscribe(c);
                    break;
                }
                case NetActionType.Technique:
                {
                    var g = Find(action.a);
                    if (g != null) TechniqueActivator.TryActivate(engine, g, out _);
                    break;
                }
            }
            // engine.OnStateChanged → BroadcastAll が走る
        }

        CardInstance Find(int iid)
        {
            if (iid == 0 || engine == null) return null;
            foreach (var p in engine.players)
            {
                foreach (var c in p.hand) if (c.instanceId == iid) return c;
                foreach (var c in p.board) if (c.instanceId == iid) return c;
                foreach (var c in p.cornerstones) if (c.instanceId == iid) return c;
            }
            return null;
        }

        // ───────── 配信（ホスト） ─────────
        void BroadcastAll()
        {
            if (engine == null) return;
            // ホスト自身(player0)はローカルで反映
            LatestView = BuildView(0);
            OnViewUpdated?.Invoke();
            // 参加者(player1)へ送信
            ulong remote = RemoteClientId();
            if (remote != ulong.MaxValue)
                SendBytes(MSG_STATE, remote, Encoding.UTF8.GetBytes(NetJson.ToJson(BuildView(1))),
                    NetworkDelivery.ReliableFragmentedSequenced);
        }

        ulong RemoteClientId()
        {
            foreach (var id in NM.ConnectedClientsIds)
                if (id != NM.LocalClientId) return id;
            return ulong.MaxValue;
        }

        void OnStateReceivedClient(ulong sender, FastBufferReader reader)
        {
            LatestView = NetJson.ViewFromJson(Encoding.UTF8.GetString(ReadBytes(reader)));
            OnViewUpdated?.Invoke();
        }

        // ───────── ビュー構築 ─────────
        GameView BuildView(int viewer)
        {
            var me = engine.players[viewer];
            var foe = engine.players[1 - viewer];
            int result = engine.result == GameResult.Ongoing ? 0
                : (engine.result == GameResult.Player0Win ? (viewer == 0 ? 1 : 2)
                                                          : (viewer == 1 ? 1 : 2));

            // このビューに写るマイモンの定義情報を集める（受信側が名前/技/系統を復元して描画する。写真は含めない）。
            var gen = new List<GenCardInfo>();
            var seen = new HashSet<string>();
            void AddGen(CardInstance c)
            {
                if (c != null && c.definition != null && c.definition.id.StartsWith("gen_") && seen.Add(c.definition.id))
                    gen.Add(GenCardInfo.From(c.definition));
            }
            foreach (var c in me.hand) AddGen(c);
            foreach (var c in me.board) AddGen(c);
            foreach (var c in foe.board) AddGen(c);
            if (engine.deck.Count > 0) AddGen(engine.deck.cards[0]); // デッキトップは公開情報

            return new GameView
            {
                genCards = gen.ToArray(),
                me = BuildPlayer(me, ownHand: true),
                foe = BuildPlayer(foe, ownHand: false),
                deckCount = engine.deck.Count,
                deckTopId = engine.deck.Count > 0 ? engine.deck.cards[0].definition.id : "",
                deckTopEng = engine.deck.Count > 0 ? engine.deck.cards[0].engravingCount : 0,
                phase = (int)engine.phase,
                myTurn = engine.currentPlayer == viewer,
                result = result,
                log = hostLog.ToArray()
            };
        }

        PlayerView BuildPlayer(RecallerState p, bool ownHand)
        {
            return new PlayerView
            {
                name = p.name,
                hp = p.hp,
                gauge = p.recallGauge,
                gaugeMax = p.recallGaugeMax,
                memoryCount = p.memoryZone.Count,
                pactCount = p.memoryZone.Count(c => c.engravingCount >= GameEngine.PactEngraving),
                rubble = p.rubbleTokens,
                inscribed = p.inscribedThisTurn,
                hand = p.hand.Select(c => CV(c, faceDown: !ownHand)).ToArray(),
                board = p.board.Select(c => CV(c, false)).ToArray(),
                cornerstones = p.cornerstones.Select(c => CV(c, false)).ToArray()
            };
        }

        static CardView CV(CardInstance c, bool faceDown)
        {
            if (faceDown)
                return new CardView { iid = c.instanceId, faceDown = true };
            return new CardView
            {
                iid = c.instanceId,
                cardId = c.definition.id,
                atk = c.CurrentAttack,
                def = c.RemainingDefense,
                engraving = c.engravingCount,
                sick = c.summoningSick,
                acted = c.attackedThisTurn,
                techDone = c.techniqueUsedThisTurn
            };
        }

        // ───────── 送受信ユーティリティ ─────────
        void SendBytes(string msg, ulong target, byte[] bytes, NetworkDelivery delivery)
        {
            if (NM == null || NM.CustomMessagingManager == null) return;
            var writer = new FastBufferWriter(bytes.Length + 8, Allocator.Temp);
            using (writer)
            {
                writer.WriteValueSafe(bytes.Length);
                writer.WriteBytesSafe(bytes, bytes.Length);
                NM.CustomMessagingManager.SendNamedMessage(msg, target, writer, delivery);
            }
        }

        static byte[] ReadBytes(FastBufferReader reader)
        {
            reader.ReadValueSafe(out int len);
            var bytes = new byte[len];
            reader.ReadBytesSafe(ref bytes, len);
            return bytes;
        }
    }
}
