using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace WarPoker.EditorTools
{
    /// <summary>
    /// Renders the animated troop sprites in Resources/Troops/ from the Asset Store pack
    /// "Mini Legion Footman PBR HP Polyart" (Standard Asset Store EULA: rendered art may ship,
    /// the pack itself is NOT in the repo, see .gitignore). Every asset is loaded by path, so this
    /// compiles without the pack and only logs an error when it is missing.
    ///
    /// Output (one horizontal row of square 128 px frames, frame 0 on the left, transparent bg):
    ///   soldier_idle.png, soldier_walk.png (in place, seamless loop), soldier_victory.png,
    ///   soldier_death.png (last frame = body on the ground), plus soldier.png (256 px static icon:
    ///   idle frame 0 with the same framing, so swapping icon and strips never jumps).
    /// All strips share ONE camera, scale and ground line: the feet's ground point sits at the
    /// same pixel in every frame of every strip (horizontally centred, so mirroring the sprite
    /// for leftward movement does not shift it).
    ///
    /// Look: the pack's hand-painted HP texture in its original blue (with -troopColors red: blue cloth
    /// hue-shifted to vermilion, helmet-guard plates and the shield's inner face in red lacquer); ortho 3/4 camera
    /// looking down 24°, warm key + cool fill, 4x supersampling, dark outline, soft contact shadow.
    /// Batch: Unity -batchmode -quit -projectPath client -executeMethod WarPoker.EditorTools.TroopRenderer.RenderAll
    /// Optional args: -troopOut &lt;dir&gt; (write elsewhere), -troopYaw &lt;deg&gt;, -troopColors original|red, -troopGuard red|black|steel,
    /// -troopDumpAlbedo &lt;png&gt; (save the recoloured texture for inspection).
    /// Needs a graphics device (no -nographics).
    /// </summary>
    public static class TroopRenderer
    {
        const string Pack = "Assets/Mini Legion Footman PBR HP Polyart/";
        const string Model = Pack + "Meshes/Footman_Default.fbx";               // UVs of the HP/PBR textures
        const string Albedo = Pack + "Textures/HP/footmanBlue.png";            // hand painted, reads well small
        const string GlossTex = Pack + "Textures/PBR/MatallicSmoothness.png";  // same UVs: R metallic, A smoothness
        const string AnimDir = Pack + "Animations/";
        const string OutDir = "Assets/_Project/Resources/Troops";

        /// One strip: clip time range in seconds. Loops sample [t0, t1) so the last frame flows into
        /// frame 0; one-shots sample [t0, t1] inclusive so the last frame is the final pose.
        struct Strip
        {
            public string File, Clip; public int Frames; public float T0, T1; public bool Loop; public float Yaw;
            public Strip(string file, string clip, int frames, float t0, float t1, bool loop, float yaw)
            { File = file; Clip = clip; Frames = frames; T0 = t0; T1 = t1; Loop = loop; Yaw = yaw; }
        }
        // At most 16 frames per strip: 16 x 128 = 2048 px is the sprite importer's default max texture size
        // (a wider strip would be downscaled on import and every frame blurred).
        static readonly Strip[] Strips =
        {
            // Standing clips face the player; the march is seen from the side so it reads as walking.
            new Strip("soldier_idle.png",    "Footman_Idle",    12, 0f, 1f,         true,  FrontYaw),
            new Strip("soldier_walk.png",    "Footman_Walk",    12, 0f, 4f / 3f,    true,  SideYaw),
            new Strip("soldier_victory.png", "Footman_Victory", 16, 0f, 10f / 3f,   false, FrontYaw),
            new Strip("soldier_death.png",   "Footman_Death",   14, 0f, 1f,         false, FrontYaw),
        };
        const string IconFile = "soldier.png";

        const int Frame = 128;         // strip frame px
        const int Icon = 256;          // static icon px
        const int SS = 4;              // supersampling factor
        const float CamPitch = 24f;    // degrees looking down
        // Model faces +Z; 180 = facing the camera, 90 = profile facing screen right.
        const float FrontYaw = 180f, SideYaw = 125f;
        /// -troopYaw &lt;deg&gt; forces one angle for every strip (for trying looks); otherwise each strip uses its own.
        static float YawFor(Strip s) => float.TryParse(Arg("-troopYaw"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var y) ? y : s.Yaw;
        const float Pad = 0.02f;       // frame padding (fraction of the side), outline added on top
        const float OutlineFrac = 2.25f / 128f; // outline thickness as a fraction of the output side
        static readonly Color OutlineCol = new Color(0.10f, 0.06f, 0.04f, 1f);
        const float ShadowAlpha = 0.42f;

        // ---------- recolour (blue cloth -> vermilion) ----------
        static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);
        static readonly Color Verm = C(206, 44, 30);
        static readonly Color LacquerRed = C(120, 22, 16);
        static readonly Color LacquerMid = C(170, 36, 26);
        /// Dark plates of the helmet guard: "red" lacquer (default, reads best at ~40 px), "black" (as painted) or "steel".
        static string Guard => Arg("-troopGuard") ?? "red";
        /// Colours: "original" (the pack's blue footman, the default the user picked) or "red" (Chinese-army recolour).
        static string Palette => Arg("-troopColors") ?? "original";
        // inside face of the shield in the HP texture: centre (u, v) and radius inside the steel rim
        static readonly Vector3 ShieldBack = new Vector3(610f / 1024f, 1f - 480f / 1024f, 84f / 1024f);

        // ================= entry point =================

        [MenuItem("WarPoker/Render Troop Sprites")]
        public static void RenderAllMenu() => RenderAll();

        public static void RenderAll()
        {
            var missing = new List<string>();
            foreach (var p in new[] { Model, Albedo, GlossTex }.Concat(Strips.Select(s => AnimDir + s.Clip + ".fbx")))
                if (!File.Exists(p)) missing.Add(p);
            if (missing.Count > 0)
            {
                Debug.LogError("[TroopRenderer] The Asset Store pack \"Mini Legion Footman PBR HP Polyart\" is not in this project " +
                               "(it is git-ignored; import it from the Package Manager / My Assets to re-render). Nothing rendered. Missing:\n  " +
                               string.Join("\n  ", missing));
                return;
            }

            string outDir = Arg("-troopOut") ?? OutDir;
            Directory.CreateDirectory(outDir);
            WithStage(() =>
            {
                foreach (var (name, png) in RenderSoldier())
                {
                    File.WriteAllBytes(Path.Combine(outDir, name), png);
                    Debug.Log("[TroopRenderer] wrote " + Path.Combine(outDir, name));
                }
            });
            if (outDir.StartsWith("Assets")) AssetDatabase.Refresh();
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
        }

        // ================= stage (temp scene + quality settings, restored afterwards) =================

        static void WithStage(Action body)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var q = (QualitySettings.shadows, QualitySettings.shadowResolution, QualitySettings.shadowDistance,
                     QualitySettings.shadowCascades, QualitySettings.pixelLightCount, QualitySettings.shadowProjection);
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.shadowResolution = ShadowResolution.VeryHigh;
                QualitySettings.shadowDistance = 60f;
                QualitySettings.shadowCascades = 1;
                QualitySettings.pixelLightCount = 4;
                QualitySettings.shadowProjection = ShadowProjection.StableFit;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.72f, 0.72f, 0.76f);
                RenderSettings.ambientEquatorColor = new Color(0.58f, 0.55f, 0.52f);
                RenderSettings.ambientGroundColor = new Color(0.30f, 0.26f, 0.22f);
                RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
                RenderSettings.fog = false;
                body();
            }
            finally
            {
                QualitySettings.shadows = q.Item1; QualitySettings.shadowResolution = q.Item2;
                QualitySettings.shadowDistance = q.Item3; QualitySettings.shadowCascades = q.Item4;
                QualitySettings.pixelLightCount = q.Item5; QualitySettings.shadowProjection = q.Item6;
                if (setup != null && setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
                else if (!Application.isBatchMode) EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            }
        }

        // ================= the soldier =================

        /// One baked pose: world-space mesh + its contact-shadow ellipse on the ground.
        class Pose { public Mesh Mesh; public Vector3[] Verts; public Vector3 ShadowC; public float ShadowX, ShadowZ; }

        static List<(string, byte[])> RenderSoldier()
        {
            var temp = new List<Object>();
            var stage = new GameObject("troop_stage");
            var outputs = new List<(string, byte[])>();
            try
            {
                var pivot = new GameObject("pivot");
                pivot.transform.SetParent(stage.transform, false);
                pivot.transform.rotation = Quaternion.Euler(0, YawFor(Strips[0]), 0);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Model);
                var ch = (GameObject)Object.Instantiate(prefab, pivot.transform, false);
                ch.transform.localPosition = Vector3.zero; ch.transform.localRotation = Quaternion.identity;
                var anim = ch.GetComponent<Animator>();
                if (anim) { anim.applyRootMotion = false; anim.runtimeAnimatorController = null; }
                var smr = ch.GetComponentInChildren<SkinnedMeshRenderer>();
                smr.enabled = false;

                // ---- bake every frame of every strip (humanoid clips: SampleAnimation retargets via the Animator) ----
                var poses = new List<List<Pose>>();
                foreach (var s in Strips)
                {
                    pivot.transform.rotation = Quaternion.Euler(0, YawFor(s), 0);
                    var clip = LoadClip(AnimDir + s.Clip + ".fbx");
                    var list = new List<Pose>();
                    for (int i = 0; i < s.Frames; i++)
                    {
                        float u = s.Loop ? i / (float)s.Frames : i / (float)(s.Frames - 1);
                        float t = Mathf.Min(s.T0 + u * (s.T1 - s.T0), clip.length);
                        clip.SampleAnimation(ch, t);
                        clip.SampleAnimation(ch, t);
                        list.Add(new Pose { Mesh = Bake(smr, temp) });
                    }
                    poses.Add(list);
                    Debug.Log($"[TroopRenderer] {s.File}: clip {s.Clip} ({clip.length:F3}s @ {clip.frameRate}fps), {s.Frames} frames, t {s.T0:F3}..{s.T1:F3}s {(s.Loop ? "loop [t0,t1)" : "one-shot [t0,t1]")}");
                }
                foreach (var p in poses.SelectMany(l => l)) p.Verts = p.Mesh.vertices;

                // ground = lowest point of the idle rest frame; everything is measured from it
                float groundY = poses[0][0].Verts.Min(v => v.y);
                float height = poses[0][0].Verts.Max(v => v.y) - groundY;

                // contact shadow: footprint of the low part of the body (feet when standing, the whole body when lying)
                foreach (var p in poses.SelectMany(l => l)) Footprint(p, groundY, height);
                for (int k = 0; k < Strips.Length; k++)
                    if (Strips[k].Loop || Strips[k].Clip.EndsWith("Victory"))
                    {   // steady shadow for loops / the cheer: average footprint
                        var l = poses[k];
                        var c = new Vector3(l.Average(p => p.ShadowC.x), groundY + 0.005f, l.Average(p => p.ShadowC.z));
                        float sx = l.Average(p => p.ShadowX), sz = l.Average(p => p.ShadowZ);
                        foreach (var p in l) { p.ShadowC = c; p.ShadowX = sx; p.ShadowZ = sz; }
                    }

                // ---- shared framing for all strips ----
                var camRot = Quaternion.Euler(CamPitch, 0, 0);
                var right = camRot * Vector3.right; var up = camRot * Vector3.up;
                var foot = new Vector3(0, groundY, 0);
                float hw = 0, y0 = float.MaxValue, y1 = float.MinValue;
                foreach (var p in poses.SelectMany(l => l))
                    foreach (var v in p.Verts.Concat(ShadowPts(p)))
                    {
                        float x = Vector3.Dot(v - foot, right), y = Vector3.Dot(v - foot, up);
                        hw = Mathf.Max(hw, Mathf.Abs(x)); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
                    }
                float margin = Pad + (OutlineFrac * Frame + 1f) / Frame;
                float half = Mathf.Max(hw, (y1 - y0) / 2f);
                float ortho = half / (1f - 2f * margin);
                var center = foot + up * ((y0 + y1) / 2f);
                float footPxY = Frame / 2f - (y0 + y1) / 2f / ortho * (Frame / 2f); // from the bottom
                Debug.Log($"[TroopRenderer] shared framing: ortho {ortho:F3} ({Frame / (2 * ortho):F1} px per unit at {Frame} px); " +
                          $"ground point under the figure at x={Frame / 2f:F1}px, y={Frame - footPxY:F1}px from the top");

                var rig = new Rig(stage.transform, MakeMaterial(temp), temp);
                for (int k = 0; k < Strips.Length; k++)
                {
                    var l = poses[k];
                    var strip = new Texture2D(Frame * l.Count, Frame, TextureFormat.RGBA32, false); temp.Add(strip);
                    for (int i = 0; i < l.Count; i++)
                        strip.SetPixels32(i * Frame, 0, Frame, Frame, rig.Shoot(l[i], center, ortho, Frame));
                    strip.Apply();
                    outputs.Add((Strips[k].File, strip.EncodeToPNG()));
                }

                // ---- static icon: idle frame 0 with the SAME framing, at 2x resolution (swapping icon <-> strips never jumps) ----
                {
                    var tex = new Texture2D(Icon, Icon, TextureFormat.RGBA32, false); temp.Add(tex);
                    tex.SetPixels32(rig.Shoot(poses[0][0], center, ortho, Icon)); tex.Apply();
                    outputs.Add((IconFile, tex.EncodeToPNG()));
                }
                return outputs;
            }
            finally
            {
                Object.DestroyImmediate(stage);
                foreach (var o in temp) if (o) Object.DestroyImmediate(o);
            }
        }

        static AnimationClip LoadClip(string fbx) =>
            AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));

        static void Footprint(Pose p, float groundY, float height)
        {
            var low = p.Verts.Where(v => v.y < groundY + height * 0.3f).ToList();
            if (low.Count < 8) low = p.Verts.ToList();
            float x0 = low.Min(v => v.x), x1 = low.Max(v => v.x), z0 = low.Min(v => v.z), z1 = low.Max(v => v.z);
            p.ShadowC = new Vector3((x0 + x1) / 2, groundY + 0.005f, (z0 + z1) / 2);
            p.ShadowX = Mathf.Max(height * 0.75f, (x1 - x0) * 1.3f);
            p.ShadowZ = Mathf.Max(height * 0.55f, (z1 - z0) * 1.3f);
        }

        static IEnumerable<Vector3> ShadowPts(Pose p)
        {   // visible extent of the blob (its alpha fades out well before the quad edge)
            yield return p.ShadowC + new Vector3(p.ShadowX * .38f, 0, 0); yield return p.ShadowC - new Vector3(p.ShadowX * .38f, 0, 0);
            yield return p.ShadowC + new Vector3(0, 0, p.ShadowZ * .38f); yield return p.ShadowC - new Vector3(0, 0, p.ShadowZ * .38f);
        }

        /// CPU-skins the current pose into a world-space static mesh (reliable offscreen, exact bounds).
        static Mesh Bake(SkinnedMeshRenderer smr, List<Object> temp)
        {
            var src = smr.sharedMesh;
            var bones = smr.bones; var bp = src.bindposes; var bw = src.boneWeights;
            var sv = src.vertices; var sn = src.normals;
            var wv = new Vector3[sv.Length]; var wn = new Vector3[sv.Length];
            var mats = new Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++) mats[i] = bones[i].localToWorldMatrix * bp[i];
            for (int i = 0; i < sv.Length; i++)
            {
                var w = bw[i]; Vector3 p = Vector3.zero, nn = Vector3.zero;
                if (w.weight0 > 0) { p += w.weight0 * mats[w.boneIndex0].MultiplyPoint3x4(sv[i]); nn += w.weight0 * mats[w.boneIndex0].MultiplyVector(sn[i]); }
                if (w.weight1 > 0) { p += w.weight1 * mats[w.boneIndex1].MultiplyPoint3x4(sv[i]); nn += w.weight1 * mats[w.boneIndex1].MultiplyVector(sn[i]); }
                if (w.weight2 > 0) { p += w.weight2 * mats[w.boneIndex2].MultiplyPoint3x4(sv[i]); nn += w.weight2 * mats[w.boneIndex2].MultiplyVector(sn[i]); }
                if (w.weight3 > 0) { p += w.weight3 * mats[w.boneIndex3].MultiplyPoint3x4(sv[i]); nn += w.weight3 * mats[w.boneIndex3].MultiplyVector(sn[i]); }
                float ws = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                wv[i] = ws > 0 ? p / ws : p; wn[i] = nn.normalized;
            }
            var mesh = new Mesh { indexFormat = src.indexFormat }; temp.Add(mesh);
            mesh.vertices = wv; mesh.normals = wn; mesh.uv = src.uv;
            mesh.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++) mesh.SetTriangles(src.GetTriangles(s), s);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Texture2D ReadPng(string path, List<Object> temp, bool linear = false)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear); temp.Add(t);
            t.LoadImage(File.ReadAllBytes(path));
            return t;
        }

        /// HP albedo as painted (or, with -troopColors red, blue cloth hue-shifted to vermilion); PBR smoothness for small glints on metal / gold.
        static Material MakeMaterial(List<Object> temp)
        {
            var alb = ReadPng(Albedo, temp);
            var px = alb.GetPixels32();
            Color.RGBToHSV(Verm, out float vh, out float vs, out float vv);
            for (int i = 0; Palette == "red" && i < px.Length; i++)
            {
                Color c = px[i];
                Color.RGBToHSV(c, out float h, out float s, out float v);
                float hd = h * 360f;
                // blue band ~190..265 deg, soft edges; needs some saturation (greys/steel stay)
                float wHue = Mathf.Clamp01((hd - 185f) / 15f) * Mathf.Clamp01((275f - hd) / 15f);
                float wSat = Mathf.Clamp01((s - 0.18f) / 0.17f);
                float w = wHue * wSat;
                // inside face of the round shield (dark steel disc) -> deep red lacquer, steel rim kept
                int x = i % alb.width, y = i / alb.width;
                float du = (x + .5f) / alb.width - ShieldBack.x, dv = (y + .5f) / alb.height - ShieldBack.y;
                float wShield = 1f - Mathf.SmoothStep(0f, 1f, (Mathf.Sqrt(du * du + dv * dv) - ShieldBack.z) / 0.01f);
                if (wShield > 0)
                {
                    float lum = 0.3f * c.r + 0.59f * c.g + 0.11f * c.b;
                    var lac = Color.Lerp(LacquerRed, Verm, Mathf.Clamp01((lum - 0.15f) / 0.25f)) * Mathf.Lerp(0.75f, 1.15f, Mathf.Clamp01(lum / 0.4f));
                    var o2 = Color.Lerp(c, lac, wShield); o2.a = 1;
                    px[i] = o2;
                    continue;
                }
                // dark plates of the helmet's face / neck guard (left column of the texture, not the steel visor)
                float gu = (x + .5f) / alb.width, gv = 1f - (y + .5f) / alb.height;
                if (Guard != "black" && gu < 0.13f && gv > 0.38f && s < 0.25f && v < 0.5f)
                {
                    Color g2 = Guard == "steel" ? new Color(Mathf.Clamp01(c.r * 1.9f + 0.06f), Mathf.Clamp01(c.g * 1.9f + 0.06f), Mathf.Clamp01(c.b * 1.9f + 0.08f))
                                               : Color.Lerp(LacquerRed, LacquerMid, Mathf.Clamp01((v - 0.12f) / 0.3f)) * Mathf.Lerp(0.85f, 1.12f, Mathf.Clamp01(v / 0.45f));
                    g2.a = 1; px[i] = g2; continue;
                }
                if (w <= 0) continue;
                // value: painted blue is bright; vermilion reads a touch darker, keep the painted shading
                float nv = Mathf.Clamp01(v * 0.92f);
                float ns = Mathf.Clamp01(Mathf.Lerp(vs, s, 0.35f));
                var red = Color.HSVToRGB(vh, ns, nv);
                // deep navy shadows -> deep crimson rather than near black
                var o = Color.Lerp(c, red, w); o.a = 1;
                px[i] = o;
            }
            var pal = new Texture2D(alb.width, alb.height, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            temp.Add(pal);
            pal.SetPixels32(px); pal.Apply(true);
            var dump = Arg("-troopDumpAlbedo");
            if (dump != null) File.WriteAllBytes(dump, pal.EncodeToPNG());

            var gsrc = ReadPng(GlossTex, temp, true).GetPixels32();
            var gp = new Color32[gsrc.Length];
            for (int i = 0; i < gp.Length; i++)
                gp[i] = new Color32((byte)(gsrc[i].r * 0.25f), 0, 0, (byte)(gsrc[i].a * 0.75f));
            var gloss = new Texture2D(alb.width, alb.height, TextureFormat.RGBA32, true, true) { wrapMode = TextureWrapMode.Clamp };
            temp.Add(gloss);
            gloss.SetPixels32(gp); gloss.Apply(true);

            var m = new Material(Shader.Find("Standard")); temp.Add(m);
            m.mainTexture = pal;
            m.SetTexture("_MetallicGlossMap", gloss);
            m.EnableKeyword("_METALLICGLOSSMAP");
            m.SetFloat("_GlossMapScale", 1f);
            m.SetFloat("_SpecularHighlights", 1f);
            m.SetFloat("_GlossyReflections", 0f);
            m.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            return m;
        }

        // ================= camera, lights, post =================

        class Rig
        {
            readonly Camera cam; readonly MeshFilter fig; readonly MeshRenderer figR;
            readonly GameObject shadow; readonly MeshRenderer shadowR; readonly List<Object> temp;

            public Rig(Transform parent, Material mat, List<Object> temp)
            {
                this.temp = temp;
                var f = new GameObject("figure"); f.transform.SetParent(parent, false);
                fig = f.AddComponent<MeshFilter>(); figR = f.AddComponent<MeshRenderer>(); figR.sharedMaterial = mat;

                shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Object.DestroyImmediate(shadow.GetComponent<Collider>());
                shadow.transform.SetParent(parent, false);
                shadow.transform.rotation = Quaternion.Euler(90, 0, 0);
                var smat = new Material(Shader.Find("Sprites/Default")) { mainTexture = BlobTexture(temp) }; temp.Add(smat);
                smat.color = new Color(0, 0, 0, ShadowAlpha);
                shadowR = shadow.GetComponent<MeshRenderer>(); shadowR.sharedMaterial = smat;
                shadowR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                shadowR.receiveShadows = false;

                var camGo = new GameObject("troop_cam"); camGo.transform.SetParent(parent, false);
                cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.nearClipPlane = 1f; cam.farClipPlane = 140f;
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.allowMSAA = false; cam.allowHDR = false;
                cam.renderingPath = RenderingPath.Forward;
                cam.enabled = false;

                // lights: warm key upper-left-front, cool fill/rim from right-back
                var key = new GameObject("key").AddComponent<Light>(); key.transform.SetParent(parent, false);
                key.type = LightType.Directional; key.color = new Color(1f, 0.95f, 0.86f); key.intensity = 1.2f;
                key.transform.rotation = Quaternion.Euler(42, 32, 0);
                key.shadows = LightShadows.Soft; key.shadowStrength = 0.55f; key.shadowBias = 0.03f; key.shadowNormalBias = 0.25f;
                var fill = new GameObject("fill").AddComponent<Light>(); fill.transform.SetParent(parent, false);
                fill.type = LightType.Directional; fill.color = new Color(0.78f, 0.84f, 1f); fill.intensity = 0.7f;
                fill.transform.rotation = Quaternion.Euler(18, -125, 0);
                fill.shadows = LightShadows.None;
            }

            public Color32[] Shoot(Pose p, Vector3 center, float ortho, int size)
            {
                fig.sharedMesh = p.Mesh;
                shadow.transform.position = p.ShadowC;
                shadow.transform.localScale = new Vector3(p.ShadowX, p.ShadowZ, 1);
                var camRot = Quaternion.Euler(CamPitch, 0, 0);
                cam.orthographicSize = ortho;
                cam.transform.SetPositionAndRotation(center - camRot * Vector3.forward * 60f, camRot);

                int n = size * SS;
                var rt = RenderTexture.GetTemporary(n, n, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
                cam.targetTexture = rt;
                try
                {
                    figR.enabled = false; shadowR.enabled = true;
                    var shadowPx = Grab(rt, n);
                    figR.enabled = true; shadowR.enabled = false;
                    var figPx = Grab(rt, n);
                    return Compose(figPx, shadowPx, n, size);
                }
                finally
                {
                    cam.targetTexture = null;
                    RenderTexture.ReleaseTemporary(rt);
                }
            }

            Color[] Grab(RenderTexture rt, int n)
            {
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, n, n), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                var px = tex.GetPixels();
                Object.DestroyImmediate(tex);
                return px;
            }
        }

        static Texture2D BlobTexture(List<Object> temp)
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            temp.Add(t);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.SmoothStep(0.25f, 1f, d));
                    t.SetPixel(x, y, new Color(1, 1, 1, a));
                }
            t.Apply();
            return t;
        }

        /// figure (opaque, alpha = coverage) + dark outline + contact shadow, then SSxSS box downsample
        /// in premultiplied space and un-premultiply (no dark/white fringes).
        static Color32[] Compose(Color[] fig, Color[] shadow, int n, int size)
        {
            float rad = OutlineFrac * size * SS;
            int ri = Mathf.CeilToInt(rad + 1);
            var offs = new List<(int dx, int dy, float d)>();
            for (int dy = -ri; dy <= ri; dy++)
                for (int dx = -ri; dx <= ri; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d <= rad + 1) offs.Add((dx, dy, d));
                }
            offs.Sort((a, b) => a.d.CompareTo(b.d));

            var solid = new bool[n * n];
            for (int i = 0; i < n * n; i++) solid[i] = fig[i].a > 0.5f;

            // integral image of the solid mask: skip pixels with nothing solid within the outline radius
            var integ = new int[(n + 1) * (n + 1)];
            for (int y = 0; y < n; y++)
            {
                int row = 0;
                for (int x = 0; x < n; x++)
                {
                    row += solid[y * n + x] ? 1 : 0;
                    integ[(y + 1) * (n + 1) + x + 1] = integ[y * (n + 1) + x + 1] + row;
                }
            }
            int Count(int x0, int y0, int x1, int y1)
            {
                x0 = Mathf.Clamp(x0, 0, n); y0 = Mathf.Clamp(y0, 0, n); x1 = Mathf.Clamp(x1, 0, n); y1 = Mathf.Clamp(y1, 0, n);
                return integ[y1 * (n + 1) + x1] - integ[y0 * (n + 1) + x1] - integ[y1 * (n + 1) + x0] + integ[y0 * (n + 1) + x0];
            }

            var outA = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (solid[i]) { outA[i] = 1; continue; }
                    if (Count(x - ri, y - ri, x + ri + 1, y + ri + 1) == 0) continue;
                    foreach (var o in offs)
                    {
                        int xx = x + o.dx, yy = y + o.dy;
                        if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                        if (solid[yy * n + xx]) { outA[i] = Mathf.Clamp01(rad + 0.5f - o.d); break; }
                    }
                }

            var px = new Color32[size * size];
            float inv = 1f / (SS * SS);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            int i = (y * SS + sy) * n + x * SS + sx;
                            // premultiplied composite: fig over outline over shadow
                            var f = fig[i]; // background is (0,0,0,0) so this is already premultiplied
                            float oa = outA[i] * OutlineCol.a;
                            var s = shadow[i];
                            float ur = OutlineCol.r * oa + s.r * (1 - oa), ug = OutlineCol.g * oa + s.g * (1 - oa),
                                  ub = OutlineCol.b * oa + s.b * (1 - oa), ua = oa + s.a * (1 - oa);
                            float fa = Mathf.Clamp01(f.a);
                            r += f.r + ur * (1 - fa); g += f.g + ug * (1 - fa); b += f.b + ub * (1 - fa); a += fa + ua * (1 - fa);
                        }
                    r *= inv; g *= inv; b *= inv; a *= inv;
                    if (a > 1e-4f) { r /= a; g /= a; b /= a; } else { r = g = b = 0; a = 0; }
                    px[y * size + x] = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(a));
                }
            return px;
        }
    }
}
