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
    /// Renders the troop sprite Resources/Troops/soldier.png from the free "Voxel Knights" pack
    /// (The Voxel Store, itch.io: usable in any game, credit optional). Sources live in
    /// Assets/_Project/ArtSource/Voxel/. Knight model in the idle pose holding a spear upright,
    /// palette recolored to vermilion cloth / black-lacquer armor / gold trim, 3/4 view from
    /// slightly above, key + fill light, 4x supersampled, dark outline and soft contact shadow.
    /// Batch: Unity -batchmode -quit -projectPath client -executeMethod WarPoker.EditorTools.TroopRenderer.RenderAll
    /// (optional: -troopOut &lt;dir&gt; to write elsewhere). Needs a graphics device (no -nographics).
    /// </summary>
    public static class TroopRenderer
    {
        const string K = "Assets/_Project/ArtSource/Voxel/Knights/";
        const string Character = K + "FBX/Characters/TVS_VoxelKnights_Knight.fbx";
        const string Palette = K + "Textures/Characters/TVS_VoxelKnights_Knight_Texture.png";
        const string Anim = K + "Animations/Humans/Human_Idle_Anim.fbx";
        const string Spear = K + "FBX/Props/TVS_VoxelKnights_Spear.fbx";
        const string SpearPalette = K + "Textures/Props/TVS_VoxelKnights_Spear_Texture.png";
        const string OutDir = "Assets/_Project/Resources/Troops";
        const string OutName = "soldier.png";

        const int Size = 256;          // output px
        const int SS = 4;              // supersampling factor
        const float CamPitch = 24f;    // degrees looking down
        const float Yaw = 160f;        // 180 = facing the camera, lower = turned toward screen right
        const float Pad = 0.035f;      // frame padding (fraction of the side), outline added on top
        const float OutlinePx = 2.5f;  // outline thickness at output resolution
        static readonly Color OutlineCol = new Color(0.10f, 0.06f, 0.04f, 1f);
        const float ShadowAlpha = 0.42f;

        // Spear: hand sits 0.8 below the spear origin; fattened so it survives at icon size.
        const float SpearGrip = -0.8f, SpearThick = 1.9f;

        // ---------- palette (256x1 strips, index = u * 256) ----------
        // Rig palette layout: 0-2 darks (eyes/hair/boots), 4-6 steel, 7 skin, 8-10 gold, 34-35 team cloth.
        static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);
        static readonly Color32 Verm = C(206, 44, 30), VermDark = C(160, 28, 22);
        static readonly Color32 Gold = C(232, 182, 42), GoldDark = C(186, 136, 24);
        static readonly Color32 Lacq = C(48, 46, 54), LacqMid = C(34, 33, 39), LacqLight = C(78, 76, 86);

        static readonly Dictionary<int, Color32> SoldierRecolor = new Dictionary<int, Color32>
        { { 34, VermDark }, { 35, Verm }, { 4, LacqLight }, { 5, LacqMid }, { 6, Lacq }, { 8, Gold }, { 9, GoldDark } };
        static readonly Dictionary<int, Color32> SpearRecolor = new Dictionary<int, Color32>
        { { 34, VermDark }, { 35, Verm }, { 8, Gold } };

        // smoothness per palette index (lacquer + gold get a specular glint)
        static readonly Dictionary<int, float> Gloss = new Dictionary<int, float>
        { { 4, .55f }, { 5, .5f }, { 6, .5f }, { 8, .6f }, { 9, .55f }, { 10, .6f } };

        // ================= entry point =================

        [MenuItem("WarPoker/Render Troop Sprites")]
        public static void RenderAllMenu() => RenderAll();

        public static void RenderAll()
        {
            string outDir = Arg("-troopOut") ?? OutDir;
            Directory.CreateDirectory(outDir);
            WithStage(() =>
            {
                File.WriteAllBytes(Path.Combine(outDir, OutName), RenderSoldier());
                Debug.Log("[TroopRenderer] wrote " + Path.Combine(outDir, OutName));
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
                RenderSettings.ambientSkyColor = new Color(0.62f, 0.62f, 0.66f);
                RenderSettings.ambientEquatorColor = new Color(0.50f, 0.47f, 0.44f);
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

        static byte[] RenderSoldier()
        {
            var temp = new List<Object>();
            var root = new GameObject("troop_soldier");
            try
            {
                var ch = Spawn(Character, root.transform);
                Sample(ch, Anim, 0f);

                // spear upright in the right hand (attached in unit space, before baking)
                var hand = Find(ch.transform, "Hand.Right");
                var handEnd = Find(ch.transform, "Hand.Right_end");
                var pivot = new GameObject("spear_pivot");
                pivot.transform.SetParent(root.transform, false);
                pivot.transform.position = (hand.position + handEnd.position) * 0.5f;
                pivot.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                pivot.transform.localScale = new Vector3(SpearThick, SpearThick, 1f);
                var spear = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Spear));
                var lr = spear.transform.localRotation; var ls = spear.transform.localScale;
                spear.transform.SetParent(pivot.transform, false);
                spear.transform.localRotation = lr; spear.transform.localScale = ls;
                spear.transform.localPosition = new Vector3(0, 0, -SpearGrip);
                var spearMat = MakeMaterial(SpearPalette, SpearRecolor, temp);
                foreach (var r in spear.GetComponentsInChildren<Renderer>()) r.sharedMaterial = spearMat;

                var renderers = Bake(ch, MakeMaterial(Palette, SoldierRecolor, temp), root.transform, temp);
                renderers.AddRange(pivot.GetComponentsInChildren<Renderer>());

                root.transform.rotation = Quaternion.Euler(0, Yaw, 0);
                return Shoot(root, renderers, temp);
            }
            finally
            {
                Object.DestroyImmediate(root);
                foreach (var o in temp) if (o) Object.DestroyImmediate(o);
            }
        }

        static GameObject Spawn(string fbx, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
            if (!prefab) throw new Exception("missing model " + fbx);
            var go = (GameObject)Object.Instantiate(prefab, parent, false);
            go.transform.localPosition = Vector3.zero; go.transform.localRotation = Quaternion.identity;
            return go;
        }

        static void Sample(GameObject go, string animFbx, float t01)
        {
            if (animFbx == null) return;
            var clip = AssetDatabase.LoadAllAssetsAtPath(animFbx).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview"));
            clip.SampleAnimation(go, Mathf.Clamp01(t01) * clip.length);
        }

        static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = Find(c, name); if (f) return f; }
            return null;
        }

        /// Replaces skinned renderers with static meshes skinned on the CPU into world space
        /// (reliable offscreen, exact bounds, no BakeMesh scale quirks with the 100x armature).
        static List<Renderer> Bake(GameObject go, Material mat, Transform parent, List<Object> temp)
        {
            var list = new List<Renderer>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var src = smr.sharedMesh;
                var bones = smr.bones; var bp = src.bindposes; var bw = src.boneWeights;
                var sv = src.vertices; var sn = src.normals;
                var wv = new Vector3[sv.Length]; var wn = new Vector3[sv.Length];
                var toParent = parent.worldToLocalMatrix;
                if (bones == null || bones.Length == 0 || bw.Length != sv.Length)
                {
                    var m = toParent * smr.transform.localToWorldMatrix;
                    for (int i = 0; i < sv.Length; i++) { wv[i] = m.MultiplyPoint3x4(sv[i]); wn[i] = m.MultiplyVector(sn[i]).normalized; }
                }
                else
                {
                    var mats = new Matrix4x4[bones.Length];
                    for (int i = 0; i < bones.Length; i++) mats[i] = toParent * bones[i].localToWorldMatrix * bp[i];
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
                }
                var mesh = new Mesh { indexFormat = src.indexFormat }; temp.Add(mesh);
                mesh.vertices = wv; mesh.normals = wn; mesh.uv = src.uv;
                mesh.subMeshCount = src.subMeshCount;
                for (int s = 0; s < src.subMeshCount; s++) mesh.SetTriangles(src.GetTriangles(s), s);
                mesh.RecalculateBounds();
                var b = new GameObject(smr.name + "_baked");
                b.transform.SetParent(parent, false);
                b.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = b.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                list.Add(mr);
                smr.enabled = false;
            }
            return list;
        }

        static Material MakeMaterial(string palettePath, Dictionary<int, Color32> recolor, List<Object> temp)
        {
            var src = new Texture2D(2, 2);
            src.LoadImage(File.ReadAllBytes(palettePath)); temp.Add(src);
            var pal = new Texture2D(256, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var gloss = new Texture2D(256, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            temp.Add(pal); temp.Add(gloss);
            for (int i = 0; i < 256; i++)
            {
                Color32 c = src.GetPixel(i * src.width / 256, 0);
                if (recolor != null && recolor.TryGetValue(i, out var rc)) c = rc;
                c.a = 255;
                pal.SetPixel(i, 0, c);
                gloss.SetPixel(i, 0, new Color(0, 0, 0, Gloss.TryGetValue(i, out var g) ? g : 0.12f));
            }
            pal.Apply(); gloss.Apply();
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

        static IEnumerable<Vector3> WorldVerts(Renderer r)
        {
            var mf = r.GetComponent<MeshFilter>();
            var m = r.transform.localToWorldMatrix;
            foreach (var v in mf.sharedMesh.vertices) yield return m.MultiplyPoint3x4(v);
        }

        static byte[] Shoot(GameObject root, List<Renderer> figure, List<Object> temp)
        {
            var camRot = Quaternion.Euler(CamPitch, 0, 0);
            var right = camRot * Vector3.right; var up = camRot * Vector3.up; var fwd = camRot * Vector3.forward;

            var verts = figure.SelectMany(WorldVerts).ToList();
            float groundY = verts.Min(v => v.y);
            float minX = verts.Min(v => v.x), maxX = verts.Max(v => v.x), minZ = verts.Min(v => v.z), maxZ = verts.Max(v => v.z);

            // contact shadow: soft ellipse on the ground under the figure footprint (low verts only)
            var low = verts.Where(v => v.y < groundY + 0.6f).ToList();
            var fc = new Vector3((low.Min(v => v.x) + low.Max(v => v.x)) / 2, groundY + 0.01f, (low.Min(v => v.z) + low.Max(v => v.z)) / 2);
            float sx = Mathf.Max(1.6f, (low.Max(v => v.x) - low.Min(v => v.x)) * 1.25f);
            float sz = Mathf.Max(1.2f, (low.Max(v => v.z) - low.Min(v => v.z)) * 1.25f);
            var shadow = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(shadow.GetComponent<Collider>());
            shadow.transform.SetParent(root.transform.parent, false);
            shadow.transform.position = fc;
            shadow.transform.rotation = Quaternion.Euler(90, 0, 0);
            shadow.transform.localScale = new Vector3(sx, sz, 1);
            var smat = new Material(Shader.Find("Sprites/Default")) { mainTexture = BlobTexture(temp) }; temp.Add(smat);
            smat.color = new Color(0, 0, 0, ShadowAlpha);
            var sr = shadow.GetComponent<MeshRenderer>(); sr.sharedMaterial = smat;
            sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            temp.Add(shadow);

            // ---- framing (orthographic, fit figure + shadow) ----
            var pts = new List<Vector3>(verts);
            pts.Add(fc + new Vector3(sx * .38f, 0, 0)); pts.Add(fc - new Vector3(sx * .38f, 0, 0));
            pts.Add(fc + new Vector3(0, 0, sz * .38f)); pts.Add(fc - new Vector3(0, 0, sz * .38f));
            float x0 = pts.Min(p => Vector3.Dot(p, right)), x1 = pts.Max(p => Vector3.Dot(p, right));
            float y0 = pts.Min(p => Vector3.Dot(p, up)), y1 = pts.Max(p => Vector3.Dot(p, up));
            float half = Mathf.Max(x1 - x0, y1 - y0) / 2f;
            float margin = Pad + (OutlinePx + 1f) / Size;
            float ortho = half / (1f - 2f * margin);
            var center = right * (x0 + x1) / 2 + up * (y0 + y1) / 2;

            var camGo = new GameObject("troop_cam"); temp.Add(camGo);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true; cam.orthographicSize = ortho;
            cam.transform.SetPositionAndRotation(center - fwd * 60f, camRot);
            cam.nearClipPlane = 1f; cam.farClipPlane = 140f;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.allowMSAA = false; cam.allowHDR = false;
            cam.renderingPath = RenderingPath.Forward;

            // ---- lights: warm key upper-left-front, cool fill/rim from right-back ----
            var key = new GameObject("key").AddComponent<Light>(); temp.Add(key.gameObject);
            key.type = LightType.Directional; key.color = new Color(1f, 0.95f, 0.86f); key.intensity = 1.05f;
            key.transform.rotation = Quaternion.Euler(42, 32, 0);
            key.shadows = LightShadows.Soft; key.shadowStrength = 0.55f; key.shadowBias = 0.03f; key.shadowNormalBias = 0.25f;
            var fill = new GameObject("fill").AddComponent<Light>(); temp.Add(fill.gameObject);
            fill.type = LightType.Directional; fill.color = new Color(0.78f, 0.84f, 1f); fill.intensity = 0.45f;
            fill.transform.rotation = Quaternion.Euler(18, -125, 0);
            fill.shadows = LightShadows.None;

            int N = Size * SS;
            var rt = RenderTexture.GetTemporary(N, N, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
            cam.targetTexture = rt;
            try
            {
                // pass 1: shadow only
                foreach (var r in figure) r.enabled = false;
                var shadowPx = Grab(cam, rt, N, temp);
                foreach (var r in figure) r.enabled = true;
                sr.enabled = false;
                var figPx = Grab(cam, rt, N, temp);
                return Compose(figPx, shadowPx, N);
            }
            finally
            {
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
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

        static Color[] Grab(Camera cam, RenderTexture rt, int n, List<Object> temp)
        {
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false); temp.Add(tex);
            tex.ReadPixels(new Rect(0, 0, n, n), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            return tex.GetPixels();
        }

        /// figure (opaque, alpha = coverage) + dark outline + contact shadow, then 4x box downsample
        /// in premultiplied space and un-premultiply (no dark/white fringes).
        static byte[] Compose(Color[] fig, Color[] shadow, int n)
        {
            float rad = OutlinePx * SS;
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

            var outA = new float[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int i = y * n + x;
                    if (solid[i]) { outA[i] = 1; continue; }
                    foreach (var o in offs)
                    {
                        int xx = x + o.dx, yy = y + o.dy;
                        if (xx < 0 || yy < 0 || xx >= n || yy >= n) continue;
                        if (solid[yy * n + xx]) { outA[i] = Mathf.Clamp01(rad + 0.5f - o.d); break; }
                    }
                }

            // premultiplied composite: fig over outline over shadow
            var pm = new Color[n * n];
            for (int i = 0; i < n * n; i++)
            {
                var f = fig[i]; // background is (0,0,0,0) so this is already premultiplied
                float oa = outA[i] * OutlineCol.a;
                var o = new Color(OutlineCol.r * oa, OutlineCol.g * oa, OutlineCol.b * oa, oa);
                var s = shadow[i];
                var under = new Color(o.r + s.r * (1 - o.a), o.g + s.g * (1 - o.a), o.b + s.b * (1 - o.a), o.a + s.a * (1 - o.a));
                float fa = Mathf.Clamp01(f.a);
                pm[i] = new Color(f.r + under.r * (1 - fa), f.g + under.g * (1 - fa), f.b + under.b * (1 - fa), fa + under.a * (1 - fa));
            }

            var outTex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var px = new Color32[Size * Size];
            float inv = 1f / (SS * SS);
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float r = 0, g = 0, b = 0, a = 0;
                    for (int sy = 0; sy < SS; sy++)
                        for (int sx = 0; sx < SS; sx++)
                        {
                            var c = pm[(y * SS + sy) * n + x * SS + sx];
                            r += c.r; g += c.g; b += c.b; a += c.a;
                        }
                    r *= inv; g *= inv; b *= inv; a *= inv;
                    if (a > 1e-4f) { r /= a; g /= a; b /= a; } else { r = g = b = 0; a = 0; }
                    px[y * Size + x] = new Color(Mathf.Clamp01(r), Mathf.Clamp01(g), Mathf.Clamp01(b), Mathf.Clamp01(a));
                }
            outTex.SetPixels32(px);
            outTex.Apply();
            var bytes = outTex.EncodeToPNG();
            Object.DestroyImmediate(outTex);
            return bytes;
        }
    }
}
