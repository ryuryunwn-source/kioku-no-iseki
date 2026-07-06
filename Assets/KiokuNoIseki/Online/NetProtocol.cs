using System;
using System.Collections.Generic;
using KiokuNoIseki;

namespace KiokuNoIseki.Online
{
    // 【v2】写し身の同期用。写真そのものは送らず、生成された結果（合成CardData相当）だけを載せる（18-4）。
    // カードIDは画像ハッシュから決定論的なので、受信側は自分が同じ写真を持っていれば実画像で描画でき、
    // 相手の写し身はプレースホルダ画像になる（写真は端末外に出ない）。
    [Serializable]
    public class GenCardInfo
    {
        public string cardId;   // "gen_xxxxxxxx"
        public string name;     // 名前
        public int elem;        // Element
        public int cost;
        public int atk;         // 基礎攻撃力（definition.attack）
        public int def;         // 基礎防御力
        public int incCost;     // 発動コスト
        public string techName; // 技名
        public int techEff;     // EffectId
        public int techMag;     // 技の効果量

        public CardData ToCardData() => new CardData
        {
            id = cardId,
            trueName = string.IsNullOrEmpty(name) ? "写し身" : name,
            kind = CardKind.Guardian,
            element = (Element)elem,
            cost = cost,
            attack = atk,
            defense = def,
            guard = false,
            effectText = EffectText.Describe((EffectId)techEff, techMag),
            incantationCost = incCost,
            techniqueName = techName,
            techniqueEffect = (EffectId)techEff,
            techniqueMagnitude = techMag,
        };

        public static GenCardInfo From(CardData d) => new GenCardInfo
        {
            cardId = d.id, name = d.trueName, elem = (int)d.element, cost = d.cost,
            atk = d.attack, def = d.defense, incCost = d.incantationCost,
            techName = d.techniqueName, techEff = (int)d.techniqueEffect, techMag = d.techniqueMagnitude,
        };
    }

    // JsonUtility はトップレベル配列を扱えないためラップする（client→hostの写し身送信に使用）。
    [Serializable]
    public class GenCardList
    {
        public GenCardInfo[] items = Array.Empty<GenCardInfo>();
    }

    // 18章：ホスト権威の同期で送受信する「視点ごとのゲーム状態スナップショット」。
    // 隠し情報保護のため、相手の手札・山札の中身は送らず、枚数のみ送る。
    // カードの不変データ(名前/コスト/技/効果)は両者が CardDatabase で共有しているので、
    // スナップショットには定義ID(cardId)と可変状態だけを載せれば十分。

    [Serializable]
    public class CardView
    {
        public int iid;        // インスタンスID（操作時の指定に使う）
        public string cardId;  // 定義ID（空なら裏向き=相手の手札）
        public int atk;        // 現在攻撃力（強化等込み）
        public int def;        // 現在防御力（残り）
        public int engraving;  // 強化数
        public bool sick;      // 召喚酔い
        public bool faceDown;  // 裏向き（相手の手札）
    }

    [Serializable]
    public class PlayerView
    {
        public string name;
        public int hp;
        public int gauge;
        public int gaugeMax;
        public int memoryCount;
        public int pactCount;   // 完全強化(強化3)が勝利ゾーンに何体か（盟約進捗・公開情報）
        public int rubble;
        public CardView[] hand = Array.Empty<CardView>();
        public CardView[] board = Array.Empty<CardView>();
        public CardView[] cornerstones = Array.Empty<CardView>();
    }

    [Serializable]
    public class GameView
    {
        public PlayerView me;   // 受信者自身（手札は表）
        public PlayerView foe;  // 相手（手札は裏向き）
        public int deckCount;   // 山札残数
        public string deckTopId = "";  // デッキトップの定義ID（公開情報。空=デッキ切れ）
        public int deckTopEng;         // デッキトップの強化数
        public int phase;       // TurnPhase
        public bool myTurn;     // 受信者の手番か
        public int result;      // 0=継続 / 1=自分の勝ち / 2=相手の勝ち
        public string[] log = Array.Empty<string>();
        // このビューに写っている写し身の定義情報（受信側が名前/技/系統を復元して描画する）。写真は含まない。
        public GenCardInfo[] genCards = Array.Empty<GenCardInfo>();
    }

    // クライアント→ホストの操作要求
    public enum NetActionType { PlayCard = 0, Attack = 1, Inscribe = 2, Technique = 3, EndTurn = 4 }

    [Serializable]
    public class NetAction
    {
        public int type;   // NetActionType
        public int a;      // 主対象のインスタンスID（手札カード/攻撃元/ユニット/捧げる手札）
        public int b;      // 副対象のインスタンスID（攻撃先=0で本体／呪文の対象。未使用は0）
    }

    public static class NetJson
    {
        // JsonUtility は配列ルートやnull安全が弱いのでラッパ経由で安全に扱う
        public static string ToJson(GameView v) => UnityEngine.JsonUtility.ToJson(v);
        public static GameView ViewFromJson(string s) => UnityEngine.JsonUtility.FromJson<GameView>(s);
        public static string ToJson(NetAction a) => UnityEngine.JsonUtility.ToJson(a);
        public static NetAction ActionFromJson(string s) => UnityEngine.JsonUtility.FromJson<NetAction>(s);
    }
}
