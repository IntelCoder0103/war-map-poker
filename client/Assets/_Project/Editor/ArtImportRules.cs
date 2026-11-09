using UnityEditor;

namespace WarPoker.EditorTools
{
    /// <summary>
    /// Every image under Assets/_Project/Resources/ is imported as a UI sprite
    /// (the project defaults to 3D, which would import plain textures), so code can
    /// Resources.Load&lt;Sprite&gt;() it. 9-slice borders are set in code (UISkin).
    /// </summary>
    public class ArtImportRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Resources/")) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
