using System;
using System.Collections.Generic;
using System.Linq;

namespace KiokuNoIseki
{
    // 山札（19章 RuinsDeck）
    public class RuinsDeck
    {
        public List<CardInstance> cards = new List<CardInstance>();
        readonly Random rng;
        public RuinsDeck(Random r) { rng = r; }

        public int Count => cards.Count;

        public void Shuffle()
        {
            for (int i = cards.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (cards[i], cards[j]) = (cards[j], cards[i]);
            }
        }

        public CardInstance DrawTop()
        {
            if (cards.Count == 0) return null;
            var c = cards[0];
            cards.RemoveAt(0);
            return c;
        }

        // 復帰：ランダムな位置に戻す（10章）
        public void ReturnToRandomPosition(CardInstance card)
        {
            int idx = rng.Next(cards.Count + 1);
            cards.Insert(idx, card);
        }

        // 呪文の戻し処理（一番下）
        public void ReturnToBottom(CardInstance card) => cards.Add(card);

        public List<CardInstance> PeekTop(int n) => cards.Take(n).ToList();
    }

    public enum GameResult { Ongoing, Player0Win, Player1Win }

    // ゲーム全体の進行と行動。Human/AIどちらも GameActions を呼ぶ（17-1）
    public class GameEngine
    {
        // ── 古き盟約のバランス調整パラメータ（バランスシミュレーターで探索する） ──
        // 強化がこの数に達したユニットが破壊されると勝利ゾーンへ（＝完全強化）。
        public static int MemoryEntryEngraving = 4;
        // 勝利ゾーンにこの体数が集まると盟約勝利（バランス調整で3→2）。
        public static int PactWinCount = 2;
        // 勝利ゾーン入りしたカードが持つ強化数（＝盟約カウント対象の閾値）。
        public static int PactEngraving => MemoryEntryEngraving - 1;
        // 盤面で1ターン生き延びるごとに得る強化（自分で狙える成熟経路。守護で守る価値も上がる）。
        public static int EngraveOnSurvive = 1;
        // 完全強化のユニットが破壊されたら勝利ゾーンへ。
        // true=持ち主の勝利ゾーン（自分で盟約を狙える）／false=破壊した側（従来）。
        public static bool BankToOwner = true;

        public RecallerState[] players = new RecallerState[2];
        public RuinsDeck deck;
        public int currentPlayer;          // 0 or 1
        public TurnPhase phase;
        public int turnNumber;
        public GameResult result = GameResult.Ongoing;
        public readonly Random rng;

        public RecallerState Cur => players[currentPlayer];
        public RecallerState Opp => players[1 - currentPlayer];

        // 【v2】山札に合流させる写し身（両者ぶんまとめて）。null/空なら固定48枚のまま。
        // 与えた写し身の枚数だけ、固定ユニット(30枚のうち)をランダムに置き換える（5章・13章末尾の方針）。
        public List<CardInstance> injectedWriteshi;

        // UIへ通知するログ
        public Action<string> OnLog;
        public Action OnStateChanged;
        void Log(string s) { OnLog?.Invoke(s); }

        public GameEngine(int seed = 0)
        {
            rng = seed == 0 ? new Random() : new Random(seed);
        }

        public void NewGame(bool player1IsAI)
        {
            players[0] = new RecallerState { name = player1IsAI ? "あなた" : "プレイヤー1", isAI = false };
            players[1] = new RecallerState { name = player1IsAI ? "AIプレイヤー" : "プレイヤー2", isAI = player1IsAI };

            deck = new RuinsDeck(rng);
            var defs = CardDatabase.BuildDeckDefinitions();

            // 写し身の差し替え：与えた枚数だけ固定ユニットをランダムに除外し、写し身を代わりに入れる。
            int replace = 0;
            HashSet<CardData> removed = null;
            if (injectedWriteshi != null && injectedWriteshi.Count > 0)
            {
                int guardianCount = defs.Count(d => d.kind == CardKind.Guardian);
                replace = Math.Min(injectedWriteshi.Count, guardianCount);
                removed = new HashSet<CardData>(
                    defs.Where(d => d.kind == CardKind.Guardian)
                        .OrderBy(_ => rng.Next())
                        .Take(replace));
                Log($"写し身 {replace} 体が固定ユニットと置き換わって山札に合流した。");
            }

            foreach (var def in defs)
                if (removed == null || !removed.Contains(def))
                    deck.cards.Add(new CardInstance(def));
            if (replace > 0)
                for (int i = 0; i < replace; i++)
                    deck.cards.Add(injectedWriteshi[i]);

            deck.Shuffle();

            // 各プレイヤー5枚ドロー
            for (int p = 0; p < 2; p++)
                for (int i = 0; i < 5; i++)
                    DrawInto(players[p]);

            // 先手後手（0が先手固定、後手は+1枚）
            currentPlayer = 0;
            DrawInto(players[1]);
            Log("後手が1枚多くドローした。");

            turnNumber = 1;
            result = GameResult.Ongoing;
            BeginTurn();
        }

