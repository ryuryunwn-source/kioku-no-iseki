using UnityEditor;
using UnityEngine;

// Assets/Resources/CardArt/ に入れた画像を、自動で Sprite として取り込む。
// （手動で Texture Type を Sprite に変える作業が不要になる）
public class CardArtImporter : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        string p = assetPath.Replace("\\", "/");
        if (!p.Contains("/Resources/CardArt/") && !p.Contains("/Resources/Frames/")) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
    }
}
