using UnityEngine;
using UnityEngine.UI;

namespace KiokuNoIseki
{
    // カード裏面（石板に彫り込んだ「マイモン」の紋章）を生成する共有ユーティリティ。
    // オフライン(GameUI)とオンライン(OnlineController)で相手手札・デッキの裏面を同じ見た目にするために使う。
    public static class CardBackArt
    {
        static Sprite s_circle;
        static Sprite CircleSprite()
        {
            if (s_circle != null) return s_circle;
            const int sz = 64;
            var tex = new Texture2D(sz, sz, TextureFormat.RGBA32, false);
            float r = sz * 0.5f, cx = r, cy = r;
            var px = new Color32[sz * sz];
            for (int y = 0; y < sz; y++)
                for (int x = 0; x < sz; x++)
                {
                    float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
                    bool inside = dx * dx + dy * dy <= r * r;
                    px[y * sz + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px); tex.Apply();
            tex.wrapMode = TextureWrapMode.Clamp; tex.filterMode = FilterMode.Bilinear;
            s_circle = Sprite.Create(tex, new Rect(0, 0, sz, sz), new Vector2(0.5f, 0.5f));
            return s_circle;
        }

        static void Circle(Transform parent, Color color, Vector2 center, float size)
        {
            var go = new GameObject("Circle"); go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.sprite = CircleSprite(); img.color = color; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center; rt.sizeDelta = new Vector2(size, size);
        }

        // parent の中に、中心 center・サイズ size で裏面を作る（pivot中央）。
        public static GameObject Build(Transform parent, Vector2 center, Vector2 size, Font font)
        {
            float s = size.x / 128f; // 128幅を基準にした拡縮
            Color edge = new Color(0.33f, 0.27f, 0.42f);   // 記憶＝深い紫
            Color bronzeCol = new Color(0.62f, 0.49f, 0.27f);
            Color stone = new Color(0.13f, 0.12f, 0.15f);

            var go = new GameObject("CardBack"); go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>(); img.color = edge; img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = center; rt.sizeDelta = size;

            // 銅トリム
            var bronze = new GameObject("Bronze"); bronze.transform.SetParent(go.transform, false);
            var bi = bronze.AddComponent<Image>(); bi.color = bronzeCol; bi.raycastTarget = false;
            var brt = bi.rectTransform; brt.anchorMin = Vector2.zero; brt.anchorMax = Vector2.one;
            brt.offsetMin = new Vector2(3 * s, 3 * s); brt.offsetMax = new Vector2(-3 * s, -3 * s);

            // 石板の面
            var face = new GameObject("Face"); face.transform.SetParent(go.transform, false);
            var fi = face.AddComponent<Image>(); fi.color = stone; fi.raycastTarget = false;
            var frt = fi.rectTransform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(5 * s, 5 * s); frt.offsetMax = new Vector2(-5 * s, -5 * s);

            // 中央の同心円彫紋
            Circle(go.transform, bronzeCol,                       Vector2.zero, 78 * s);
            Circle(go.transform, stone,                           Vector2.zero, 70 * s);
            Circle(go.transform, bronzeCol * 0.85f,               Vector2.zero, 50 * s);
            Circle(go.transform, new Color(0.10f, 0.09f, 0.11f),  Vector2.zero, 44 * s);

            void Diamond(Vector2 c, float sz2, Color col)
            {
                var d = new GameObject("Diamond"); d.transform.SetParent(go.transform, false);
                var di = d.AddComponent<Image>(); di.color = col; di.raycastTarget = false;
                var drt = di.rectTransform;
                drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.5f); drt.pivot = new Vector2(0.5f, 0.5f);
                drt.anchoredPosition = c; drt.sizeDelta = new Vector2(sz2, sz2);
                d.transform.localEulerAngles = new Vector3(0, 0, 45);
                var o = d.AddComponent<Outline>(); o.effectColor = bronzeCol; o.effectDistance = new Vector2(1f * s, -1f * s);
            }
            Diamond(Vector2.zero, 30 * s, edge * 1.2f);

            // 中央の「記」字
            var glyphGo = new GameObject("Glyph"); glyphGo.transform.SetParent(go.transform, false);
            var glyph = glyphGo.AddComponent<Text>();
            glyph.text = "記"; glyph.font = font; glyph.fontSize = Mathf.Max(8, Mathf.RoundToInt(20 * s));
            glyph.alignment = TextAnchor.MiddleCenter; glyph.color = new Color(0.93f, 0.87f, 0.73f); glyph.raycastTarget = false;
            var grt = glyph.rectTransform;
            grt.anchorMin = grt.anchorMax = new Vector2(0.5f, 0.5f); grt.pivot = new Vector2(0.5f, 0.5f);
            grt.anchoredPosition = Vector2.zero; grt.sizeDelta = new Vector2(30 * s, 30 * s);

            // 四隅の小菱形
            float ccx = size.x * 0.5f - 13 * s, ccy = size.y * 0.5f - 13 * s;
            Diamond(new Vector2(-ccx,  ccy), 9 * s, bronzeCol);
            Diamond(new Vector2( ccx,  ccy), 9 * s, bronzeCol);
            Diamond(new Vector2(-ccx, -ccy), 9 * s, bronzeCol);
            Diamond(new Vector2( ccx, -ccy), 9 * s, bronzeCol);

            return go;
        }
    }
}
