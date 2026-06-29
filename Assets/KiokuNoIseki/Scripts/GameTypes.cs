using System.Collections.Generic;

namespace KiokuNoIseki
{
    // 系統（焔/森/流/光/影）
    public enum Element { Honoo, Mori, Nagare, Hikari, Kage }

    public enum CardKind { Guardian, Recollection, Cornerstone }

    // ターンフェイズ（6章）
    public enum TurnPhase { Decay, Excavate, Inscribe, Action, End }

    // 効果アーキタイプ。技・想起術の効果はすべてこのIDで表現する（15-4のデフォルト対象を採用）
    public enum EffectId
    {
        None,
        SelfAttackBuffTurn,         // 自身の攻撃力を今ターン+mag
        SelfDefenseBuffTurn,        // 自身の防御力を今ターン+mag
        SelfAttackBuffPerm,         // 自身の攻撃力を永続+mag
        SelfDefenseBuffPerm,        // 自身の防御力を永続+mag
        DamageLowestDefEnemyOrFace, // 相手の防御力最小の守護者にmag。いなければ相手本体にmag
        DamageAllEnemyGuardians,    // 相手の場の守護者全体にmag
        DebuffHighestAtkTurn,       // 相手の攻撃力最大の守護者を今ターン-mag
        DebuffHighestAtkPerm,       // 相手の攻撃力最大の守護者を永続-mag
        DebuffAllEnemyAtkTurn,      // 相手の場全体の攻撃力を今ターン-mag
        BuffLowestDefAllyPerm,      // 自分の防御力最小の守護者の防御力を永続+mag
        BuffAllAllyDefPerm,         // 自分の場全体の防御力を永続+mag
        BuffAllAllyAtkPerm,         // 自分の場全体の攻撃力を永続+mag
        BuffAllAllyAtkDefPerm,      // 自分の場全体の攻撃力・防御力を永続+mag
        HealHP,                     // 自分のHPをmag回復
        GainGauge,                  // 自分の想起ゲージを+mag（上限超えない）
        DrainEnemyGauge,            // 相手の想起ゲージを-mag（最低0）
        RemoveSicknessAlly,         // 自分の守護者1体の召喚酔いを解除（自動対象=最も攻撃力が高い召喚酔い）
        DamageEnemyGuardian,        // 相手の守護者1体にmag（自動対象=攻撃力最大）
        DestroyEnemyGuardian,       // 相手の守護者1体を破壊（自動対象=攻撃力最大、転生処理）
        EngraveAlly,                // 自分の守護者1体に刻印+1（自動対象=攻撃力最大）
        ExtraExcavate,             // 追加1枚発掘（このターン手札上限-1）
        SacrificeForRubble,         // (廃止予定・未使用) 旧:手札1枚を捧げ瓦礫mag個
        SacrificeForBurn,           // 手札1枚(最安)を代償に捧げ、相手の防御最小の守護者か本体にmagダメージ
        DrawCard,                   // 遺構デッキからmag枚発掘する
        ScryReorder,                // 遺構デッキ上mag枚を見て並べ替え（AIは現状維持・人間も自動）
        DigSelectNext,              // 次の発掘で上3枚から1枚選択（自動=最もコストが高いカード）
        ResetWeathering,            // 手札1枚の風化を0に（自動=最も風化が進んだ手札）
        ReturnMemoryToDeck,         // 記憶領域のカード1枚を遺構へ戻す（自動=任意1枚）
    }

    // カード静的データ（ScriptableObjectの代わりにコード定義。19章CardDefinition相当）
    public class CardData
    {
        public string id;
        public string trueName;     // 真名
        public CardKind kind;
        public Element element;
        public int cost;
        public int attack;
        public int defense;
        public bool guard;          // 守護：場にいる間、相手は本体(HP)を直接攻撃できない
        public string effectText;

        // 守護者の技
        public int incantationCost;
        public string techniqueName;
        public EffectId techniqueEffect;
        public int techniqueMagnitude;

