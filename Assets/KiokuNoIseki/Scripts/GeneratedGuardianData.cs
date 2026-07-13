using System;
using System.Collections.Generic;

namespace KiokuNoIseki
{
    // 効果ID＋効果量から、固定カードと同じ文体の日本語効果説明を生成する。
    // マイモンは効果テキストを自動生成するため、これを使って「何ダメージ／何回復」まで明記する。
    public static class EffectText
    {
        public static string Describe(EffectId eff, int mag)
        {
            switch (eff)
            {
                case EffectId.DamageLowestDefEnemyOrFace:
                    return $"相手の場で防御力が最も低いモンスターに{mag}ダメージ。いなければ相手に{mag}ダメージ。";
                case EffectId.DamageAllEnemyGuardians:
                    return $"相手の場のモンスター全体に{mag}ダメージ。";
                case EffectId.DamageEnemyGuardian:
                    return $"相手のモンスター1体に{mag}ダメージ。";
                case EffectId.SelfDefenseBuffPerm:
                    return $"自分自身の防御力+{mag}する（永続）。";
                case EffectId.SelfDefenseBuffTurn:
                    return $"このターン中、自分自身の防御力+{mag}する。";
                case EffectId.SelfAttackBuffPerm:
                    return $"自分自身の攻撃力+{mag}する（永続）。";
                case EffectId.SelfAttackBuffTurn:
                    return $"このターン中、自分自身の攻撃力+{mag}する。";
                case EffectId.HealHP:
                    return $"自分のHPを{mag}回復する。";
                case EffectId.GainGauge:
                    return $"自分のゲージを+{mag}する（上限は超えない）。";
                case EffectId.DrainEnemyGauge:
                    return $"相手のゲージを-{mag}する（最低0）。";
                case EffectId.BuffAllAllyDefPerm:
                    return $"自分の場のモンスター全体の防御力+{mag}する（永続）。";
                case EffectId.BuffAllAllyAtkPerm:
                    return $"自分の場のモンスター全体の攻撃力+{mag}する（永続）。";
                case EffectId.DebuffHighestAtkPerm:
                    return $"相手の場で攻撃力が最も高いモンスターの攻撃力-{mag}する（永続）。";
                case EffectId.DebuffHighestAtkTurn:
                    return $"相手の場で攻撃力が最も高いモンスターの攻撃力をこのターン中-{mag}する。";
                default:
                    return "（特殊効果）";
            }
        }
    }

    // 【v2】マイモン：写真から動的生成されるモンスターデータ（19章 GeneratedGuardianData）。
    // 固定カード(CardData)とは別に、CardInstance.generated に保持する。
    // 強さ（系統・コスト・攻防・技の効果）はすべて決定論的アルゴリズムで決める（15章）。
    // 名前・技名だけを AI（またはオフライン候補リスト）で与える。同じ写真は常に同じカードになる。
    public class GeneratedGuardianData
    {
        public ulong sourceImageHash;
        public string trueName;        // 名前（AI生成 or フォールバック）
        public string techniqueName;   // 技のフレーバー名
        public Element element;        // アルゴリズムのみで決定（15-2）
        public int cost;               // アルゴリズムのみで決定（15-3）
        public int attack;
        public int defense;
        public EffectId techniqueEffect;   // 効果内容はアルゴリズムのみで決定（15-4）
        public int techniqueMagnitude;
        public int incantationCost;

        // このデータから、既存エンジンがそのまま扱える合成 CardData（モンスター）を作る。
        // これによりマイモンは固定モンスターと完全に同じルール（劣化・転生・刻印・守護）で動く。
        public CardData ToCardData()
        {
            return new CardData
            {
                id = "gen_" + sourceImageHash.ToString("x8"),
                trueName = string.IsNullOrEmpty(trueName) ? "マイモン" : trueName,
                kind = CardKind.Guardian,
                element = element,
                cost = cost,
                attack = attack,
                defense = defense,
                guard = false,
                effectText = EffectText.Describe(techniqueEffect, techniqueMagnitude),
                incantationCost = incantationCost,
                techniqueName = techniqueName,
                techniqueEffect = techniqueEffect,
                techniqueMagnitude = techniqueMagnitude,
            };
        }
    }

    // 写真の特徴（ハッシュ・平均色HSV）からマイモンのステータスを決定論的に生成する（15-2〜15-4）。
    // 純粋計算のみ（UnityEngine非依存）。画像→ハッシュ/HSVの抽出は PhotoWriteshi 側で行う。
    public static class CardGenerator
    {
        // 系統ごとの技テンプレート（すべて対象選択不要の自己解決型・15-4）。
        // 効果量は予算ティア(0=低/1=中/2=高)でスケールする。
        struct Tmpl { public EffectId eff; public int[] mag; public Tmpl(EffectId e, int a, int b, int c) { eff = e; mag = new[] { a, b, c }; } }

