using System.Collections.Generic;
using UnityEngine;

namespace KiokuNoIseki
{
    // 写真(Texture2D)から写し身(CardInstance)を組み立てる橋渡し役。
    // ・画像から決定論的ハッシュと平均HSVを算出（→CardGeneratorへ）
    // ・写真をSpriteにしてGeneratedArtへ登録（→対戦画面で表示）
    // ・生成した写し身は WriteshiCollection に貯め、対戦開始時にデッキへ合流する。
    public static class PhotoWriteshi
    {
        // 写真 + （任意の）AI命名結果から写し身を作る。名前がnull/空ならオフライン候補で決定論的に命名。
        public static CardInstance Build(Texture2D tex, string trueName = null, string techniqueName = null)
        {
            if (tex == null) return null;

            ulong hash = HashTexture(tex);
            ComputeAverageHsv(tex, out float h, out float s, out float v);

            var gen = CardGenerator.Generate(hash, h, s, v, trueName, techniqueName);
            var inst = new CardInstance(gen);

            var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            GeneratedArt.Register(inst.definition.id, sprite);
            return inst;
        }

        // 画像バイト列から 64bit FNV-1a ハッシュ（同じ写真は常に同じ値）。
        static ulong HashTexture(Texture2D tex)
        {
            Color32[] px;
            try { px = tex.GetPixels32(); }
            catch { return (ulong)((tex.width * 73856093) ^ (tex.height * 19349663)); }

            const ulong prime = 1099511628211UL;
            ulong hash = 14695981039346656037UL;
            // 全画素だと重いので最大4096点をストライドサンプリング（決定論的）。
            int step = Mathf.Max(1, px.Length / 4096);
            for (int i = 0; i < px.Length; i += step)
            {
                var c = px[i];
                hash = (hash ^ c.r) * prime;
                hash = (hash ^ c.g) * prime;
                hash = (hash ^ c.b) * prime;
            }
            return hash;
        }

        // 8x8 相当のダウンサンプルで平均色を求め、HSV(0..1)に変換（15-1）。
        static void ComputeAverageHsv(Texture2D tex, out float h, out float s, out float v)
        {
            Color32[] px;
            try { px = tex.GetPixels32(); }
            catch { h = 0f; s = 0f; v = 0.5f; return; }

            long r = 0, g = 0, b = 0; int n = 0;
            int step = Mathf.Max(1, px.Length / 64);
            for (int i = 0; i < px.Length; i += step)
            {
                r += px[i].r; g += px[i].g; b += px[i].b; n++;
            }
            if (n == 0) { h = 0f; s = 0f; v = 0.5f; return; }
            Color avg = new Color(r / (255f * n), g / (255f * n), b / (255f * n));
            Color.RGBToHSV(avg, out h, out s, out v);
        }
    }

    // 端末内で生成した写し身の一時コレクション（対戦開始時にデッキへ渡す）。
    // ※実行時のみ保持（永続保存は将来の拡張。15章のキャッシュ相当）。
    public static class WriteshiCollection
    {
        public static readonly List<CardInstance> Cards = new List<CardInstance>();
        public static int Count => Cards.Count;
        public static void Add(CardInstance c) { if (c != null) Cards.Add(c); }
        public static void Clear() => Cards.Clear();

        // 対戦へ渡すためのコピー（同一インスタンスを複数対戦で使い回さないよう都度clone）。
        public static List<CardInstance> Snapshot()
        {
            var list = new List<CardInstance>();
            foreach (var c in Cards)
            {
                if (c.generated != null) list.Add(new CardInstance(c.generated));
                else list.Add(new CardInstance(c.definition.Clone()));
            }
            return list;
        }
    }
}
