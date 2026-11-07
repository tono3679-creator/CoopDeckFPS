using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Animation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Surface look of the Fox Gunner: a per-texel surface mask (fur / face / eye / cloth / leather / metal /
    /// glass) built from the Meshy texture colours and the skin weights, the materials built on it, and a
    /// before/after capture rig with fixed lighting. Mesh, rig, UVs and colours are untouched.
    ///
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoopDeck.EditorTools.FoxGunnerLookFactory.RunBatch
    ///   (captures every variant into Screenshots/FoxLook_*.png)
    /// </summary>
    public static class FoxGunnerLookFactory
    {
        public const string TextureFolder = FoxGunnerAssetFactory.ArtFolder + "/Textures";
        public const string MaterialFolder = FoxGunnerAssetFactory.ArtFolder + "/Materials";
        public const string SurfaceMaskPath = TextureFolder + "/T_FoxGunner_SurfaceMask.png";
        public const string MetallicSmoothnessPath = TextureFolder + "/T_FoxGunner_MetallicSmoothness.png";
        public const string BackupMaterialPath = MaterialFolder + "/Backup/M_FoxGunner_Toon_Original.mat";
        public const string LitMaterialPath = MaterialFolder + "/M_FoxGunner_Lit.mat";
        public const string SoftToonMaterialPath = MaterialFolder + "/M_FoxGunner_SoftToon.mat";
        public const string OutlineMaterialPath = MaterialFolder + "/M_FoxGunner_Outline.mat";
        public const string FirstPersonMaterialPath = MaterialFolder + "/M_FoxGunner_SoftToon_FirstPerson.mat";
        public const string NormalMapPath = TextureFolder + "/T_FoxGunner_Normal.png"; // refine_character.py normalmap
        public const string SoftToonShaderPath = GamePaths.Root + "/Art/Characters/Shaders/SG_Character_SoftToon.shadergraph";
        public const string OutlineShaderPath = GamePaths.Root + "/Art/Characters/Shaders/CH_Outline.shader";
        const int MaskSize = 2048; // half the 4096 base colour: crisp region edges

        public enum Region { Fur, Face, Eye, Cloth, Leather, Metal, Glass }

        /// <summary>Surface parameters per region: softness, smoothness, metallic, warmth (the mask's RGBA).</summary>
        public static readonly IReadOnlyDictionary<Region, Vector4> Presets = new Dictionary<Region, Vector4>
        {
            { Region.Fur, new Vector4(0.85f, 0.12f, 0f, 0.55f) },     // matte, soft wrapped gradient, warm
            { Region.Face, new Vector4(1f, 0.16f, 0f, 0.8f) },        // softest gradient, warmest terminator
            { Region.Eye, new Vector4(0.5f, 0.75f, 0f, 0f) },         // small wet highlight
            { Region.Cloth, new Vector4(0.6f, 0.06f, 0f, 0.15f) },    // scarf, vest, pants: no gloss
            { Region.Leather, new Vector4(0.3f, 0.42f, 0f, 0f) },     // boots, gloves, belts: soft sheen
            { Region.Metal, new Vector4(0.15f, 0.62f, 0.85f, 0f) },   // buckles, toe caps, goggle frames
            { Region.Glass, new Vector4(0.2f, 0.85f, 0f, 0f) },       // goggle lenses
        };

        static readonly IReadOnlyDictionary<Region, Color> DebugColors = new Dictionary<Region, Color>
        {
            { Region.Fur, new Color(1f, 0.5f, 0.05f) }, { Region.Face, new Color(1f, 0.72f, 0.8f) },
            { Region.Eye, new Color(0f, 1f, 1f) }, { Region.Cloth, new Color(0.15f, 0.35f, 1f) },
            { Region.Leather, new Color(0.45f, 0.25f, 0.1f) }, { Region.Metal, new Color(0.85f, 0.85f, 0.9f) },
            { Region.Glass, new Color(0.1f, 0.9f, 0.2f) },
        };

        [MenuItem("CoopDeck/Art/Fox Gunner Look: Build Surface Mask And Materials", priority = 23)]
        public static void BuildFromMenu() => EnsureAll(rebuildMask: true);

        [MenuItem("CoopDeck/Art/Fox Gunner Look: Capture Comparison", priority = 24)]
        public static void CaptureFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                CaptureComparison();
            }
        }

        public static void RunBatch()
        {
            try
            {
                EnsureAll(rebuildMask: Array.IndexOf(Environment.GetCommandLineArgs(), "-coopdeckRebuildMask") >= 0);
                CaptureComparison();
                Debug.Log("[CoopDeck] Fox look comparison finished.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public sealed class Look
        {
            public Material Original;
            public Material Lit;
            public Material SoftToon;
            public Material Outline;
            public Material FirstPerson;
            public Texture2D RegionDebug;
        }

        public static Look EnsureAll(bool rebuildMask)
        {
            Material original = BackupOriginalMaterial();
            Texture2D debug = null;
            if (rebuildMask || !File.Exists(SurfaceMaskPath) || !File.Exists(MetallicSmoothnessPath))
            {
                debug = BuildSurfaceMasks();
            }

            ConfigureDataTexture(SurfaceMaskPath);
            ConfigureDataTexture(MetallicSmoothnessPath);
            ConfigureNormalMap();
            var look = new Look
            {
                Original = original,
                Lit = EnsureLitMaterial(),
                SoftToon = EnsureSoftToonMaterial(),
                Outline = EnsureOutlineMaterial(),
                RegionDebug = debug,
            };
            look.FirstPerson = EnsureFirstPersonMaterial(look.SoftToon);
            AssetDatabase.SaveAssets();
            return look;
        }

        // ---------------------------------------------------------------- backup

        /// <summary>Keeps a copy of the material as it was before the look work (the CH_Toon cel version).</summary>
        static Material BackupOriginalMaterial()
        {
            if (!File.Exists(BackupMaterialPath))
            {
                EditorUtil.EnsureFolder(Path.GetDirectoryName(BackupMaterialPath)?.Replace('\\', '/'));
                if (!AssetDatabase.CopyAsset(FoxGunnerAssetFactory.MaterialPath, BackupMaterialPath))
                {
                    throw new IOException("could not back up " + FoxGunnerAssetFactory.MaterialPath);
                }
            }

            return AssetDatabase.LoadAssetAtPath<Material>(BackupMaterialPath);
        }

        // ---------------------------------------------------------------- surface mask

        enum Part { Head, Neck, Torso, Shoulder, Hips, Tail, UpperArm, Forearm, Hand, Thigh, Shin, Foot }

        // Grey = cool or colourless grey (steel); Muted = warm low-saturation shading on fur, skin and cloth.
        enum Tone { Glow, Amber, Orange, Red, Cream, Peach, Grey, Muted, Dark }

        /// <summary>Body part from a bone name (tolerates added Tail/Ear/Finger bones).</summary>
        static Part PartOf(string bone)
        {
            string b = bone.ToLowerInvariant();
            if (b.Contains("tail")) return Part.Tail;
            if (b.Contains("forearm")) return Part.Forearm; // before "ear": fore-ARM contains it
            if (b.StartsWith("ear") || b.Contains("_ear") || b.Contains("head")) return Part.Head;
            if (b.Contains("neck")) return Part.Neck;
            if (b.Contains("spine")) return Part.Torso;
            if (b.Contains("hips")) return Part.Hips;
            if (b.Contains("shoulder")) return Part.Shoulder; // clavicle: carries the scarf and the vest, not the armbands
            if (b.Contains("forearm")) return Part.Forearm;
            if (b.Contains("hand") || b.Contains("finger") || b.Contains("thumb")) return Part.Hand;
            if (b.Contains("upleg")) return Part.Thigh;
            if (b.Contains("foot") || b.Contains("toe")) return Part.Foot;
            if (b.EndsWith("leg")) return Part.Shin;
            if (b.EndsWith("arm")) return Part.UpperArm;
            return Part.Torso;
        }

        static Tone ToneOf(Color32 c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v);
            if (h >= 0.09f && h <= 0.17f && s >= 0.55f && v >= 0.8f) return Tone.Glow;
            if (h >= 0.095f && h <= 0.17f && s >= 0.4f && v >= 0.45f) return Tone.Amber; // irises, pouches, soles
            if (h >= 0.02f && h < 0.11f && s >= 0.45f && v >= 0.5f) return Tone.Orange;
            if ((h < 0.03f || h > 0.94f) && s >= 0.45f && v >= 0.25f) return Tone.Red;
            if (s < 0.28f && v >= 0.68f) return Tone.Cream;
            if (v < 0.3f) return Tone.Dark;
            if (s < 0.22f) return s < 0.1f || h > 0.4f && h < 0.8f ? Tone.Grey : Tone.Muted;
            if (h >= 0.02f && h <= 0.12f && v >= 0.6f) return Tone.Peach;
            return v < 0.45f ? Tone.Dark : Tone.Cream;
        }

        sealed class Zones
        {
            public Vector3 Head, Neck, Hips, Knee;
            public bool Face(Vector3 p) => p.z > Head.z - 0.01f && p.y < Head.y + 0.15f && p.y > Neck.y - 0.02f;
            public bool Tail(Vector3 p) => p.z < Hips.z - 0.1f && p.y > 0.15f && p.y < Hips.y + 0.05f;
            public bool Belt(Vector3 p) => Mathf.Abs(p.y - Hips.y) < 0.06f;
            public bool Boot(Vector3 p) => p.y < Knee.y - 0.12f;
        }

        static Region Decide(Part part, Tone tone, Vector3 p, Zones z)
        {
            bool furTone = tone == Tone.Orange || tone == Tone.Cream || tone == Tone.Peach || tone == Tone.Muted;
            if (tone == Tone.Glow || tone == Tone.Amber)
            {
                switch (part)
                {
                    case Part.Head: return z.Face(p) ? Region.Eye : Region.Glass; // irises; goggle lenses
                    case Part.Neck:
                    case Part.Tail: return Region.Fur;
                    case Part.Torso: return tone == Tone.Glow ? Region.Metal : Region.Cloth; // emblem on the top
                    default: return tone == Tone.Glow ? Region.Metal : Region.Leather;  // flame plates; pouches, soles
                }
            }

            switch (part)
            {
                case Part.Head:
                    if (furTone || tone == Tone.Dark && z.Face(p)) return z.Face(p) ? Region.Face : Region.Fur;
                    return tone == Tone.Red ? Region.Cloth : tone == Tone.Grey ? Region.Metal : Region.Leather;
                case Part.Neck:
                    return tone == Tone.Red || tone == Tone.Dark ? Region.Cloth : Region.Fur; // scarf and its shading
                case Part.Tail:
                    return Region.Fur;
                case Part.Torso:
                case Part.Shoulder:
                    // Bare midriff and chest are fur; behind the body orange is the scarf's flame print.
                    bool front = p.z > z.Neck.z - 0.08f;
                    return front && (tone == Tone.Orange || tone == Tone.Peach) ? Region.Fur : Region.Cloth;
                case Part.Hips:
                case Part.Thigh:
                    if (furTone && z.Tail(p)) return Region.Fur;
                    if (tone == Tone.Grey) return Region.Metal;
                    if (tone == Tone.Dark) return part == Part.Hips && z.Belt(p) ? Region.Leather : Region.Cloth;
                    return tone == Tone.Orange ? Region.Leather : Region.Cloth; // orange pouches; red sash, cream vest hem
                case Part.UpperArm:
                    return tone == Tone.Dark || tone == Tone.Red || tone == Tone.Grey ? Region.Leather : Region.Fur; // armbands; bare arms
                case Part.Forearm:
                case Part.Hand:
                    if (tone == Tone.Grey) return Region.Metal; // glove plates
                    return tone == Tone.Dark || tone == Tone.Red ? Region.Leather : Region.Fur; // gloves; fingers
                case Part.Shin:
                    if (tone == Tone.Grey) return Region.Metal;
                    return (tone == Tone.Dark || tone == Tone.Muted) && !z.Boot(p) ? Region.Cloth : Region.Leather; // pant cuffs above the boots
                default: // Foot
                    return tone == Tone.Grey ? Region.Metal : Region.Leather;
            }
        }

        /// <summary>
        /// Rasterizes every triangle into UV space and decides each texel's region from its colour, the
        /// triangle's body part (dominant skin bone) and its bind-pose position. Writes the soft-toon surface
        /// mask and the URP Lit metallic/smoothness map; returns a region-colour texture for checking.
        /// </summary>
        static Texture2D BuildSurfaceMasks()
        {
            // The FBX itself (bind pose, Humanoid avatar): independent of the body prefab, which uses these materials.
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(FoxGunnerAssetFactory.ModelPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            try
            {
                SkinnedMeshRenderer skinned = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                Animator animator = instance.GetComponent<Animator>();
                Mesh mesh = skinned.sharedMesh;
                var baked = new Mesh();
                skinned.BakeMesh(baked, true);
                Matrix4x4 toWorld = skinned.transform.localToWorldMatrix;
                Vector3[] world = baked.vertices.Select(v => toWorld.MultiplyPoint3x4(v)).ToArray();
                Object.DestroyImmediate(baked);

                var zones = new Zones
                {
                    Head = animator.GetBoneTransform(HumanBodyBones.Head).position,
                    Neck = animator.GetBoneTransform(HumanBodyBones.Neck).position,
                    Hips = animator.GetBoneTransform(HumanBodyBones.Hips).position,
                    Knee = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).position,
                };
                Part[] boneParts = skinned.bones.Select(b => PartOf(b.name)).ToArray();
                BoneWeight[] weights = mesh.boneWeights;
                Vector2[] uv = mesh.uv;
                int[] tris = mesh.triangles;

                var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                source.LoadImage(File.ReadAllBytes(FoxGunnerAssetFactory.TexturePath));
                Color32[] baseColors = source.GetPixels32();
                int bw = source.width, bh = source.height;
                Object.DestroyImmediate(source);

                var regions = new int[MaskSize * MaskSize];
                for (int i = 0; i < regions.Length; i++) regions[i] = -1;
                var headProbe = new Dictionary<Tone, List<Vector3>>();

                for (int t = 0; t < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    Part part = MajorityPart(boneParts, weights[a], weights[b], weights[c]);
                    Rasterize(uv[a], uv[b], uv[c], (x, y, wa, wb, wc) =>
                    {
                        Vector2 texel = uv[a] * wa + uv[b] * wb + uv[c] * wc;
                        int sx = Mathf.Clamp((int)(texel.x * bw), 0, bw - 1), sy = Mathf.Clamp((int)(texel.y * bh), 0, bh - 1);
                        Vector3 p = world[a] * wa + world[b] * wb + world[c] * wc;
                        Tone tone = ToneOf(baseColors[sy * bw + sx]);
                        if (part == Part.Head)
                        {
                            if (!headProbe.TryGetValue(tone, out List<Vector3> list)) headProbe[tone] = list = new List<Vector3>();
                            list.Add(p);
                        }

                        regions[y * MaskSize + x] = (int)Decide(part, tone, p, zones);
                    });
                }

                Debug.Log($"[CoopDeck] head bone {zones.Head}, neck {zones.Neck}; head texels by tone (y p10/p50/p90, z p50): " +
                          string.Join(" | ", headProbe.Select(kv =>
                          {
                              float[] ys = kv.Value.Select(v => v.y).OrderBy(v => v).ToArray();
                              float[] zs = kv.Value.Select(v => v.z).OrderBy(v => v).ToArray();
                              return $"{kv.Key} n={ys.Length} y {ys[ys.Length / 10]:0.00}/{ys[ys.Length / 2]:0.00}/{ys[ys.Length * 9 / 10]:0.00} z {zs[zs.Length / 2]:0.00}";
                          })));

                int cleaned = RemoveSpeckles(regions, 2);
                Debug.Log($"[CoopDeck] Fox surface mask: {cleaned} isolated texels joined their neighbours' region");
                Dilate(regions, 4);
                WriteMasks(regions, out Texture2D debug);
                LogCoverage(regions);
                return debug;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        static Part MajorityPart(Part[] boneParts, params BoneWeight[] weights)
        {
            var score = new Dictionary<Part, float>();
            foreach (BoneWeight w in weights)
            {
                Add(w.boneIndex0, w.weight0);
                Add(w.boneIndex1, w.weight1);
                Add(w.boneIndex2, w.weight2);
                Add(w.boneIndex3, w.weight3);
            }

            return score.OrderByDescending(kv => kv.Value).First().Key;

            void Add(int bone, float weight)
            {
                if (weight <= 0f) return;
                Part part = boneParts[bone];
                score[part] = score.TryGetValue(part, out float s) ? s + weight : weight;
            }
        }

        delegate void TexelVisitor(int x, int y, float wa, float wb, float wc);

        static void Rasterize(Vector2 ua, Vector2 ub, Vector2 uc, TexelVisitor visit)
        {
            Vector2 pa = ua * MaskSize, pb = ub * MaskSize, pc = uc * MaskSize;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))));
            int maxX = Mathf.Min(MaskSize - 1, Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))));
            int maxY = Mathf.Min(MaskSize - 1, Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))));
            float area = (pb.x - pa.x) * (pc.y - pa.y) - (pc.x - pa.x) * (pb.y - pa.y);
            if (Mathf.Abs(area) < 1e-6f) return;
            const float Edge = -0.02f; // a little past the edges so thin triangles still claim texels
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var p = new Vector2(x + 0.5f, y + 0.5f);
                    float wa = ((pb.x - p.x) * (pc.y - p.y) - (pc.x - p.x) * (pb.y - p.y)) / area;
                    float wb = ((pc.x - p.x) * (pa.y - p.y) - (pa.x - p.x) * (pc.y - p.y)) / area;
                    float wc = 1f - wa - wb;
                    if (wa >= Edge && wb >= Edge && wc >= Edge)
                    {
                        visit(x, y, Mathf.Clamp01(wa), Mathf.Clamp01(wb), Mathf.Clamp01(wc));
                    }
                }
            }
        }

        static readonly int[] NeighbourX = { -1, 0, 1, -1, 1, -1, 0, 1 };
        static readonly int[] NeighbourY = { -1, -1, -1, 0, 0, 1, 1, 1 };

        /// <summary>
        /// Majority filter over used texels: a texel whose region at most one of its 8 neighbours shares, while 5 or
        /// more agree on another region, takes that region. Each texel is classified from one base-colour texel, so a
        /// stray darker or lighter texel in the painted skin or fur would flip class and show as a dot in the shading.
        /// Thicker features (eye lines, straps) keep their texels; unused texels and UV island gaps are not counted.
        /// </summary>
        static int RemoveSpeckles(int[] regions, int passes)
        {
            int changed = 0;
            var counts = new int[Enum.GetValues(typeof(Region)).Length];
            for (int pass = 0; pass < passes; pass++)
            {
                var next = (int[])regions.Clone();
                for (int y = 0; y < MaskSize; y++)
                {
                    for (int x = 0; x < MaskSize; x++)
                    {
                        int own = regions[y * MaskSize + x];
                        if (own < 0) continue;
                        Array.Clear(counts, 0, counts.Length);
                        for (int k = 0; k < 8; k++)
                        {
                            int nx = x + NeighbourX[k], ny = y + NeighbourY[k];
                            if (nx < 0 || ny < 0 || nx >= MaskSize || ny >= MaskSize) continue;
                            int r = regions[ny * MaskSize + nx];
                            if (r >= 0) counts[r]++;
                        }

                        if (counts[own] > 1) continue;
                        int best = own;
                        for (int r = 0; r < counts.Length; r++)
                        {
                            if (r != own && counts[r] >= 5 && counts[r] > counts[best]) best = r;
                        }

                        if (best != own)
                        {
                            next[y * MaskSize + x] = best;
                            changed++;
                        }
                    }
                }

                Array.Copy(next, regions, regions.Length);
            }

            return changed;
        }

        /// <summary>Grows regions into unused texels so bilinear filtering and mips do not bleed at UV seams.</summary>
        static void Dilate(int[] regions, int passes)
        {
            for (int pass = 0; pass < passes; pass++)
            {
                var next = (int[])regions.Clone();
                for (int y = 0; y < MaskSize; y++)
                {
                    for (int x = 0; x < MaskSize; x++)
                    {
                        if (regions[y * MaskSize + x] >= 0) continue;
                        for (int k = 0; k < 8; k++)
                        {
                            int nx = x + NeighbourX[k], ny = y + NeighbourY[k];
                            if (nx < 0 || ny < 0 || nx >= MaskSize || ny >= MaskSize) continue;
                            int r = regions[ny * MaskSize + nx];
                            if (r >= 0)
                            {
                                next[y * MaskSize + x] = r;
                                break;
                            }
                        }
                    }
                }

                Array.Copy(next, regions, regions.Length);
            }
        }

        static void WriteMasks(int[] regions, out Texture2D debug)
        {
            var surface = new Color32[regions.Length];
            var metallic = new Color32[regions.Length];
            var debugPixels = new Color32[regions.Length];
            for (int i = 0; i < regions.Length; i++)
            {
                bool set = regions[i] >= 0;
                Vector4 p = set ? Presets[(Region)regions[i]] : Vector4.zero; // texels no triangle uses stay empty
                surface[i] = new Color(p.x, p.y, p.z, p.w);
                metallic[i] = new Color(p.z, 0f, 0f, p.y); // URP Lit: R = metallic, A = smoothness
                debugPixels[i] = set ? (Color32)DebugColors[(Region)regions[i]] : new Color32(255, 0, 255, 255);
            }

            Save(SurfaceMaskPath, surface);
            Save(MetallicSmoothnessPath, metallic);
            debug = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, true);
            debug.SetPixels32(debugPixels);
            debug.Apply();
        }

        static void Save(string path, Color32[] pixels)
        {
            var texture = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        static void LogCoverage(int[] regions)
        {
            int used = regions.Count(r => r >= 0);
            string shares = string.Join(", ", Enum.GetValues(typeof(Region)).Cast<Region>()
                .Select(r => $"{r} {100f * regions.Count(x => x == (int)r) / Mathf.Max(1, used):0.0}%"));
            Debug.Log($"[CoopDeck] Fox surface mask: {shares}");
        }

        static void ConfigureDataTexture(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false; // parameters, not colours
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = MaskSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC7 keeps the four channels apart
            importer.SaveAndReimport();
        }

        // ---------------------------------------------------------------- materials

        static Material EnsureMaterial(string path, Shader shader)
        {
            if (shader == null)
            {
                throw new FileNotFoundException("shader for " + path + " is missing");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                EditorUtil.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(FoxGunnerAssetFactory.TexturePath));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>First check: plain URP Lit with the region map driving metallic and smoothness.</summary>
        static Material EnsureLitMaterial()
        {
            Material m = EnsureMaterial(LitMaterialPath, Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", Color.white);
            m.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(MetallicSmoothnessPath));
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
            m.SetFloat("_WorkflowMode", 1f);
            m.SetFloat("_SmoothnessTextureChannel", 0f); // metallic alpha
            m.SetFloat("_Smoothness", 1f);               // multiplier when the map is set
            m.SetFloat("_Metallic", 1f);
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMapPath));
            m.SetFloat("_BumpScale", 1f);
            m.EnableKeyword("_NORMALMAP");
            return m;
        }

        static Material EnsureSoftToonMaterial()
        {
            Material m = EnsureMaterial(SoftToonMaterialPath, AssetDatabase.LoadAssetAtPath<Shader>(SoftToonShaderPath));
            m.SetTexture("_SurfaceMask", AssetDatabase.LoadAssetAtPath<Texture2D>(SurfaceMaskPath));
            m.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMapPath));
            m.SetFloat("_NormalStrength", 1f);
            return m;
        }

        /// <summary>
        /// The soft toon look without the body's normal map, for the first-person arms: their mesh (high-res forearms,
        /// modelled hands) only borrows the body's UVs for colour, so the body's baked normals would not fit it.
        /// </summary>
        static Material EnsureFirstPersonMaterial(Material softToon)
        {
            Material m = EnsureMaterial(FirstPersonMaterialPath, softToon.shader);
            m.CopyPropertiesFromMaterial(softToon);
            m.SetTexture("_NormalMap", null);
            m.SetFloat("_NormalStrength", 0f);
            return m;
        }

        static void ConfigureNormalMap()
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(NormalMapPath);
            importer.textureType = TextureImporterType.NormalMap;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 4096;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC5: two clean channels
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }

        static Material EnsureOutlineMaterial()
        {
            return EnsureMaterial(OutlineMaterialPath, AssetDatabase.LoadAssetAtPath<Shader>(OutlineShaderPath));
        }

        // ---------------------------------------------------------------- comparison capture

        /// <summary>
        /// Renders the idle body under one fixed lighting setup (sun, sky-like ambient, the sandbox's post
        /// profile) with each look: before (CH_Toon backup), URP Lit, soft toon + outline, and the region map.
        /// </summary>
        public static void CaptureComparison()
        {
            Look look = EnsureAll(rebuildMask: false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Created after the scene switch: loading a scene destroys unsaved in-memory textures.
            Texture2D regionDebug = RegionDebugFromMask();
            regionDebug.hideFlags = HideFlags.DontSave;
            var ambient = new SphericalHarmonicsL2();
            ambient.AddAmbientLight(new Color(0.36f, 0.39f, 0.47f));
            ambient.AddDirectionalLight(Vector3.up, new Color(0.22f, 0.26f, 0.34f), 1f);
            ambient.AddDirectionalLight(Vector3.down, new Color(0.12f, 0.1f, 0.08f), 1f);
            RenderSettings.ambientMode = AmbientMode.Custom;
            RenderSettings.ambientProbe = ambient;
            RenderSettings.fog = false;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.88f);
            sun.intensity = 1.25f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(38f, 145f, 0f);

            var volume = new GameObject("Post").AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(GamePaths.Rendering + "/VP_Sandbox.asset");

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            var groundMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMaterial.SetColor("_BaseColor", new Color(0.42f, 0.44f, 0.48f));
            groundMaterial.SetFloat("_Smoothness", 0.1f);
            ground.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoxGunnerAssetFactory.PrefabPath);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Animator animator = body.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.SetFloat(AnimatorParams.Speed, 0f);
            animator.Update(0f);
            animator.Update(0.8f); // idle pose, set before the first render (skinning runs once per editor frame)
            SkinnedMeshRenderer skinned = body.GetComponentInChildren<SkinnedMeshRenderer>();

            var cameraGo = new GameObject("CaptureCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.24f, 0.26f, 0.31f);
            camera.nearClipPlane = 0.05f;
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            var debugMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            debugMaterial.SetTexture("_BaseMap", regionDebug);
            debugMaterial.SetColor("_BaseColor", Color.white);
            Color32[] check = regionDebug.GetPixels32();
            Debug.Log($"[CoopDeck] region debug: shader {debugMaterial.shader.name}, texture {regionDebug.width}x{regionDebug.height}, " +
                      $"distinct colours {check.Distinct().Count()}, centre {check[check.Length / 2]}");

            var variants = new (string name, Material[] materials)[]
            {
                ("Before", new[] { look.Original }),
                ("URPLit", new[] { look.Lit }),
                ("SoftToon", new[] { look.SoftToon, look.Outline }),
                ("Regions", new[] { debugMaterial }),
            };

            var rows = new List<Texture2D>();
            foreach (var variant in variants)
            {
                skinned.sharedMaterials = variant.materials;
                Texture2D row = CaptureRow(camera, body.transform);
                File.WriteAllBytes($"Screenshots/FoxLook_{variant.name}.png", row.EncodeToPNG());
                rows.Add(row);
            }

            // Before / URP Lit / soft toon, top to bottom.
            WriteSheet("Screenshots/FoxLook_Compare.png", rows[0], rows[1], rows[2]);
            WriteSheet("Screenshots/FoxLook_BeforeAfter.png", rows[0], rows[2]);

            // The baked normal map shows mostly where light turns away: the soft toon look under a low side sun,
            // without (top) and with (bottom) the map.
            sun.transform.rotation = Quaternion.Euler(18f, 100f, 0f);
            var flat = new Material(look.SoftToon) { hideFlags = HideFlags.DontSave };
            flat.SetFloat("_NormalStrength", 0f);
            skinned.sharedMaterials = new[] { flat, look.Outline };
            Texture2D sideFlat = CaptureRow(camera, body.transform);
            skinned.sharedMaterials = new[] { look.SoftToon, look.Outline };
            Texture2D sideMapped = CaptureRow(camera, body.transform);
            WriteSheet("Screenshots/FoxLook_NormalMap_SideLight.png", sideFlat, sideMapped);
            Object.DestroyImmediate(flat);
            sun.transform.rotation = Quaternion.Euler(38f, 145f, 0f);
            Object.DestroyImmediate(groundMaterial);
            Object.DestroyImmediate(debugMaterial);
            Debug.Log("[CoopDeck] Fox look captures written to Screenshots/FoxLook_*.png");

            CaptureInSandbox(look);
        }

        static void WriteSheet(string path, params Texture2D[] rowsTopToBottom)
        {
            int w = rowsTopToBottom[0].width, h = rowsTopToBottom[0].height;
            var sheet = new Texture2D(w, h * rowsTopToBottom.Length, TextureFormat.RGB24, false);
            for (int i = 0; i < rowsTopToBottom.Length; i++)
            {
                sheet.SetPixels(0, h * (rowsTopToBottom.Length - 1 - i), w, h, rowsTopToBottom[i].GetPixels());
            }

            sheet.Apply();
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
        }

        /// <summary>
        /// The same comparison under the game's own lighting: the sandbox scene (its sun, sky, fog and post),
        /// a fox standing near the player spawn, seen from a team-mate's distance (about 4 m, game FOV).
        /// </summary>
        static void CaptureInSandbox(Look look)
        {
            EditorSceneManager.OpenScene(GamePaths.SandboxScene, OpenSceneMode.Single); // never saved
            Transform player = Object.FindAnyObjectByType<CoopDeck.Input.PlayerInputReader>().transform;
            Vector3 spot = player.position + player.forward * 4f + player.right * 0.6f;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoxGunnerAssetFactory.PrefabPath);
            var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            body.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-player.forward + player.right * 0.6f, Vector3.up));
            Animator animator = body.GetComponent<Animator>();
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            animator.Update(0.8f);
            SkinnedMeshRenderer skinned = body.GetComponentInChildren<SkinnedMeshRenderer>();

            var cameraGo = new GameObject("SandboxCapture");
            var camera = cameraGo.AddComponent<Camera>();
            camera.fieldOfView = 68f; // the player camera's FOV
            camera.nearClipPlane = 0.05f;
            camera.cullingMask = ~(1 << CoopDeck.Core.GameLayers.ViewModelIndex);
            UniversalAdditionalCameraData data = camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraGo.transform.position = player.position + Vector3.up * 1.62f;
            cameraGo.transform.LookAt(spot + Vector3.up * 0.9f);

            var shots = new List<Texture2D>();
            foreach (Material[] materials in new[] { new[] { look.Original }, new[] { look.SoftToon, look.Outline } })
            {
                skinned.sharedMaterials = materials;
                Texture2D wide = Render(camera, 1120, 760);
                camera.fieldOfView = 22f; // the same fox through a zoomed view
                Texture2D zoom = Render(camera, 1120, 760);
                camera.fieldOfView = 68f;
                var row = new Texture2D(2240, 760, TextureFormat.RGB24, false);
                row.SetPixels(0, 0, 1120, 760, wide.GetPixels());
                row.SetPixels(1120, 0, 1120, 760, zoom.GetPixels());
                row.Apply();
                shots.Add(row);
            }

            WriteSheet("Screenshots/FoxLook_Sandbox_BeforeAfter.png", shots[0], shots[1]);
            Debug.Log("[CoopDeck] Fox look sandbox capture written");
        }

        // ---------------------------------------------------------------- cost check

        /// <summary>
        /// GPU cost check on this machine: nine idle foxes filling a 1280x720 view under the current quality
        /// level, each look rendered 150 times with a GPU sync per frame; logs milliseconds per frame,
        /// with an empty-scene baseline. Run with the Handheld quality level for the ROG Ally X numbers.
        /// </summary>
        public static void BenchmarkBatch()
        {
            int previousLevel = QualitySettings.GetQualityLevel(); // restored below so the project settings stay untouched
            try
            {
                string level = Environment.GetCommandLineArgs().SkipWhile(a => a != "-coopdeckQuality").Skip(1).FirstOrDefault() ?? "Handheld";
                int index = Array.IndexOf(QualitySettings.names, level);
                if (index >= 0)
                {
                    QualitySettings.SetQualityLevel(index, true);
                }

                Look look = EnsureAll(rebuildMask: false);
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
                Camera camera = Camera.main;
                camera.transform.SetPositionAndRotation(new Vector3(0f, 1.0f, 4.2f), Quaternion.Euler(4f, 180f, 0f));
                camera.fieldOfView = 68f;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                Light sun = Object.FindAnyObjectByType<Light>();
                sun.shadows = LightShadows.Soft;

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FoxGunnerAssetFactory.PrefabPath);
                var bodies = new List<SkinnedMeshRenderer>();
                for (int i = 0; i < 9; i++)
                {
                    var body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    body.transform.position = new Vector3((i % 3 - 1) * 0.9f, 0f, (i / 3) * -0.9f);
                    body.transform.rotation = Quaternion.Euler(0f, 160f + i * 7f, 0f);
                    Animator animator = body.GetComponent<Animator>();
                    animator.Rebind();
                    animator.Update(0f);
                    animator.Update(0.5f + i * 0.1f);
                    bodies.Add(body.GetComponentInChildren<SkinnedMeshRenderer>());
                }

                var results = new List<string>();
                var variants = new (string name, Material[] materials)[]
                {
                    ("Empty", null),
                    ("Before (CH_Toon)", new[] { look.Original }),
                    ("URP Lit", new[] { look.Lit }),
                    ("Soft toon + outline", new[] { look.SoftToon, look.Outline }),
                };
                var target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                foreach (var variant in variants)
                {
                    foreach (SkinnedMeshRenderer body in bodies)
                    {
                        body.enabled = variant.materials != null;
                        if (variant.materials != null)
                        {
                            body.sharedMaterials = variant.materials;
                        }
                    }

                    var times = new List<double>();
                    for (int frame = 0; frame < 170; frame++)
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                        RenderTexture.active = target;
                        probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); // waits for the GPU
                        RenderTexture.active = null;
                        watch.Stop();
                        if (frame >= 20)
                        {
                            times.Add(watch.Elapsed.TotalMilliseconds);
                        }
                    }

                    times.Sort();
                    results.Add($"{variant.name}: median {times[times.Count / 2]:0.00} ms, p90 {times[times.Count * 9 / 10]:0.00} ms");
                }

                Debug.Log($"[CoopDeck] Fox look GPU check ({SystemInfo.graphicsDeviceName}, quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}, 9 foxes, 1280x720): "
                          + string.Join(" | ", results));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
            finally
            {
                QualitySettings.SetQualityLevel(previousLevel, false);
            }
        }

        /// <summary>Four views of the idle body: front three-quarter, right side, back three-quarter, face close-up.</summary>
        static Texture2D CaptureRow(Camera camera, Transform body)
        {
            const int W = 560, H = 760;
            Vector3 center = body.position + Vector3.up * 0.82f;
            var shots = new (Vector3 offset, Vector3 target, float fov)[]
            {
                (new Vector3(-1.6f, 0.55f, 3.6f), center, 30f),
                (new Vector3(4.0f, 0.3f, 0f), center, 28f),
                (new Vector3(1.8f, 0.6f, -3.4f), center, 30f),
                (new Vector3(-0.35f, 0.42f, 1.15f), body.position + Vector3.up * 1.22f, 24f),
            };
            var row = new Texture2D(W * shots.Length, H, TextureFormat.RGB24, false);
            for (int i = 0; i < shots.Length; i++)
            {
                camera.fieldOfView = shots[i].fov;
                camera.transform.position = shots[i].target + shots[i].offset;
                camera.transform.LookAt(shots[i].target);
                Texture2D shot = Render(camera, W, H);
                row.SetPixels(i * W, 0, W, H, shot.GetPixels());
                Object.DestroyImmediate(shot);
            }

            row.Apply();
            return row;
        }

        static Texture2D Render(Camera camera, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            target.Create();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            target.Release();
            Object.DestroyImmediate(target);
            return image;
        }

        /// <summary>Region colours recovered from the saved mask (when it was not rebuilt this run).</summary>
        static Texture2D RegionDebugFromMask()
        {
            var mask = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            mask.LoadImage(File.ReadAllBytes(SurfaceMaskPath));
            Color32[] pixels = mask.GetPixels32();
            var debug = new Texture2D(mask.width, mask.height, TextureFormat.RGBA32, true);
            var colors = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color p = pixels[i];
                var value = new Vector4(p.r, p.g, p.b, p.a);
                Region best = Presets.OrderBy(kv => (kv.Value - value).sqrMagnitude).First().Key;
                colors[i] = DebugColors[best];
            }

            debug.SetPixels32(colors);
            debug.Apply();
            Object.DestroyImmediate(mask);
            return debug;
        }
    }
}
