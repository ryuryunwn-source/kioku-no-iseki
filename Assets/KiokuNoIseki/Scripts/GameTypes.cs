using System.Collections.Generic;

namespace KiokuNoIseki
{
    // 系統（焔/森/流/光/影）
    public enum Element { Honoo, Mori, Nagare, Hikari, Kage }

    public enum CardKind { Guardian, Recollection, Cornerstone }

    // ターンフェイズ（6章）
    public enum TurnPhase { Decay, Excavate, Inscribe, Action, End }

    // 効果アーキタイプ。技・呪文の効果はすべてこのIDで表現する（15-4のデフォルト対象を採用）
    public enum EffectId
    {
        None,
        SelfAttackBuffTurn,         // 自身の攻撃力を今ターン+mag
        SelfDefenseBuffTurn,        // 自身の防御力を今ターン+mag
        SelfAttackBuffPerm,         // 自身の攻撃力を永続+mag
        SelfDefenseBuffPerm,        // 自身の防御力を永続+mag
        DamageLowestDefEnemyOrFace, // 相手の防御力最小のユニットにmag。いなければ相手本体にmag
        DamageAllEnemyGuardians,    // 相手の場のユニット全体にmag
        DebuffHighestAtkTurn,       // 相手の攻撃力最大のユニットを今ターン-mag
        DebuffHighestAtkPerm,       // 相手の攻撃力最大のユニットを永続-mag
        DebuffAllEnemyAtkTurn,      // 相手の場全体の攻撃力を今ターン-mag
        BuffLowestDefAllyPerm,      // 自分の防御力最小のユニットの防御力を永続+mag
        BuffAllAllyDefPerm,         // 自分の場全体の防御力を永続+mag
        BuffAllAllyAtkPerm,         // 自分の場全体の攻撃力を永続+mag
        BuffAllAllyAtkDefPerm,      // 自分の場全体の攻撃力・防御力を永続+mag
        HealHP,                     // 自分のHPをmag回復
        GainGauge,                  // 自分のマナを+mag（上限超えない）
        DrainEnemyGauge,            // 相手のマナを-mag（最低0）
        RemoveSicknessAlly,         // 自分のユニット1体の召喚酔いを解除（自動対象=最も攻撃力が高い召喚酔い）
        DamageEnemyGuardian,        // 相手のユニット1体にmag（自動対象=攻撃力最大）
        DestroyEnemyGuardian,       // 相手のユニット1体を破壊（自動対象=攻撃力最大、復帰処理）
        EngraveAlly,                // 自分のユニット1体に強化+1（自動対象=攻撃力最大）
        ExtraExcavate,             // 追加1枚ドロー（このターン手札上限-1）
        SacrificeForRubble,         // (廃止予定・未使用) 旧:手札1枚を捧げ瓦礫mag個
        SacrificeForBurn,           // 手札1枚(最安)を代償に捧げ、相手の防御最小のユニットか本体にmagダメージ
        DrawCard,                   // 山札からmag枚ドローする
        ScryReorder,                // 山札上mag枚を見て並べ替え（AIは現状維持・人間も自動）
        DigSelectNext,              // 次のドローで上3枚から1枚選択（自動=最もコストが高いカード）
        ResetWeathering,            // 手札1枚の劣化を0に（自動=最も劣化が進んだ手札）
        ReturnMemoryToDeck,         // 勝利ゾーンのカード1枚を山札へ戻す（自動=任意1枚）
    }

    // カード静的データ（ScriptableObjectの代わりにコード定義。19章CardDefinition相当）
    public class CardData
    {
        public string id;
        public string trueName;     // 名前
        public CardKind kind;
        public Element element;
        public int cost;
        public int attack;
        public int defense;
        public bool guard;          // 守護：場にいる間、相手は本体(HP)を直接攻撃できない
        public string effectText;

        // ユニットの技
        public int incantationCost;
        public string techniqueName;
        public EffectId techniqueEffect;
        public int techniqueMagnitude;

        // 呪文・設置カードの効果
        public EffectId spellEffect;
        public int spellMagnitude;

        public CardData Clone()
        {
            return (CardData)MemberwiseClone();
        }
    }

    // カードインスタンス（19章）。復帰で状態が引き継がれる
    public class CardInstance
    {
        public CardData definition;
        // 【v2】写し身の場合に元データを保持（19章）。固定カードでは常にnull。
        // definition は generated.ToCardData() の合成データを指すため、エンジンは両者を区別せず扱える。
        public GeneratedGuardianData generated;
        public int weatheringCounter;
        public int engravingCount;
        public int turnsInHand;
        public bool techniqueUsedThisTurn;
        public bool summoningSick;          // 召喚酔い
        public bool attackedThisTurn;

        // 永続強化（強化ボーナスとは別に技で付与された分）
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
        // 写し身インスタンス：合成CardDataをdefinitionに、元データをgeneratedに保持
        public CardInstance(GeneratedGuardianData gen) : this(gen.ToCardData()) { generated = gen; }

        public bool IsGuardian => definition.kind == CardKind.Guardian;

        // 強化1つにつき攻撃力+1/防御力+1（10章）
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
        public string name = "プレイヤー";
        public bool isAI;
        public int hp = 20;
        public int recallGauge = 2;
        public int recallGaugeMax = 2;
        public List<CardInstance> hand = new List<CardInstance>();
        public List<CardInstance> board = new List<CardInstance>();        // ユニットゾーン×5
        public List<CardInstance> cornerstones = new List<CardInstance>(); // 設置カードゾーン×3
        public List<CardInstance> memoryZone = new List<CardInstance>();   // 勝利ゾーン
        public int rubbleTokens;

        // ターン状態フラグ
        public bool inscribedThisTurn;      // このターンマナ加速した→次の減衰スキップ
        public bool skipNextDecay;          // 直前ターンにマナ加速した→今ターンの減衰スキップ
        public int handLimitModifierThisTurn; // 掘削の儀などによる一時的な手札上限変動
        public bool digSelectPending;       // 導きの残光：次のドローで選択
        public int extraExcavatePending;    // 追加ドロー予約数

        public const int BoardLimit = 5;
        public const int CornerstoneLimit = 3;
        public int HandLimit => 7 + handLimitModifierThisTurn + (HasCornerstone(EffectId.None) ? 0 : 0);

        // 設置カードパッシブ判定
        public bool HasShrineAltar;     // 記憶の祭壇：マナ加速上昇量+1
        public bool HasSanctuary;       // 忘れられし聖堂：劣化猶予+1ターン
        public bool HasNameLantern;     // 名前の灯篭：発動コスト-1
        public bool HasFortThorn;       // 瓦礫の砦：自分のユニットが破壊されるたび相手に1ダメージ

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
