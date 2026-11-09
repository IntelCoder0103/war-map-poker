using System.IO;
using UnityEditor;
using UnityEngine;
using WarPoker.Game;
using WarPoker.UI;

namespace WarPoker.EditorTools
{
    /// <summary>Saves the procedural map and compass as PNGs (Previews/map_preview.png, Previews/compass_preview.png),
    /// handy for checking the look or as a starting point for the artist.</summary>
    public static class MapPreviewExport
    {
        [MenuItem("WarPoker/Export Map Preview")]
        public static void Export()
        {
            Directory.CreateDirectory("Previews");
            Save("Previews/map_preview.png", MapArt.Width, MapArt.Height, MapArt.BackgroundPixels());
            Save("Previews/compass_preview.png", 192, 192, ProcSprites.Raster(192, 192, MapArt.CompassPixel, 2));
            Debug.Log("[WarPoker] Map preview written to Previews/map_preview.png and Previews/compass_preview.png");
        }

        static void Save(string path, int w, int h, Color[] px)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
        }
    }
}
