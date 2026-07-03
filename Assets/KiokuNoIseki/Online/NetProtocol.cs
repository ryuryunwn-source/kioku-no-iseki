using System;
using System.Collections.Generic;

namespace KiokuNoIseki.Online
{
    // 18章：ホスト権威の同期で送受信する「視点ごとのゲーム状態スナップショット」。
    // 隠し情報保護のため、相手の手札・遺構デッキの中身は送らず、枚数のみ送る。
    // カードの不変データ(名前/コスト/技/効果)は両者が CardDatabase で共有しているので、
    // スナップショットには定義ID(cardId)と可変状態だけを載せれば十分。

    [Serializable]
    public class CardView
    {
        public int iid;        // インスタンスID（操作時の指定に使う）
        public string cardId;  // 定義ID（空なら裏向き=相手の手札）
        public int atk;        // 現在攻撃力（刻印等込み）
        public int def;        // 現在防御力（残り）
        public int engraving;  // 刻印数
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
        public int pactCount;   // 完全刻印(刻印3)が記憶領域に何体か（盟約進捗・公開情報）
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
        public int deckCount;   // 遺構デッキ残数
        public string deckTopId = "";  // デッキトップの定義ID（公開情報。空=デッキ切れ）
        public int deckTopEng;         // デッキトップの刻印数
        public int phase;       // TurnPhase
        public bool myTurn;     // 受信者の手番か
        public int result;      // 0=継続 / 1=自分の勝ち / 2=相手の勝ち
        public string[] log = Array.Empty<string>();
    }

    // クライアント→ホストの操作要求
    public enum NetActionType { PlayCard = 0, Attack = 1, Inscribe = 2, Technique = 3, EndTurn = 4 }

    [Serializable]
    public class NetAction
    {
        public int type;   // NetActionType
        public int a;      // 主対象のインスタンスID（手札カード/攻撃元/守護者/捧げる手札）
        public int b;      // 副対象のインスタンスID（攻撃先=0で本体／想起術の対象。未使用は0）
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
