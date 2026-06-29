using System.Collections.Generic;

// v1: 13章の固定48枚カード定義（コード生成・ScriptableObject不使用）
namespace KiokuNoIseki
{
    // 13章の固定48枚（守護者30＋想起術14＋礎石4）をコードで定義する。
    public static class CardDatabase
    {
        static CardData G(string id, string name, Element el, int cost, int atk, int def,
            int incCost, string techName, EffectId eff, int mag, string text)
        {
            return new CardData
            {
                id = id, trueName = name, kind = CardKind.Guardian, element = el,
                cost = cost, attack = atk, defense = def,
                incantationCost = incCost, techniqueName = techName,
                techniqueEffect = eff, techniqueMagnitude = mag, effectText = text
            };
        }

        static CardData R(string id, string name, int cost, EffectId eff, int mag, string text)
        {
            return new CardData
            {
                id = id, trueName = name, kind = CardKind.Recollection,
                cost = cost, spellEffect = eff, spellMagnitude = mag, effectText = text
            };
        }

        static CardData C(string id, string name, int cost, string text)
        {
            return new CardData
            {
                id = id, trueName = name, kind = CardKind.Cornerstone,
                cost = cost, effectText = text
            };
        }

        // 13章の並び順どおり（guardian_001〜030 に対応）
        public static List<CardData> BuildDeckDefinitions()
        {
            var list = new List<CardData>();

            // ── 焔 ──
            list.Add(G("g001","焔の偵察兵",Element.Honoo,1,3,1,1,"先制の火花",EffectId.SelfAttackBuffTurn,1,"このターン中、自分自身の攻撃力+1する。"));
            list.Add(G("g002","灰塵の狩人",Element.Honoo,2,4,2,1,"灼熱の一撃",EffectId.DamageLowestDefEnemyOrFace,2,"相手の場で防御力が最も低い守護者に2ダメージ。いなければ相手に2ダメージ。"));
            list.Add(G("g003","焼け跡の番犬",Element.Honoo,2,3,3,1,"威嚇の咆哮",EffectId.DebuffHighestAtkTurn,1,"相手の場で攻撃力が最も高い守護者の攻撃力をこのターン中-1する。"));
            list.Add(G("g004","灼熱の剣士",Element.Honoo,3,6,2,1,"斬鉄火",EffectId.DamageLowestDefEnemyOrFace,3,"相手の場で防御力が最も低い守護者に3ダメージ。いなければ相手に3ダメージ。"));
            list.Add(G("g005","炎纏う巨兵",Element.Honoo,4,7,3,2,"業火轟",EffectId.DamageAllEnemyGuardians,2,"相手の場の守護者全体に2ダメージ。"));
            list.Add(G("g006","終焔の王",Element.Honoo,5,8,4,2,"滅尽の咆哮",EffectId.DamageAllEnemyGuardians,3,"相手の場の守護者全体に3ダメージ。"));

            // ── 森 ──
            list.Add(G("g007","苔むす守り手",Element.Mori,1,1,3,1,"根の盾",EffectId.SelfDefenseBuffTurn,2,"このターン中、自分自身の防御力+2する。"));
            list.Add(G("g008","若木の射手",Element.Mori,2,2,4,1,"若葉の守り",EffectId.BuffLowestDefAllyPerm,2,"自分の場で防御力が最も低い守護者の防御力+2する（永続）。"));
            list.Add(G("g009","蔓巻きの罠師",Element.Mori,2,3,3,1,"絡みつく蔓",EffectId.DebuffHighestAtkTurn,2,"相手の場で攻撃力が最も高い守護者の攻撃力をこのターン中-2する。"));
            list.Add(G("g010","古木の賢者",Element.Mori,3,3,5,1,"癒しの息吹",EffectId.HealHP,2,"自分のHPを2回復する。"));
            list.Add(G("g011","森林の巨人",Element.Mori,4,4,6,2,"大樹の抱擁",EffectId.BuffAllAllyDefPerm,1,"自分の場の守護者全体の防御力+1する（永続）。"));
            list.Add(G("g012","千年樹の化身",Element.Mori,5,5,7,2,"世界樹の祝福",EffectId.HealHP,4,"自分のHPを4回復する。"));

            // ── 流 ──
            list.Add(G("g013","水面の斥候",Element.Nagare,1,2,2,1,"流転の一歩",EffectId.GainGauge,1,"自分の想起ゲージを+1する（上限は超えない）。"));
            list.Add(G("g014","潮渡りの剣士",Element.Nagare,2,3,3,1,"流水の剣",EffectId.DamageLowestDefEnemyOrFace,1,"相手の場で防御力が最も低い守護者に1ダメージ。いなければ相手に1ダメージ。"));
            list.Add(G("g015","渦巻く影",Element.Nagare,2,2,4,1,"渦の罠",EffectId.DrainEnemyGauge,1,"相手の想起ゲージを-1する（最低0）。"));
            list.Add(G("g016","氷河の守護者",Element.Nagare,3,4,4,1,"氷結の縛り",EffectId.DebuffHighestAtkTurn,2,"相手の場で攻撃力が最も高い守護者の攻撃力をこのターン中-2する。"));
            list.Add(G("g017","深淵の漂流者",Element.Nagare,4,5,5,2,"深海の祝福",EffectId.GainGauge,2,"自分の想起ゲージを+2する（上限は超えない）。"));
            list.Add(G("g018","大海の支配者",Element.Nagare,5,6,6,2,"満ち潮の加護",EffectId.BuffAllAllyAtkPerm,1,"自分の場の守護者全体の攻撃力+1する（永続）。"));

            // ── 光 ──
            list.Add(G("g019","灯火の巫女",Element.Hikari,1,1,2,1,"小さな灯り",EffectId.HealHP,1,"自分のHPを1回復する。"));
            list.Add(G("g020","導きの旅人",Element.Hikari,2,2,3,1,"道しるべ",EffectId.RemoveSicknessAlly,0,"自分の場の守護者1体（自身を除く）を選び、召喚酔いを即座に解除する。"));
            list.Add(G("g021","聖なる番兵",Element.Hikari,2,3,4,1,"光の盾",EffectId.SelfDefenseBuffPerm,2,"自分自身の防御力+2する（永続）。"));
            list.Add(G("g022","黎明の祈祷師",Element.Hikari,3,2,6,1,"祈りの灯",EffectId.HealHP,3,"自分のHPを3回復する。"));
            list.Add(G("g023","神域の守護騎士",Element.Hikari,4,5,6,2,"聖域の加護",EffectId.BuffAllAllyDefPerm,1,"自分の場の守護者全体の防御力+1する（永続）。"));
            list.Add(G("g024","天啓の大司祭",Element.Hikari,5,5,8,2,"天啓",EffectId.HealHP,5,"自分のHPを5回復する。"));

            // ── 影 ──
            list.Add(G("g025","闇に潜む者",Element.Kage,1,3,1,1,"闇討ち",EffectId.DamageLowestDefEnemyOrFace,1,"相手の場で防御力が最も低い守護者に1ダメージ。いなければ相手に1ダメージ。"));
            list.Add(G("g026","呪縛の使い魔",Element.Kage,2,2,2,1,"呪縛",EffectId.DebuffHighestAtkPerm,1,"相手の場で攻撃力が最も高い守護者の攻撃力-1する（永続）。"));
            list.Add(G("g027","影渡りの刺客",Element.Kage,3,5,1,1,"暗殺の刃",EffectId.DamageLowestDefEnemyOrFace,3,"相手の場で防御力が最も低い守護者に3ダメージ。いなければ相手に3ダメージ。"));
            list.Add(G("g028","忘却の番人",Element.Kage,3,3,4,1,"忘却の手",EffectId.DrainEnemyGauge,2,"相手の想起ゲージを-2する（最低0）。"));
            list.Add(G("g029","終夜の伯爵",Element.Kage,4,6,3,2,"夜の帳",EffectId.DebuffAllEnemyAtkTurn,1,"相手の場の守護者全体の攻撃力をこのターン中-1する。"));
            list.Add(G("g030","深淵より来たる者",Element.Kage,5,7,4,2,"深淵の咆哮",EffectId.DamageAllEnemyGuardians,2,"相手の場の守護者全体に2ダメージ。"));

            // ── 想起術（14種） ──
            list.Add(R("r01","微かな残響",1,EffectId.GainGauge,1,"自分の想起ゲージを+1する（上限は超えない）。"));
            list.Add(R("r02","掘削の儀",2,EffectId.ExtraExcavate,1,"遺構デッキから追加で1枚発掘する。ただしそのターンの終了フェイズで手札上限を1減らす。"));
            list.Add(R("r03","忘却の代償",2,EffectId.SacrificeForBurn,3,"自分の手札を1枚（最もコストが低い）代償に捧げる。相手の防御力が最も低い守護者に3ダメージ。いなければ相手に3ダメージ。"));
            list.Add(R("r04","古き盟約の欠片",3,EffectId.EngraveAlly,1,"自分の場の守護者1体を選ぶ。その守護者に刻印を1つ刻む。"));
            list.Add(R("r05","静かな目覚め",2,EffectId.RemoveSicknessAlly,0,"自分の場の守護者1体の召喚酔いを即座に解除する。"));
            list.Add(R("r06","砕けし記憶",1,EffectId.DamageEnemyGuardian,1,"相手の守護者1体に1ダメージを与える。"));
            list.Add(R("r07","遺構の囁き",2,EffectId.ScryReorder,3,"遺構デッキの上から3枚を見て、好きな順番に並べ替えて戻す。"));
            list.Add(R("r08","想いの澱み",1,EffectId.DrainEnemyGauge,1,"相手の想起ゲージを-1する（最低0）。"));
            list.Add(R("r09","断たれた絆",3,EffectId.DestroyEnemyGuardian,0,"相手の守護者1体を破壊する（転生処理が発生し、刻印が1つ刻まれる）。"));
            list.Add(R("r10","祈りの灯",2,EffectId.HealHP,3,"自分のHPを3回復する。"));
            list.Add(R("r11","逆引きの理",2,EffectId.ResetWeathering,0,"自分の手札のカード1枚の風化カウンターを0にリセットする。"));
            list.Add(R("r12","導きの残光",1,EffectId.DigSelectNext,3,"次に自分が発掘する際、遺構デッキの上から3枚を見て1枚を選んで手札に加える。"));
            list.Add(R("r13","無音の盟約",4,EffectId.BuffAllAllyAtkDefPerm,1,"自分の場の守護者全体の攻撃力と防御力を+1する（永続）。"));
            list.Add(R("r14","再誦の記憶",2,EffectId.DrawCard,1,"遺構デッキから1枚発掘する（追加ドロー）。"));

            // ── 礎石（4種） ──
            list.Add(C("cs_altar","記憶の祭壇",2,"刻む際の想起ゲージ上限の上昇量が+1される。"));
            list.Add(C("cs_cathedral","忘れられし聖堂",3,"手札の風化が増え始めるまでの猶予が1ターン延びる。"));
            list.Add(C("cs_lantern","真名の灯篭",2,"自分の守護者の技の詠唱コストが1下がる（最低1）。"));
            list.Add(C("cs_fort","瓦礫の砦",3,"自分の守護者が破壊されるたび、砕けた瓦礫が相手に1ダメージを与える。"));

            // 守護キーワード（6体）：場にいる間、相手は本体を直接攻撃できない
            var guardIds = new HashSet<string> { "g007", "g021", "g016", "g011", "g023", "g024" };
            foreach (var c in list) if (guardIds.Contains(c.id)) c.guard = true;

            return list; // 計48枚
        }
    }
}