        void DrawInto(RecallerState p)
        {
            var c = deck.DrawTop();
            if (c == null) return;
            c.turnsInHand = 0;
            if (p.hand.Count < p.HandLimit + 10) // 物理上限は緩め（手札溢れは終了フェイズで処理しない簡易版）
                p.hand.Add(c);
        }

        // ───────── ターン進行 ─────────
        public void BeginTurn()
        {
            phase = TurnPhase.Decay;
            var p = Cur;
            p.inscribedThisTurn = false;
            p.handLimitModifierThisTurn = 0;
            foreach (var c in p.board) c.ResetForNewTurn();
            foreach (var c in p.board) c.summoningSick = false; // 自分の番が来たら酔い解除

            // マナ減衰フェイズ
            if (p.skipNextDecay)
            {
                p.skipNextDecay = false;
                Log($"{p.name}: 前ターンにマナ加速したため減衰なし。");
            }
            else
            {
                p.recallGauge = Math.Max(0, p.recallGauge - 1);
            }

            // ドローフェイズ
            phase = TurnPhase.Excavate;
            ExcavateForCurrent();

            phase = TurnPhase.Inscribe; // マナ加速（任意）→そのまま行動可能に
            phase = TurnPhase.Action;
            OnStateChanged?.Invoke();
        }

        void ExcavateForCurrent()
        {
            if (deck.Count == 0) { CheckDeckExhaustion(); return; }

            var p = Cur;
            // 導きの残光：上3枚から最良を選んで取得（自動）
            if (p.digSelectPending)
            {
                p.digSelectPending = false;
                var top = deck.PeekTop(3);
                var pick = top.OrderByDescending(c => c.definition.cost).First();
                deck.cards.Remove(pick);
                pick.turnsInHand = 0;
                p.hand.Add(pick);
                Log($"{p.name}: 導きの残光で「{pick.definition.trueName}」を選んでドロー。");
            }
            else
            {
                DrawInto(p);
            }

            // 追加ドロー予約
            while (p.extraExcavatePending > 0 && deck.Count > 0)
            {
                p.extraExcavatePending--;
                DrawInto(p);
            }
        }

        public void CheckDeckExhaustion()
        {
            if (deck.Count > 0) return;
            // 山札枯渇勝利（11章）
            int m0 = players[0].memoryZone.Count, m1 = players[1].memoryZone.Count;
            Log("山札が尽きた。勝利ゾーンの枚数を比較する。");
            if (m0 != m1) result = m0 > m1 ? GameResult.Player0Win : GameResult.Player1Win;
            else result = players[0].hp >= players[1].hp ? GameResult.Player0Win : GameResult.Player1Win;
            OnStateChanged?.Invoke();
        }

        // マナ加速フェイズ（7-3）
        public bool Inscribe(CardInstance sacrificed)
        {
            if (result != GameResult.Ongoing) return false;
            var p = Cur;
            if (p.inscribedThisTurn) return false;
            if (sacrificed == null || !p.hand.Contains(sacrificed)) return false;

            p.hand.Remove(sacrificed); // ゲームから除外
            int up = 1 + (p.HasShrineAltar ? 1 : 0);
            p.recallGaugeMax += up;
            p.recallGauge = p.recallGaugeMax; // 上限まで全回復
            p.inscribedThisTurn = true;
            p.skipNextDecay = true;
            Log($"{p.name}: 「{sacrificed.definition.trueName}」を捧げてマナ加速した（ゲージ上限+{up}）。");
            OnStateChanged?.Invoke();
            return true;
        }