        // 想起術・礎石の効果
        public EffectId spellEffect;
        public int spellMagnitude;

        public CardData Clone()
        {
            return (CardData)MemberwiseClone();
        }
    }

    // カードインスタンス（19章）。転生で状態が引き継がれる
    public class CardInstance
    {
        public CardData definition;
        public int weatheringCounter;
        public int engravingCount;
        public int turnsInHand;
        public bool techniqueUsedThisTurn;
        public bool summoningSick;          // 召喚酔い
        public bool attackedThisTurn;

        // 永続強化（刻印ボーナスとは別に技で付与された分）
        public int permAttackBuff;
        public int permDefenseBuff;
        // 今ターン限定の増減
        public int turnAttackMod;
        public int turnDefenseMod;
        // ダメージ蓄積（防御力との比較で破壊判定）
        public int damageTaken;

        public readonly int instanceId;
        static int s_next = 1;
        public CardInstance(CardData def) { definition = def; instanceId = s_next++; }

        public bool IsGuardian => definition.kind == CardKind.Guardian;

        // 刻印1つにつき攻撃力+1/防御力+1（10章）
        public int CurrentAttack =>
            System.Math.Max(0, definition.attack + engravingCount + permAttackBuff + turnAttackMod);
        public int CurrentDefense =>
            System.Math.Max(0, definition.defense + engravingCount + permDefenseBuff + turnDefenseMod);

        public int RemainingDefense => CurrentDefense - damageTaken;

        public void ResetForNewTurn()
        {
            techniqueUsedThisTurn = false;
            attackedThisTurn = false;
            turnAttackMod = 0;
            turnDefenseMod = 0;
        }
    }

    // プレイヤー状態（19章 RecallerState）
    public class RecallerState
    {
        public string name = "想起者";
        public bool isAI;
        public int hp = 20;
        public int recallGauge = 2;
        public int recallGaugeMax = 2;
        public List<CardInstance> hand = new List<CardInstance>();
        public List<CardInstance> board = new List<CardInstance>();        // 守護者ゾーン×5
        public List<CardInstance> cornerstones = new List<CardInstance>(); // 礎石ゾーン×3
        public List<CardInstance> memoryZone = new List<CardInstance>();   // 記憶領域
        public int rubbleTokens;

        // ターン状態フラグ
        public bool inscribedThisTurn;      // このターン刻んだ→次の減衰スキップ
        public bool skipNextDecay;          // 直前ターンに刻んだ→今ターンの減衰スキップ
        public int handLimitModifierThisTurn; // 掘削の儀などによる一時的な手札上限変動
        public bool digSelectPending;       // 導きの残光：次の発掘で選択
        public int extraExcavatePending;    // 追加発掘予約数

        public const int BoardLimit = 5;
        public const int CornerstoneLimit = 3;
        public int HandLimit => 7 + handLimitModifierThisTurn + (HasCornerstone(EffectId.None) ? 0 : 0);

        // 礎石パッシブ判定
        public bool HasShrineAltar;     // 記憶の祭壇：刻む上昇量+1
        public bool HasSanctuary;       // 忘れられし聖堂：風化猶予+1ターン
        public bool HasNameLantern;     // 真名の灯篭：詠唱コスト-1
        public bool HasFortThorn;       // 瓦礫の砦：自分の守護者が破壊されるたび相手に1ダメージ

        bool HasCornerstone(EffectId e) => false;

        public void RecountCornerstonePassives()
        {
            HasShrineAltar = HasSanctuary = HasNameLantern = HasFortThorn = false;
            foreach (var c in cornerstones)
            {
                switch (c.definition.id)
                {
                    case "cs_altar": HasShrineAltar = true; break;
                    case "cs_cathedral": HasSanctuary = true; break;
                    case "cs_lantern": HasNameLantern = true; break;
                    case "cs_fort": HasFortThorn = true; break;
                }
            }
        }

        public void AddRubble(int amount)
        {
            if (amount <= 0) return;
            rubbleTokens += amount;
        }
    }
}
