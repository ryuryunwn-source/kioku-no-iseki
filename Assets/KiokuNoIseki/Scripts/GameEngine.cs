using System;
using System.Collections.Generic;
using System.Linq;

namespace KiokuNoIseki
{
    // 遺構デッキ（19章 RuinsDeck）
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

        // 転生：ランダムな位置に戻す（10章）
        public void ReturnToRandomPosition(CardInstance card)
        {
            int idx = rng.Next(cards.Count + 1);
            cards.Insert(idx, card);
        }

        // 想起術の戻し処理（一番下）
        public void ReturnToBottom(CardInstance card) => cards.Add(card);

        public List<CardInstance> PeekTop(int n) => cards.Take(n).ToList();
    }

    public enum GameResult { Ongoing, Player0Win, Player1Win }

    // ゲーム全体の進行と行動。Human/AIどちらも GameActions を呼ぶ（17-1）
    public class GameEngine
    {
        public RecallerState[] players = new RecallerState[2];
        public RuinsDeck deck;
        public int currentPlayer;          // 0 or 1
        public TurnPhase phase;
        public int turnNumber;
        public GameResult result = GameResult.Ongoing;
        public readonly Random rng;

        public RecallerState Cur => players[currentPlayer];
        public RecallerState Opp => players[1 - currentPlayer];

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
            players[1] = new RecallerState { name = player1IsAI ? "AI想起者" : "プレイヤー2", isAI = player1IsAI };

            deck = new RuinsDeck(rng);
            foreach (var def in CardDatabase.BuildDeckDefinitions())
                deck.cards.Add(new CardInstance(def));
            deck.Shuffle();

            // 各プレイヤー5枚発掘
            for (int p = 0; p < 2; p++)
                for (int i = 0; i < 5; i++)
                    DrawInto(players[p]);

            // 先手後手（0が先手固定、後手は+1枚）
            currentPlayer = 0;
            DrawInto(players[1]);
            Log("後手が1枚多く発掘した。");

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

            // 生存刻印（記憶領域リワーク）：前の自ターンから生き延びた守護者は刻印+1（攻撃/防御+1）
            foreach (var c in p.board)
            {
                c.engravingCount = Math.Min(3, c.engravingCount + 1);
                Log($"{p.name}: 「{c.definition.trueName}」が場で生き延び刻印{c.engravingCount}（攻防+1）。");
            }

            // 想起減衰フェイズ
            if (p.skipNextDecay)
            {
                p.skipNextDecay = false;
                Log($"{p.name}: 前ターンに刻んだため減衰なし。");
            }
            else
            {
                p.recallGauge = Math.Max(0, p.recallGauge - 1);
            }

            // 発掘フェイズ
            phase = TurnPhase.Excavate;
            ExcavateForCurrent();

            phase = TurnPhase.Inscribe; // 刻む（任意）→そのまま行動可能に
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
                Log($"{p.name}: 導きの残光で「{pick.definition.trueName}」を選んで発掘。");
            }
            else
            {
                DrawInto(p);
            }

            // 追加発掘予約
            while (p.extraExcavatePending > 0 && deck.Count > 0)
            {
                p.extraExcavatePending--;
                DrawInto(p);
            }
        }

        public void CheckDeckExhaustion()
        {
            if (deck.Count > 0) return;
            // 遺構枯渇勝利（11章）
            int m0 = players[0].memoryZone.Count, m1 = players[1].memoryZone.Count;
            Log("遺構デッキが尽きた。記憶領域の枚数を比較する。");
            if (m0 != m1) result = m0 > m1 ? GameResult.Player0Win : GameResult.Player1Win;
            else result = players[0].hp >= players[1].hp ? GameResult.Player0Win : GameResult.Player1Win;
            OnStateChanged?.Invoke();
        }

        // 刻むフェイズ（7-3）
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
            Log($"{p.name}: 「{sacrificed.definition.trueName}」を捧げて刻んだ（ゲージ上限+{up}）。");
            OnStateChanged?.Invoke();
            return true;
        }

        // カードをプレイ（7-4）。chosenTarget は想起術の手動指定対象（守護者プレイ時は無視）
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
                    Log($"{p.name}: 守護者「{card.definition.trueName}」を召喚。");
                    break;

                case CardKind.Cornerstone:
                    if (p.cornerstones.Count >= RecallerState.CornerstoneLimit) return false;
                    p.hand.Remove(card);
                    p.recallGauge -= card.definition.cost;
                    p.cornerstones.Add(card);
                    p.RecountCornerstonePassives();
                    Log($"{p.name}: 礎石「{card.definition.trueName}」を設置。");
                    break;

                case CardKind.Recollection:
                    p.hand.Remove(card);
                    p.recallGauge -= card.definition.cost;
                    Log($"{p.name}: 想起術「{card.definition.trueName}」を使用。");
                    EffectResolver.Resolve(this, p, Opp, null,
                        card.definition.spellEffect, card.definition.spellMagnitude, chosenTarget);
                    deck.ReturnToBottom(card); // 使用後は遺構の一番下へ
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

            attacker.attackedThisTurn = true;

            if (target == null)
            {
                int dmg = attacker.CurrentAttack;
                if (o.memoryZone.Count > 0)
                {
                    // 記憶領域＝盾（記憶領域リワーク）：残響が最も低いカードが受け、砕けたら共有デッキへ
                    var shield = o.memoryZone.OrderBy(c => c.RemainingDefense).First();
                    shield.damageTaken += dmg;
                    if (shield.RemainingDefense <= 0)
                    {
                        o.memoryZone.Remove(shield);
                        shield.damageTaken = 0; shield.turnAttackMod = 0; shield.turnDefenseMod = 0;
                        deck.ReturnToRandomPosition(shield); // 砕けて遺構へ（刻印保持）
                        Log($"{attacker.definition.trueName} の{dmg}ダメージで記憶領域「{shield.definition.trueName}」が砕け、刻印{shield.engravingCount}を保持して遺構へ戻った。");
                    }
                    else
                    {
                        Log($"{attacker.definition.trueName} の{dmg}ダメージを記憶領域「{shield.definition.trueName}」が受けた（残響 残り{shield.RemainingDefense}）。");
                    }
                }
                else
                {
                    // 記憶領域が空の時だけHPに通る
                    o.hp -= dmg;
                    Log($"{attacker.definition.trueName} が相手に直接{dmg}ダメージ。");
                }
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

        // 破壊判定→転生（10章）
        public void ResolveDestruction(RecallerState owner, CardInstance card, RecallerState byOpponent)
        {
            if (card.RemainingDefense > 0) return;
            if (!owner.board.Contains(card)) return;
            owner.board.Remove(card);
            DoReincarnate(card, byOpponent);
        }

        public void DoReincarnate(CardInstance card, RecallerState destroyer)
        {
            // リセットして刻印+1
            card.damageTaken = 0;
            card.permAttackBuff = 0;
            card.permDefenseBuff = 0;
            card.turnAttackMod = 0;
            card.turnDefenseMod = 0;
            card.engravingCount = Math.Min(3, card.engravingCount + 1);
            card.summoningSick = false;
            card.techniqueUsedThisTurn = false;
            card.attackedThisTurn = false;

            // 記憶領域リワーク：破壊された守護者は常に共有デッキへ転生（刻印保持）。
            // 記憶領域へは「自分の守護者が刻印3で自ターン終了時に昇華」する経路のみ（EndTurn参照）。
            deck.ReturnToRandomPosition(card);
            Log($"「{card.definition.trueName}」が刻印{card.engravingCount}を得て遺構へ転生。");
        }

        // 終了フェイズ→相手にターンを渡す（7-5, 9章）
        public void EndTurn()
        {
            if (result != GameResult.Ongoing) return;
            phase = TurnPhase.End;
            var p = Cur;
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
                        p.AddRubble(1);
                        Log($"{p.name}: 「{c.definition.trueName}」が風化で消滅し瓦礫1個に（完全除外）。");
                    }
                }
            }
            // 手札上限の超過処理：上限を超えた分はコストの低い順に遺構デッキの下へ戻す
            while (p.hand.Count > p.HandLimit)
            {
                var discard = p.hand.OrderBy(c => c.definition.cost).First();
                p.hand.Remove(discard);
                deck.ReturnToBottom(discard);
                Log($"{p.name}: 手札上限({p.HandLimit})を超えたため「{discard.definition.trueName}」を遺構へ戻した。");
            }
            p.handLimitModifierThisTurn = 0;

            // 昇華（記憶領域リワーク）：刻印3以上の守護者は自ターン終了時に記憶領域へ退場（盾＋勝利進捗）
            for (int i = p.board.Count - 1; i >= 0; i--)
            {
                var c = p.board[i];
                if (c.engravingCount >= 3)
                {
                    c.damageTaken = 0;                 // 残響を満タンに
                    c.turnAttackMod = 0; c.turnDefenseMod = 0;
                    c.summoningSick = false; c.attackedThisTurn = false; c.techniqueUsedThisTurn = false;
                    p.board.RemoveAt(i);
                    p.memoryZone.Add(c);
                    Log($"{p.name}: 「{c.definition.trueName}」が完全刻印に達し記憶領域へ昇華（残響{c.RemainingDefense}）。");
                }
            }
            CheckWinConditions();
            if (result != GameResult.Ongoing) { OnStateChanged?.Invoke(); return; }

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
            // 古き盟約勝利：完全刻印(刻印3)が記憶領域に3体
            for (int i = 0; i < 2; i++)
            {
                int full = players[i].memoryZone.Count(c => c.engravingCount >= 3);
                if (full >= 3) { result = i == 0 ? GameResult.Player0Win : GameResult.Player1Win; return; }
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
            // 1. 自分の場にいる守護者か
            if (!owner.board.Contains(guardian)) { reason = "自分の場の守護者ではない"; return false; }
            // 2. 自分の行動フェイズか
            if (game.phase != TurnPhase.Action) { reason = "行動フェイズではない"; return false; }
            // 3. 今ターン未発動か
            if (guardian.techniqueUsedThisTurn) { reason = "この守護者は今ターン技を使用済み"; return false; }
            // 4. ゲージ充足か（真名の灯篭で-1、最低1）
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
