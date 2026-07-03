using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using KiokuNoIseki;

// メニュー「Kioku / Build Card Prefab」でカードのプレハブを生成する。
// 生成後は Assets/Resources/Card.prefab を Inspector/Scene で自由に調整できる。
public static class CardPrefabBuilder
{
    const string OutPath = "Assets/Resources/Card.prefab";

    [MenuItem("Kioku/Build Card Prefab")]
    public static void Build()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Sprite frameSprite = Resources.Load<Sprite>("Frames/frame_base");

        // ルート
        var root = new GameObject("Card", typeof(RectTransform));
        var rrt = root.GetComponent<RectTransform>();
        rrt.sizeDelta = new Vector2(128, 180);
        var frame = root.AddComponent<Image>();
        frame.sprite = frameSprite; frame.color = Color.white;
        var button = root.AddComponent<Button>();
        var cv = root.AddComponent<CardView>();

        // 子要素
        var art = MakeImage(root, "Art", new Vector2(0.16f,0.53f), new Vector2(0.83f,0.89f));
        art.preserveAspect = false; art.color = Color.white;

        var nameT = MakeText(root, "Name", font, 11, new Color(0.97f,0.92f,0.80f),
            new Vector2(0.31f,0.46f), new Vector2(0.71f,0.51f), true);
        var techT = MakeText(root, "Tech", font, 11, new Color(0.95f,0.89f,0.75f),
            new Vector2(0.12f,0.05f), new Vector2(0.88f,0.17f), true);
        var costT = MakePointText(root, "Cost", font, 15, new Color(1f,0.86f,0.42f), new Vector2(0.148f,0.487f), 22);
        var elemT = MakePointText(root, "Elem", font, 11, new Color(1f,0.97f,0.88f), new Vector2(0.865f,0.477f), 20);
        var atkT  = MakePointText(root, "Atk", font, 15, new Color(1f,0.55f,0.42f), new Vector2(0.810f,0.266f), 22);
        var defT  = MakePointText(root, "Def", font, 15, new Color(0.58f,0.82f,1f), new Vector2(0.283f,0.266f), 22);
        var flagsT = MakeText(root, "Flags", font, 9, new Color(0.96f,0.86f,0.52f),
            new Vector2(0.28f,0.36f), new Vector2(0.72f,0.44f), true);

        // 守護バッジ（下地＋文字）
        var badge = new GameObject("GuardBadge", typeof(RectTransform));
        badge.transform.SetParent(root.transform, false);
        SetStretch(badge.GetComponent<RectTransform>(), new Vector2(0.17f,0.795f), new Vector2(0.47f,0.875f));
        var badgeImg = badge.AddComponent<Image>(); badgeImg.color = new Color(0.05f,0.09f,0.16f,0.88f); badgeImg.raycastTarget = false;
        AddOutline(badge, new Color(0.45f,0.75f,1f,0.95f));
        var guardT = MakeText(root, "GuardText", font, 11, new Color(0.78f,0.93f,1f),
            new Vector2(0.17f,0.795f), new Vector2(0.47f,0.875f), true);
        guardT.text = "守護";
        guardT.transform.SetParent(badge.transform, false); // バッジの子にして一緒に動かせる
        SetStretch(guardT.rectTransform, Vector2.zero, Vector2.one);

        // 参照割り当て
        cv.frame = frame; cv.art = art; cv.nameText = nameT; cv.costText = costT;
        cv.elemText = elemT; cv.atkText = atkT; cv.defText = defT; cv.techText = techT;
        cv.flagsText = flagsT; cv.guardBadge = badge; cv.button = button;

        // 保存
        System.IO.Directory.CreateDirectory("Assets/Resources");
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, OutPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        Debug.Log($"[CardPrefabBuilder] 生成: {OutPath}");
    }

    static Image MakeImage(GameObject parent, string name, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var img = go.AddComponent<Image>(); img.raycastTarget = false;
        SetStretch(img.rectTransform, aMin, aMax);
        return img;
    }

    static Text MakeText(GameObject parent, string name, Font font, int size, Color col,
        Vector2 aMin, Vector2 aMax, bool shade)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.color = col; t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        SetStretch(t.rectTransform, aMin, aMax);
        if (shade) AddOutline(go, new Color(0,0,0,0.9f));
        return t;
    }

    static Text MakePointText(GameObject parent, string name, Font font, int size, Color col, Vector2 anchor, float sz)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent.transform, false);
        var t = go.AddComponent<Text>();
        t.font = font; t.fontSize = size; t.color = col; t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f,0.5f);
        rt.anchoredPosition = Vector2.zero; rt.sizeDelta = new Vector2(sz, sz);
        AddOutline(go, new Color(0,0,0,0.9f));
        return t;
    }

    static void SetStretch(RectTransform rt, Vector2 aMin, Vector2 aMax)
    {
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static void AddOutline(GameObject go, Color c)
    {
        var o = go.AddComponent<Outline>();
        o.effectColor = c; o.effectDistance = new Vector2(1.2f, -1.2f);
    }
}
