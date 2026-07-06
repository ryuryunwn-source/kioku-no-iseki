using System.Collections.Generic;
using UnityEngine;

namespace KiokuNoIseki
{
    // 写し身（gen_ で始まるカードID）の写真スプライトを実行時に保持する登録簿。
    // Resources に画像が無い写し身の描画で、GetCardArt がここを最初に参照する。
    public static class GeneratedArt
    {
        static readonly Dictionary<string, Sprite> s_map = new Dictionary<string, Sprite>();

        public static void Register(string cardId, Sprite sprite)
        {
            if (string.IsNullOrEmpty(cardId) || sprite == null) return;
            s_map[cardId] = sprite;
        }

        public static Sprite Get(string cardId)
        {
            if (cardId != null && s_map.TryGetValue(cardId, out var s)) return s;
            return null;
        }

        public static bool IsGenerated(string cardId) => cardId != null && cardId.StartsWith("gen_");
    }
}
