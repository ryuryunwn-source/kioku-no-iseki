using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using KiokuNoIseki;

// メニュー「Kioku / Build Title Prefab」でタイトル画面のプレハブを生成する。
// 生成後は Assets/Resources/Title.prefab を Inspector/Scene で自由に調整できる
// （ロゴ画像の差し替え・ボタン位置・文字など）。
public static class TitlePrefabBuilder
{
    const string OutPath = "Assets/Resources/Title.prefab";

    [MenuItem("Kioku/Build Title Prefab")]
    public static void Build()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // ルート（全画面ストレッチ）
        var root = new GameObject("Title", typeof(RectTransform));
        var rrt = root.GetComponent<RectTransform>();
        rrt.anchorMin = Vector2.zero; rrt.anchorMax = Vector2.one;
        rrt.offsetMin = Vector2.zero; rrt.offsetMax = Vector2.zero;
        var tv = root.AddComponent<TitleView>();

        // 背景（全画面ストレッチ・最背面）。スプライトがあれば設定、無ければ暗色。
        var bgGo = new GameObject("Background", typeof(RectTransform));
        bgGo.transform.SetParent(root.transform, false);
        var bg = bgGo.AddComponent<Image>();
        bg.raycastTarget = false; bg.preserveAspect = false;
        var bgrt = bg.rectTransform;
        bgrt.anchorMin = Vector2.zero; bgrt.anchorMax = Vector2.one;
        bgrt.offsetMin = Vector2.zero; bgrt.offsetMax = Vector2.zero;
        var bgSprite = Resources.Load<Sprite>("Backgrounds/title_bg");
        if (bgSprite != null) { bg.sprite = bgSprite; bg.color = Color.white; }
        else bg.color = new Color(0.06f, 0.06f, 0.10f, 1f);

        // ロゴ画像（任意）：初期は非表示。エディタでスプライトを入れて有効化して使う。
        var logoGo = new GameObject("Logo", typeof(RectTransform));
        logoGo.transform.SetParent(root.transform, false);
        var logo = logoGo.AddComponent<Image>();
        logo.raycastTarget = false; logo.preserveAspect = true; logo.color = Color.white;
        Center(logo.rectTransform, new Vector2(0, 190), new Vector2(520, 200));
        logoGo.SetActive(false);

        // タイトル文字
        var titleT = MakeText(root, "Title", font, 60, Color.white, "記憶の遺跡");
        Center(titleT.rectTransform, new Vector2(0, 190), new Vector2(800, 90));

        // サブタイトル
        var subT = MakeText(root, "Sub", font, 22, new Color(0.7f, 0.7f, 0.75f), "― Kioku no Iseki ―");
        Center(subT.rectTransform, new Vector2(0, 138), new Vector2(800, 40));

        // ボタン群
        var b1 = MakeButton(root, "AIと対戦", font, new Vector2(0, 40), new Vector2(340, 60), new Color(0.30f, 0.40f, 0.55f));
        var b2 = MakeButton(root, "2人で対戦（ローカル）", font, new Vector2(0, -32), new Vector2(340, 56), new Color(0.32f, 0.50f, 0.40f));
        var b3 = MakeButton(root, "オンラインで対戦（β）", font, new Vector2(0, -98), new Vector2(340, 56), new Color(0.35f, 0.38f, 0.55f));
        var b4 = MakeButton(root, "ルールを見る", font, new Vector2(0, -164), new Vector2(340, 56), new Color(0.40f, 0.40f, 0.48f));

        // 参照割り当て
        tv.background = bg; tv.logo = logo; tv.titleText = titleT; tv.subText = subT;
        tv.aiButton = b1; tv.localButton = b2; tv.onlineButton = b3; tv.rulesButton = b4;

        // 保存
        System.IO.Directory.CreateDirectory("Assets/Resources");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, OutPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[TitlePrefabBuilder] 生成: {OutPath}");
    }

    static void Center(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static Text MakeText(GameObject parent, string name, Font font, int size, Color col, string text)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.color = col; t.text = text;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    static Button MakeButton(GameObject parent, string label, Font font, Vector2 pos, Vector2 size, Color color)
    {
        var go = new GameObject("Button", typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var img = go.AddComponent<Image>(); img.color = color;
        var btn = go.AddComponent<Button>();
        Center(img.rectTransform, pos, size);
        var t = MakeText(go, "Label", font, 20, Color.white, label);
        var rt = t.rectTransform;
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        return btn;
    }
}
