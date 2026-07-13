using System;
using System.Linq;

namespace KiokuNoIseki
{
    // 技・魔法の効果を解決する。対象は15-4の「デフォルト対象」を自動採用する
    // （v1ではタップで自己解決させ、操作を単純化する）。
    public static class EffectResolver
    {
        static void Log(GameEngine g, string s) => g.OnLog?.Invoke("  " + s);

        // chosenTarget: 魔法などでプレイヤーが手動指定した対象（nullなら15-4のデフォルト対象を自動採用）
        public static void Resolve(GameEngine g, RecallerState self, RecallerState opp,
            CardInstance source, EffectId eff, int mag, CardInstance chosenTarget = null)
        {
            switch (eff)
            {
                case EffectId.None: break;

                case EffectId.SelfAttackBuffTurn:
                    if (source != null) { source.turnAttackMod += mag; Log(g, $"{source.definition.trueName} 攻撃力+{mag}(今ターン)。"); }
                    break;
                case EffectId.SelfDefenseBuffTurn:
                    if (source != null) { source.turnDefenseMod += mag; Log(g, $"{source.definition.trueName} 防御力+{mag}(今ターン)。"); }
                    break;
                case EffectId.SelfAttackBuffPerm:
                    if (source != null) { source.permAttackBuff += mag; Log(g, $"{source.definition.trueName} 攻撃力+{mag}(永続)。"); }
                    break;
                case EffectId.SelfDefenseBuffPerm:
                    if (source != null) { source.permDefenseBuff += mag; Log(g, $"{source.definition.trueName} 防御力+{mag}(永続)。"); }
                    break;

                case EffectId.DamageLowestDefEnemyOrFace:
                {
                    var t = opp.board.OrderBy(c => c.RemainingDefense).FirstOrDefault();
                    if (t != null)
                    {
                        t.damageTaken += mag;
                        Log(g, $"{t.definition.trueName} に{mag}ダメージ。");
                        g.ResolveDestruction(opp, t, self);
                    }
                    else { opp.hp -= mag; Log(g, $"相手に{mag}ダメージ。"); }
                    break;
                }

                case EffectId.DamageAllEnemyGuardians:
                {
                    foreach (var t in opp.board.ToList())
                    {
                        t.damageTaken += mag;
                        g.ResolveDestruction(opp, t, self);
                    }
                    Log(g, $"相手のモンスター全体に{mag}ダメージ。");
                    break;
                }

                case EffectId.DebuffHighestAtkTurn:
                {
                    var t = opp.board.OrderByDescending(c => c.CurrentAttack).FirstOrDefault();
                    if (t != null) { t.turnAttackMod -= mag; Log(g, $"{t.definition.trueName} 攻撃力-{mag}(今ターン)。"); }
                    break;
                }
                case EffectId.DebuffHighestAtkPerm:
                {
                    var t = opp.board.OrderByDescending(c => c.CurrentAttack).FirstOrDefault();
                    if (t != null) { t.permAttackBuff -= mag; Log(g, $"{t.definition.trueName} 攻撃力-{mag}(永続)。"); }
                    break;
                }
                case EffectId.DebuffAllEnemyAtkTurn:
                    foreach (var t in opp.board) t.turnAttackMod -= mag;
                    Log(g, $"相手の場全体の攻撃力-{mag}(今ターン)。");
                    break;

                case EffectId.BuffLowestDefAllyPerm:
                {
                    var t = self.board.OrderBy(c => c.CurrentDefense).FirstOrDefault();
                    if (t != null) { t.permDefenseBuff += mag; Log(g, $"{t.definition.trueName} 防御力+{mag}(永続)。"); }
                    break;
                }
                case EffectId.BuffAllAllyDefPerm:
                    foreach (var t in self.board) t.permDefenseBuff += mag;
                    Log(g, $"自分の場全体の防御力+{mag}(永続)。");
                    break;
                case EffectId.BuffAllAllyAtkPerm:
                    foreach (var t in self.board) t.permAttackBuff += mag;
                    Log(g, $"自分の場全体の攻撃力+{mag}(永続)。");
                    break;
                case EffectId.BuffAllAllyAtkDefPerm:
                    foreach (var t in self.board) { t.permAttackBuff += mag; t.permDefenseBuff += mag; }
                    Log(g, $"自分の場全体の攻撃力・防御力+{mag}(永続)。");
                    break;

                case EffectId.HealHP:
                    self.hp += mag; Log(g, $"HPを{mag}回復。");
                    break;
                case EffectId.GainGauge:
                    self.recallGauge = Math.Min(self.recallGaugeMax, self.recallGauge + mag);
                    Log(g, $"ゲージ+{mag}。");
                    break;
                case EffectId.DrainEnemyGauge:
                    opp.recallGauge = Math.Max(0, opp.recallGauge - mag);
                    Log(g, $"相手のゲージ-{mag}。");
                    break;

                case EffectId.RemoveSicknessAlly:
                {
                    var t = (chosenTarget != null && self.board.Contains(chosenTarget) && chosenTarget != source)
                          ? chosenTarget
                          : self.board.Where(c => c != source && c.summoningSick)
                                      .OrderByDescending(c => c.CurrentAttack).FirstOrDefault();
                    if (t != null) { t.summoningSick = false; Log(g, $"{t.definition.trueName} の召喚酔いを解除。"); }
                    break;
                }
                case EffectId.DamageEnemyGuardian:
                {
                    var t = (chosenTarget != null && opp.board.Contains(chosenTarget))
                          ? chosenTarget : opp.board.OrderByDescending(c => c.CurrentAttack).FirstOrDefault();
                    if (t != null)
                    {
                        t.damageTaken += mag;
                        Log(g, $"{t.definition.trueName} に{mag}ダメージ。");
                        g.ResolveDestruction(opp, t, self);
                    }
                    break;
                }
                case EffectId.DestroyEnemyGuardian:
                {
                    var t = (chosenTarget != null && opp.board.Contains(chosenTarget))
                          ? chosenTarget : opp.board.OrderByDescending(c => c.CurrentAttack).FirstOrDefault();
                    if (t != null)
                    {
                        opp.board.Remove(t);
                        g.DoReincarnate(t, self);
                        Log(g, $"{t.definition.trueName} を破壊。");
                    }
                    break;
                }
                case EffectId.EngraveAlly:
                {
                    var t = (chosenTarget != null && self.board.Contains(chosenTarget))
                          ? chosenTarget : self.board.OrderByDescending(c => c.CurrentAttack).FirstOrDefault();
                    if (t != null) { t.engravingCount += mag; Log(g, $"{t.definition.trueName} に刻印+{mag}。"); }
                    break;
                }

                case EffectId.ExtraExcavate:
                    self.extraExcavatePending += 1;
                    self.handLimitModifierThisTurn -= 1;
                    // 即時に追加ドロー（行動フェイズ中なので手札に直接加える）
                    {
                        var c = g.deck.DrawTop();
                        if (c != null) { c.turnsInHand = 0; self.hand.Add(c); self.extraExcavatePending -= 1; Log(g, "追加で1枚ドロー。"); }
                    }
                    break;

                case EffectId.SacrificeForRubble:
                    break; // 廃止（未使用）

                case EffectId.SacrificeForBurn:
                {
                    var sac = self.hand.OrderBy(c => c.definition.cost).FirstOrDefault();
                    if (sac != null) { self.hand.Remove(sac); Log(g, $"「{sac.definition.trueName}」を代償に捧げた（除外）。"); }
                    var t = opp.board.OrderBy(c => c.RemainingDefense).FirstOrDefault();
                    if (t != null) { t.damageTaken += mag; Log(g, $"{t.definition.trueName} に{mag}ダメージ。"); g.ResolveDestruction(opp, t, self); }
                    else { opp.hp -= mag; Log(g, $"相手に{mag}ダメージ。"); }
                    break;
                }

                case EffectId.DrawCard:
                {
                    int drawn = 0;
                    for (int i = 0; i < mag; i++)
                    {
                        var c = g.deck.DrawTop();
                        if (c == null) break;
                        c.turnsInHand = 0; self.hand.Add(c); drawn++;
                    }
                    Log(g, $"デッキから{drawn}枚ドロー。");
                    break;
                }
                case EffectId.ScryReorder:
                    // 上mag枚を攻撃力/コストが高い順に並べ替え（自動最適化）
                    {
                        int n = Math.Min(mag, g.deck.Count);
                        var top = g.deck.cards.Take(n).OrderByDescending(c => c.definition.cost).ToList();
                        for (int i = 0; i < n; i++) g.deck.cards[i] = top[i];
                        Log(g, "デッキ上を並べ替えた。");
                    }
                    break;
                case EffectId.DigSelectNext:
                    self.digSelectPending = true;
                    Log(g, "次のドローで上3枚から選べる。");
                    break;
                case EffectId.ResetWeathering:
                {
                    var t = self.hand.OrderByDescending(c => c.weatheringCounter * 10 + c.turnsInHand).FirstOrDefault();
                    if (t != null) { t.weatheringCounter = 0; t.turnsInHand = 0; Log(g, $"「{t.definition.trueName}」の劣化をリセット。"); }
                    break;
                }
                case EffectId.ReturnMemoryToDeck:
                {
                    var t = self.memoryZone.FirstOrDefault();
                    if (t != null) { self.memoryZone.Remove(t); g.deck.ReturnToRandomPosition(t); Log(g, $"殿堂の「{t.definition.trueName}」をデッキへ戻した。"); }
                    break;
                }
            }
        }
    }
}