        // カードをプレイ（7-4）。chosenTarget は呪文の手動指定対象（ユニットプレイ時は無視）
        public bool PlayCard(CardInstance card, CardInstance chosenTarget = null)
        {
            if (result != GameResult.Ongoing || phase != TurnPhase.Action) return false;
            var p = Cur;
            if (!p.hand.Contains(card)) return false;
            if (p.recallGauge < card.definition.cost) return false;

            switch (card.definition.kind)
            {
                case CardKind.Guardian:
                    if (p.board.Count >= RecallerState.BoardLimit) return false;
                    p.hand.Remove(card);
                    p.recallGauge -= card.definition.cost;
                    card.summoningSick = true;
                    card.attackedThisTurn = false;
                    card.techniqueUsedThisTurn = false;
                    p.board.Add(card);
                    Log($"{p.name}: ユニット「{card.definition.trueName}」を召喚。");
                    break;

                case CardKind.Cornerstone:
                    if (p.cornerstones.Count >= RecallerState.CornerstoneLimit) return false;
                    p.hand.Remove(card);
                    p.recallGauge -= card.definition.cost;
                    p.cornerstones.Add(card);
                    p.RecountCornerstonePassives();
                    Log($"{p.name}: 設置カード「{card.definition.trueName}」を設置。");
                    break;

                case CardKind.Recollection:
                    p.hand.Remove(card);
                    p.recallGauge -= card.definition.cost;
                    Log($"{p.name}: 呪文「{card.definition.trueName}」を使用。");
                    EffectResolver.Resolve(this, p, Opp, null,
                        card.definition.spellEffect, card.definition.spellMagnitude, chosenTarget);
                    deck.ReturnToBottom(card); // 使用後は山札の一番下へ
                    break;
            }
            CheckWinConditions();
            OnStateChanged?.Invoke();
            return true;
        }

        // 攻撃（7-4）
        public bool Attack(CardInstance attacker, CardInstance target)
        {
            if (result != GameResult.Ongoing || phase != TurnPhase.Action) return false;
            var p = Cur; var o = Opp;
            if (!p.board.Contains(attacker)) return false;
            if (attacker.summoningSick || attacker.attackedThisTurn) return false;
            if (attacker.CurrentAttack <= 0) return false;

            // 守護：相手に守護持ちが場にいる間は本体(HP)を直接攻撃できない（先に守護を倒す）
            if (target == null && o.board.Any(g => g.definition.guard && g.RemainingDefense > 0))
            {
                Log("相手に守護がいるため本体を直接攻撃できない。");
                return false; // 行動権は消費しない
            }

            attacker.attackedThisTurn = true;

            if (target == null)
            {
                // 直接攻撃
                o.hp -= attacker.CurrentAttack;
                Log($"{attacker.definition.trueName} が相手に直接{attacker.CurrentAttack}ダメージ。");
            }
            else
            {
                if (!o.board.Contains(target)) return false;
                // 相互ダメージ
                target.damageTaken += attacker.CurrentAttack;
                attacker.damageTaken += target.CurrentAttack;
                Log($"{attacker.definition.trueName}({attacker.CurrentAttack}) が {target.definition.trueName}({target.CurrentAttack}) と交戦。");
                // 手番プレイヤーのカードを先に処理（12章 相討ち裁定）
                ResolveDestruction(p, attacker, byOpponent: o);
                ResolveDestruction(o, target, byOpponent: p);
            }
            CheckWinConditions();
            OnStateChanged?.Invoke();
            return true;
        }

        // 破壊判定→復帰（10章）
        public void ResolveDestruction(RecallerState owner, CardInstance card, RecallerState byOpponent)
        {
            if (card.RemainingDefense > 0) return;
            if (!owner.board.Contains(card)) return;
            owner.board.Remove(card);

            // 瓦礫の砦：自分のユニットが破壊されるたび、相手に1ダメージ
            if (owner.HasFortThorn && byOpponent != null)
            {
                byOpponent.hp -= 1;
                Log($"{owner.name}の瓦礫の砦：砕けた瓦礫が相手に1ダメージ。");
            }

            DoReincarnate(card, byOpponent, owner);
        }

