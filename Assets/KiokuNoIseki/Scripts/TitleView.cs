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

        // タイトルをロゴ画像にする。sprite があれば画像を表示し、タイトル/サブ文字は隠す。
        // プレハブに logo Image が割り当てられていなければ、タイトル文字の位置に自動生成する。
        public void ApplyLogo(Sprite sprite)
        {
            if (sprite == null) return; // 画像が無ければ何もしない＝従来どおり文字表示

            if (logo == null)
            {
                Transform parent = titleText != null ? titleText.transform.parent : transform;
                var go = new GameObject("Logo", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(parent, false);
                logo = go.GetComponent<Image>();
                var rt = logo.rectTransform;
                if (titleText != null)
                {
                    var src = titleText.rectTransform;
                    rt.anchorMin = src.anchorMin; rt.anchorMax = src.anchorMax; rt.pivot = src.pivot;
                    rt.anchoredPosition = src.anchoredPosition;
                    rt.sizeDelta = new Vector2(Mathf.Max(src.sizeDelta.x, 520), Mathf.Max(src.sizeDelta.y, 200));
                }
                else
                {
                    rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
                    rt.anchoredPosition = new Vector2(0, 190);
                    rt.sizeDelta = new Vector2(560, 220);
                }
            }

            logo.sprite = sprite;
            logo.color = Color.white;
            logo.preserveAspect = true; // 画像の縦横比を保つ
            logo.enabled = true;
            logo.gameObject.SetActive(true); // プレハブでLogoが非アクティブなので必ず有効化する
            // ロゴを大きく・少し上に表示する（縦横比は保持されるので枠内に収まる範囲で最大化）
            var lrt = logo.rectTransform;
            lrt.sizeDelta = new Vector2(880, 360);
            lrt.anchoredPosition = new Vector2(lrt.anchoredPosition.x, 200);
            if (titleText != null) titleText.enabled = false; // ロゴがあれば文字は隠す
            if (subText != null) subText.enabled = false;
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
