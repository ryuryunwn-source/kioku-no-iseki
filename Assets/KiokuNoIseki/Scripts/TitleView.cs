using UnityEngine;
using UnityEngine.UI;

namespace KiokuNoIseki
{
    // タイトル画面の見た目を持つコンポーネント。
    // レイアウト（位置・大きさ・ロゴ画像）はプレハブ側（エディタ）で自由に調整でき、
    // コードは各ボタンの onClick を配線するだけにする。
    public class TitleView : MonoBehaviour
    {
        [Header("参照（プレハブで割り当て）")]
        public Image background;    // 背景画像（任意。スプライトを入れれば表示）
        public Image logo;          // ロゴ画像（任意。設定されていれば表示）
        public Text titleText;      // タイトル文字
        public Text subText;        // サブタイトル
        public Button aiButton;         // AIと対戦
        public Button localButton;      // 2人で対戦（ローカル）
        public Button onlineButton;     // オンラインで対戦（β）
        public Button rulesButton;      // ルールを見る

        // 背景スプライトを適用する（null なら何もしない＝プレハブ設定を維持）。
        public void ApplyBackground(Sprite sprite)
        {
            if (background == null || sprite == null) return;
            background.sprite = sprite;
            background.color = Color.white;
            background.enabled = true;
        }

        // 実行時のOS日本語フォントを全テキストに適用する。
        public void ApplyFont(Font font)
        {
            if (font == null) return;
            foreach (var t in GetComponentsInChildren<Text>(true))
                t.font = font;
        }
    }
}
