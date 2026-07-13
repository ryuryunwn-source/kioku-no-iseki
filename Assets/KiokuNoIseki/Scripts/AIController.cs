using System.Linq;

namespace KiokuNoIseki
{
    // 17-3 の優先順位ヒューリスティックに従うAI。
    // 人間と同じ GameEngine の行動メソッドのみを呼ぶ（AI専用ルートは作らない）。
    public static class AIController
    {
        public static void TakeTurn(GameEngine g)
        {
            var self = g.Cur;
            var opp = g.Opp;

            // ── 生贄フェイズ ──
            int unaffordable = self.hand.Count(c => c.definition.cost > self.recallGauge);
            if (!self.inscribedThisTurn && unaffordable >= 2 && self.hand.Count > 1)
            {
                var sac = self.hand.OrderBy(c => c.definition.cost).First();
                g.Inscribe(sac);
            }

            // ── 行動フェイズ：可能な行動が尽きるまでループ ──
            int guard = 0;
            while (g.result == GameResult.Ongoing && guard++ < 100)
            {
                if (DoOneAction(g, self, opp)) continue;
                break;
            }

            // 6. 攻撃可能なモンスターを全て攻撃させる
            DoAttacks(g, self, opp);

            if (g.result == GameResult.Ongoing) g.EndTurn();
        }

        // 1手だけ実行し、実行できたら true
        static bool DoOneAction(GameEngine g, RecallerState self, RecallerState opp)
        {
            // 1. 攻撃でゲームを終わらせられるなら全力攻撃（攻撃処理は後段DoAttacksで実施するため、ここではスキップ判定のみ）
            int lethal = self.board.Where(c => !c.summoningSick && !c.attackedThisTurn).Sum(c => c.CurrentAttack);
            if (lethal >= opp.hp && opp.board.Count == 0)
                return false; // 攻撃フェイズに任せる

            // 2. 発動可能な技があれば発動
            foreach (var gd in self.board.Where(c => !c.techniqueUsedThisTurn))
            {
                int cost = gd.definition.incantationCost;
                if (self.HasNameLantern && cost > 1) cost -= 1;
                if (self.recallGauge < cost) continue;
                if (!TechniqueIsUseful(gd, self, opp)) continue;
                if (TechniqueActivator.TryActivate(g, gd, out _)) return true;
            }

            // 3. 今出せる最もコストの高いモンスターを出す
            var bestGuardian = self.hand
                .Where(c => c.definition.kind == CardKind.Guardian && c.definition.cost <= self.recallGauge)
                .OrderByDescending(c => c.definition.cost).FirstOrDefault();
            if (bestGuardian != null && self.board.Count < RecallerState.BoardLimit)
                if (g.PlayCard(bestGuardian)) return true;

            // 4. 除去魔法（断たれた絆）を相手の高攻撃に
            var destroy = self.hand.FirstOrDefault(c =>
                c.definition.spellEffect == EffectId.DestroyEnemyGuardian && c.definition.cost <= self.recallGauge);
            if (destroy != null && opp.board.Count > 0)
                if (g.PlayCard(destroy)) return true;

            // 5. その他の魔法・魔法石をコストの高い順に
            var spell = self.hand
                .Where(c => c.definition.kind != CardKind.Guardian && c.definition.cost <= self.recallGauge)
                .Where(c => SpellIsUseful(c, self, opp))
                .OrderByDescending(c => c.definition.cost).FirstOrDefault();
            if (spell != null)
                if (g.PlayCard(spell)) return true;

            return false;
        }

        static void DoAttacks(GameEngine g, RecallerState self, RecallerState opp)
        {
            var attackers = self.board.Where(c => !c.summoningSick && !c.attackedThisTurn && c.CurrentAttack > 0).ToList();
            foreach (var a in attackers)
            {
                if (g.result != GameResult.Ongoing) return;
                // 撃破できる相手モンスターを優先
                var killable = opp.board
                    .Where(t => t.RemainingDefense <= a.CurrentAttack)
                    .OrderByDescending(t => t.CurrentAttack).FirstOrDefault();
                if (killable != null) { g.Attack(a, killable); continue; }

                // 守護がいると本体に行けない → 守護を殴って削る
                var guardTarget = opp.board.FirstOrDefault(t => t.definition.guard && t.RemainingDefense > 0);
                if (guardTarget != null) g.Attack(a, guardTarget);
                else g.Attack(a, null); // 直接攻撃
            }
        }

        static bool TechniqueIsUseful(CardInstance gd, RecallerState self, RecallerState opp)
        {
            switch (gd.definition.techniqueEffect)
            {
                case EffectId.DamageLowestDefEnemyOrFace:
                case EffectId.GainGauge:
                case EffectId.DrainEnemyGauge:
                    return true;
                case EffectId.DamageAllEnemyGuardians:
                case EffectId.DebuffHighestAtkTurn:
                case EffectId.DebuffHighestAtkPerm:
                case EffectId.DebuffAllEnemyAtkTurn:
                case EffectId.DamageEnemyGuardian:
                case EffectId.DestroyEnemyGuardian:
                    return opp.board.Count > 0;
                case EffectId.RemoveSicknessAlly:
                    return self.board.Any(c => c != gd && c.summoningSick);
                case EffectId.HealHP:
                    return self.hp < 18;
                case EffectId.BuffLowestDefAllyPerm:
                case EffectId.BuffAllAllyAtkPerm:
                case EffectId.BuffAllAllyDefPerm:
                case EffectId.SelfAttackBuffTurn:
                case EffectId.SelfDefenseBuffTurn:
                case EffectId.SelfDefenseBuffPerm:
                    return self.board.Count > 0;
                default: return true;
            }
        }

        static bool SpellIsUseful(CardInstance c, RecallerState self, RecallerState opp)
        {
            switch (c.definition.spellEffect)
            {
                case EffectId.DamageEnemyGuardian:
                case EffectId.DestroyEnemyGuardian:
                case EffectId.DebuffHighestAtkPerm:
                    return opp.board.Count > 0;
                case EffectId.HealHP:
                    return self.hp < 17;
                case EffectId.EngraveAlly:
                case EffectId.BuffAllAllyAtkDefPerm:
                case EffectId.RemoveSicknessAlly:
                    return self.board.Count > 0;
                case EffectId.ReturnMemoryToDeck:
                    return self.memoryZone.Count > 0;
                case EffectId.ResetWeathering:
                    return self.hand.Any(h => h.weatheringCounter > 0);
                default: return true;
            }
        }
    }
}
