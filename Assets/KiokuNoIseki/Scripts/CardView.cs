using UnityEngine;
using UnityEngine.UI;

namespace KiokuNoIseki
{
    // カード1枚分の見た目を持つコンポーネント。
    // レイアウト（位置・大きさ）はプレハブ側（エディタ）で自由に調整でき、
    // コードは Bind() でデータ（絵・数字・文字）を流し込むだけにする。
    public class CardView : MonoBehaviour
    {
        [Header("参照（プレハブで割り当て）")]
        public Image frame;         // 石板フレーム
        public Image art;           // イラスト窓
        public Text nameText;       // カード名
        public Text costText;       // コスト
        public Text elemText;       // 系統文字
        public Text atkText;        // 攻撃
        public Text defText;        // 防御
        public Text techText;       // 技/種別
        public Text flagsText;      // 刻印/酔い
        public GameObject guardBadge; // 守護バッジ（表示/非表示）
        public Button button;       // クリック領域（ルート）

        // データ流し込み。font は実行時のOS日本語フォント。
        public void Bind(Font font, Sprite frameSprite, Sprite artSprite,
            string name, string cost, bool isGuardian,
            string elem, string atk, string def, string tech, string flags, bool guard)
        {
            if (frame != null && frameSprite != null) { frame.sprite = frameSprite; frame.color = Color.white; }
            if (art != null && artSprite != null) { art.sprite = artSprite; art.color = Color.white; art.preserveAspect = false; }

            Set(nameText, name, font);
            Set(costText, cost, font);
            Set(techText, tech, font);

            bool showG = isGuardian;
            SetActive(elemText, showG); SetActive(atkText, showG); SetActive(defText, showG);
            if (showG)
            {
                Set(elemText, elem, font);
                Set(atkText, atk, font);
                Set(defText, def, font);
            }

            if (flagsText != null)
            {
                bool hasFlags = !string.IsNullOrEmpty(flags);
                flagsText.gameObject.SetActive(hasFlags);
                if (hasFlags) Set(flagsText, flags, font);
            }

            if (guardBadge != null) guardBadge.SetActive(guard);
        }

        static void Set(Text t, string s, Font font)
        {
            if (t == null) return;
            if (font != null) t.font = font;
            t.text = s;
        }
        static void SetActive(Text t, bool v) { if (t != null) t.gameObject.SetActive(v); }
    }
}