        public void DoReincarnate(CardInstance card, RecallerState destroyer, RecallerState owner = null)
        {
            // リセットして強化+1
            card.damageTaken = 0;
            card.permAttackBuff = 0;
            card.permDefenseBuff = 0;
            card.turnAttackMod = 0;
            card.turnDefenseMod = 0;
            card.engravingCount += 1;
            card.summoningSick = false;
            card.techniqueUsedThisTurn = false;
            card.attackedThisTurn = false;

            // 勝利ゾーン入りの受け手：持ち主 or 破壊側（パラメータで切替）
            var banker = BankToOwner ? owner : destroyer;
            if (card.engravingCount >= MemoryEntryEngraving && banker != null)
            {
                // 完全強化に達した後の破壊→勝利ゾーンへ（11章 古き盟約用）
                card.engravingCount = PactEngraving;
                banker.memoryZone.Add(card);
                Log($"「{card.definition.trueName}」(完全強化) が {banker.name} の勝利ゾーンへ。");
            }
            else
            {
                deck.ReturnToRandomPosition(card);
                Log($"「{card.definition.trueName}」が強化{card.engravingCount}を得て山札へ復帰。");
            }
        }

        // 終了フェイズ→相手にターンを渡す（7-5, 9章）
        public void EndTurn()
        {
            if (result != GameResult.Ongoing) return;
            phase = TurnPhase.End;
            var p = Cur;

            // 生存強化：盤面で生き延びたユニットが強化を蓄積する（自分で狙える成熟経路）
            if (EngraveOnSurvive > 0)
                foreach (var c in p.board)
                    if (!c.summoningSick) c.engravingCount += EngraveOnSurvive;

            int graceTurns = p.HasSanctuary ? 4 : 3;
            for (int i = p.hand.Count - 1; i >= 0; i--)
            {
                var c = p.hand[i];
                c.turnsInHand++;
                if (c.turnsInHand >= graceTurns)
                {
                    c.weatheringCounter++;
                    if (c.weatheringCounter >= 3)
                    {
                        p.hand.RemoveAt(i);
                        Log($"{p.name}: 「{c.definition.trueName}」が劣化で消滅した（完全除外）。");
                    }
                }
            }
            // 手札上限の超過処理：上限を超えた分はコストの低い順に山札の下へ戻す
            while (p.hand.Count > p.HandLimit)
            {
                var discard = p.hand.OrderBy(c => c.definition.cost).First();
                p.hand.Remove(discard);
                deck.ReturnToBottom(discard);
                Log($"{p.name}: 手札上限({p.HandLimit})を超えたため「{discard.definition.trueName}」を山札へ戻した。");
            }
            p.handLimitModifierThisTurn = 0;

            // 手番交代
            currentPlayer = 1 - currentPlayer;
            if (currentPlayer == 0) turnNumber++;
            BeginTurn();
        }

        public void CheckWinConditions()
        {
            // 通常勝利
            if (players[0].hp <= 0) { result = GameResult.Player1Win; return; }
            if (players[1].hp <= 0) { result = GameResult.Player0Win; return; }
            // 古き盟約勝利：完全強化が勝利ゾーンに PactWinCount 体
            for (int i = 0; i < 2; i++)
            {
                int full = players[i].memoryZone.Count(c => c.engravingCount >= PactEngraving);
                if (full >= PactWinCount) { result = i == 0 ? GameResult.Player0Win : GameResult.Player1Win; return; }
            }
        }
    }

    // 技発動の集約点（16-2）。タップ（v1）と将来の音声（v2）が共にここを呼ぶ。
    public static class TechniqueActivator
    {
        // 戻り値: 発動成功か。失敗理由は reason に。
        public static bool TryActivate(GameEngine game, CardInstance guardian, out string reason)
        {
            reason = null;
            var owner = game.Cur;
            // 1. 自分の場にいるユニットか
            if (!owner.board.Contains(guardian)) { reason = "自分の場のユニットではない"; return false; }
            // 2. 自分の行動フェイズか
            if (game.phase != TurnPhase.Action) { reason = "行動フェイズではない"; return false; }
            // 3. 今ターン未発動か
            if (guardian.techniqueUsedThisTurn) { reason = "このユニットは今ターン技を使用済み"; return false; }
            // 4. ゲージ充足か（名前の灯篭で-1、最低1）
            int cost = guardian.definition.incantationCost;
            if (owner.HasNameLantern) cost = Math.Max(1, cost - 1);
            if (owner.recallGauge < cost) { reason = "ゲージ不足"; return false; }

            owner.recallGauge -= cost;
            guardian.techniqueUsedThisTurn = true;
            game.OnLog?.Invoke($"{guardian.definition.trueName} の技「{guardian.definition.techniqueName}」発動！");
            EffectResolver.Resolve(game, owner, game.Opp, guardian,
                guardian.definition.techniqueEffect, guardian.definition.techniqueMagnitude);
            game.CheckWinConditions();
            game.OnStateChanged?.Invoke();
            return true;
        }
    }
}