        static readonly Dictionary<Element, Tmpl[]> Templates = new Dictionary<Element, Tmpl[]>
        {
            { Element.Honoo, new[] {
                new Tmpl(EffectId.DamageLowestDefEnemyOrFace, 2, 3, 4),
                new Tmpl(EffectId.DamageAllEnemyGuardians,    1, 2, 3),
            } },
            { Element.Mori, new[] {
                new Tmpl(EffectId.SelfDefenseBuffPerm, 1, 2, 3),
                new Tmpl(EffectId.HealHP,              2, 3, 4),
            } },
            { Element.Nagare, new[] {
                new Tmpl(EffectId.GainGauge,       1, 1, 2),
                new Tmpl(EffectId.DrainEnemyGauge, 1, 2, 2),
            } },
            { Element.Hikari, new[] {
                new Tmpl(EffectId.HealHP,             2, 3, 5),
                new Tmpl(EffectId.BuffAllAllyDefPerm, 1, 1, 1),
            } },
            { Element.Kage, new[] {
                new Tmpl(EffectId.DebuffHighestAtkPerm,    1, 1, 2),
                new Tmpl(EffectId.DamageAllEnemyGuardians, 1, 2, 2),
            } },
        };

        // hue01: 0..1 の色相, sat: 0..1 の彩度, val: 0..1 の明度。
        public static GeneratedGuardianData Generate(ulong hash, float hue01, float sat, float val,
            string trueName, string techniqueName)
        {
            var g = new GeneratedGuardianData { sourceImageHash = hash };

            // ── 系統判定（15-2）──
            if (sat < 0.18f)
                g.element = val >= 0.5f ? Element.Hikari : Element.Kage;
            else
            {
                float hue = hue01 * 360f;
                if (hue < 50f || hue >= 330f) g.element = Element.Honoo;      // 赤〜橙 / 赤紫寄りの赤
                else if (hue < 160f) g.element = Element.Mori;                 // 黄緑〜緑
                else if (hue < 260f) g.element = Element.Nagare;               // 青緑〜青
                else g.element = Element.Kage;                                 // 紫〜赤紫
            }

            // ── 予算ティア（15-3）──
            int tier = (int)(hash % 3);                 // 0=低 / 1=中 / 2=高
            g.cost = 2 + tier;                          // 2/3/4
            int total = 6 + tier * 2;                   // 攻防合計 6/8/10

            // ── 攻防配分（明るい写真ほど攻撃寄り）──
            float ratio = Math.Max(0.3f, Math.Min(0.7f, val));
            g.attack = (int)Math.Round(total * ratio);
            g.defense = total - g.attack;
            if (g.attack < 1) { g.attack = 1; g.defense = total - 1; }
            if (g.defense < 0) g.defense = 0;

            // ── 技テンプレート選択（15-4）──
            var tmpls = Templates[g.element];
            var t = tmpls[(int)((hash >> 8) % (ulong)tmpls.Length)];
            g.techniqueEffect = t.eff;
            g.techniqueMagnitude = t.mag[tier];
            g.incantationCost = g.cost <= 3 ? 1 : 2;

            // ── 名前（未指定ならオフライン候補から決定論的に選ぶ・15-6フォールバック）──
            g.trueName = string.IsNullOrEmpty(trueName) ? FallbackName(hash) : trueName;
            g.techniqueName = string.IsNullOrEmpty(techniqueName) ? FallbackTechName(g.element, hash) : techniqueName;
            return g;
        }

        // オフライン時の名前候補（カタカナ・発音しやすい）。同じ写真は常に同じ名前になる。
        static readonly string[] NameCandidates =
        {
            "アルガ","ヴェント","ゼノ","リグル","ノクス","カイム","セラ","ドラウグ","フィルア","ガロン",
            "メルヴ","オルジャ","ティグレ","ネビュラ","ザルド","ルミナ","ヴァルグ","エクリ","ソルド","ミラージュ",
            "ブレイズ","クロウ","フェンリ","グリムル","サイファ","レガル","ヨルム","ペイル","アズラ","ヴィント",
        };

        static readonly Dictionary<Element, string[]> TechNames = new Dictionary<Element, string[]>
        {
            { Element.Honoo, new[] { "灼熱の牙", "紅蓮撃", "焦土の咆哮" } },
            { Element.Mori,  new[] { "大樹の護り", "芽吹きの息吹", "根絡みの守り" } },
            { Element.Nagare,new[] { "潮流の導き", "渦潮の理", "静水の恵み" } },
            { Element.Hikari,new[] { "聖光の祈り", "暁の加護", "祝福の灯" } },
            { Element.Kage,  new[] { "夜陰の呪詛", "影喰らい", "忘却の手" } },
        };

        public static string FallbackName(ulong hash) => NameCandidates[(int)(hash % (ulong)NameCandidates.Length)];
        static string FallbackTechName(Element e, ulong hash)
        {
            var arr = TechNames[e];
            return arr[(int)((hash >> 16) % (ulong)arr.Length)];
        }
    }
}
