using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using WarPoker.Cards;
using WarPoker.Game;
using WarPoker.UI;
using Object = UnityEngine.Object;

namespace WarPoker.EditorTools
{
    /// <summary>
    /// Renders the procedural cards through the real uGUI CardView into PNGs:
    ///   Previews/cards_preview.png    a sheet of faces and backs at both in-game sizes, drawn at 2× density
    ///   Previews/cards_mock.png       a 1920×1080 mock of the table (map, compass, board, deck, camp stand-ins)
    /// Works in batchmode as long as graphics are available (no -nographics).
    /// </summary>
    public static class CardPreviewExport
    {
        static readonly Vector2 Small = new Vector2(62, 86), Big = new Vector2(96, 134);
        static readonly string[] Faces = { "As", "Kh", "Td", "7c", "Qs" };

        [MenuItem("WarPoker/Export Card Preview")]
        public static void Export()
        {
            Directory.CreateDirectory("Previews");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            MapArt.BackgroundPixels();
            Debug.Log("[WarPoker] Map generation took " + sw.ElapsedMilliseconds + " ms (Editor, Mono)");
            Render("Previews/cards_preview.png", 1440, 760, 2f, BuildSheet);
            Render("Previews/cards_mock.png", 1920, 1080, 1f, BuildMock);
            Debug.Log("[WarPoker] Card previews written to Previews/cards_preview.png and Previews/cards_mock.png");
        }

        static void BuildSheet(RectTransform root)
        {
            var bg = UIKit.Deco(root, "Map", MapArt.Background(), Color.white);
            UIKit.Stretch(bg.rectTransform);
            // big row: the five faces and a back
            for (int i = 0; i <= Faces.Length; i++)
            {
                var c = CardView.Create(root, "Big" + i, Big);
                UIKit.Center(c.Rect, new Vector2(-280 + i * 112, 80), Big);
                if (i < Faces.Length) c.SetCard(Faces[i], true);
            }
            // small row: same, plus a camp-style tilted pair and a dimmed card
            for (int i = 0; i <= Faces.Length; i++)
            {
                var c = CardView.Create(root, "Small" + i, Small);
                UIKit.Center(c.Rect, new Vector2(-280 + i * 80, -95), Small);
                if (i < Faces.Length) c.SetCard(Faces[i], true);
            }
            for (int i = 0; i < 2; i++)
            {
                var c = CardView.Create(root, "Pair" + i, Small);
                UIKit.Center(c.Rect, new Vector2(230 + i * 38, -95), Small);
                c.Rect.localEulerAngles = new Vector3(0, 0, i == 0 ? 6f : -6f);
                c.SetCard(i == 0 ? "Jh" : "9s", true);
            }
            var dim = CardView.Create(root, "Dim", Small);
            UIKit.Center(dim.Rect, new Vector2(330, -95), Small);
            dim.SetCard("2c", true);
            dim.SetDim(true);
        }

        static void BuildMock(RectTransform root)
        {
            var bg = UIKit.Deco(root, "Map", MapArt.Background(), Color.white);
            UIKit.Stretch(bg.rectTransform);

            Vector2 center = new Vector2(0, 30);
            for (int i = 0; i < 5; i++)
            {
                Vector2 pos = center + new Vector2(10 + (i - 2) * 106f, 10);
                var slot = UIKit.Deco(root, "Slot" + i, ProcSprites.RoundedRect(10, 3, 0.4f), new Color(0.3f, 0.2f, 0.1f, 0.18f));
                UIKit.Center(slot.rectTransform, pos, Big);
                if (i < 4)
                {
                    var c = CardView.Create(root, "Board" + i, Big);
                    UIKit.Center(c.Rect, pos, Big);
                    c.SetCard(Faces[i], true);
                    if (i == 1) c.SetHighlight(true);
                }
            }
            for (int i = 0; i < 3; i++)
            {
                var c = CardView.Create(root, "Deck" + i, Small);
                UIKit.Center(c.Rect, new Vector2(-345 - i * 2f, 40 + i * 2f), Small);
            }
            var pot = UIKit.Panel(root, "Pot", "plank", Color.white, 0.6f, true);
            UIKit.Center(pot.rectTransform, new Vector2(0, -106), new Vector2(190, 40));

            // camp stand-ins on the 720×330 ellipse
            for (int d = 0; d < 8; d++)
            {
                float a = (-90f - d * 45f) * Mathf.Deg2Rad;
                Vector2 pos = center + new Vector2(Mathf.Cos(a) * 720f, Mathf.Sin(a) * 330f);
                var camp = UIKit.Rect("Camp" + d, root);
                UIKit.Center(camp, pos, new Vector2(260, 150));
                var body = UIKit.Panel(camp, "Body", "panel_wide", Color.white, 0.38f, true);
                UIKit.Center(body.rectTransform, Vector2.zero, new Vector2(260, 150));
                UIKit.AddShadow(body, 4f, 0.35f);
                var banner = UIKit.Panel(camp, "Banner", "bar_fill", d == 0 ? Theme.Positive : Theme.Crimson, 0.9f, true);
                UIKit.Center(banner.rectTransform, new Vector2(0, 62), new Vector2(244, 42));
                for (int i = 0; i < 2; i++)
                {
                    var c = CardView.Create(camp, "Card" + i, Small);
                    UIKit.Center(c.Rect, new Vector2(i == 0 ? 58f : 96f, -6f), Small);
                    c.Rect.localEulerAngles = new Vector3(0, 0, i == 0 ? 6f : -6f);
                    if (d == 0) c.SetCard(i == 0 ? "Ah" : "Qs", true);
                    else if (d == 5) { c.SetCard(i == 0 ? "Tc" : "Td", true); c.SetDim(true); }
                }
            }
        }

        static void Render(string path, int w, int h, float scale, Action<RectTransform> build)
        {
            var camGo = new GameObject("PreviewCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
            cam.orthographic = true;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            rt.antiAliasing = 1;
            cam.targetTexture = rt;

            var canvasGo = new GameObject("PreviewCanvas", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;
            canvas.scaleFactor = scale;
            try
            {
                build((RectTransform)canvasGo.transform);
                Canvas.ForceUpdateCanvases();
                cam.Render();
                Canvas.ForceUpdateCanvases();
                cam.Render();   // second pass: font atlas is filled after the first rebuild

                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(canvasGo);
                Object.DestroyImmediate(camGo);
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }
}
