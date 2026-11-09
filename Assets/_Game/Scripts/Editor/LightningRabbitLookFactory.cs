using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Characters;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// The Lightning Rabbit's surface look and its own lightning (Phase 9, Docs/LIGHTNING_RABBIT.md §3.7):
    ///   masks      the per-part surface mask and the glow mask baked by ArtSource/Tools/rabbit/rabbit_look.py (body and
    ///              first-person arms, each in its own UVs), imported as linear data
    ///   glow       a copy of only the glowing triangles of each body LOD and of the first-person arms, skinned to the same
    ///              bones and drawn additively with CH_GlowLines (the soft toon shader has no emission)
    ///   markers    glove-lens points on both hands (where the hands' glowing triangles are) and the ear tips
    ///   effects    spark and ear-arc prefabs (Particle System / LineRenderer, played through FxPool like every other
    ///              effect) and the afterimage material; <see cref="ElectricBodyFx"/> on the kit plays them
    /// VFX Graph is not used, for the reason in Docs/FIREBALL_VFX.md (graphs cannot be built through the public API).
    /// </summary>
    public static class LightningRabbitLookFactory
    {
        const string Textures = LightningRabbitAssetFactory.ArtFolder + "/Textures";
        const string Materials = LightningRabbitAssetFactory.ArtFolder + "/Materials";
        public const string MeshFolder = LightningRabbitAssetFactory.ArtFolder + "/Meshes";
        public const string FxFolder = GamePaths.VfxPrefabs + "/LightningRabbit";
        public const string FirstPersonSurfaceMaskPath = Textures + "/T_LightningRabbit_FPArms_SurfaceMask.png";
        public const string GlowMaskPath = Textures + "/T_LightningRabbit_GlowMask.png";
        public const string FirstPersonGlowMaskPath = Textures + "/T_LightningRabbit_FPArms_GlowMask.png";
        public const string GlowMaterialPath = Materials + "/M_LightningRabbit_Glow.mat";
        public const string FirstPersonGlowMaterialPath = Materials + "/M_LightningRabbit_Glow_FirstPerson.mat";
        public const string GhostMaterialPath = Materials + "/M_LightningRabbit_Afterimage.mat";
        public const string GlowShaderPath = GamePaths.Root + "/Art/Characters/Shaders/CH_GlowLines.shader";
        public const string GhostShaderPath = GamePaths.Root + "/Art/Characters/Shaders/CH_ElectricGhost.shader";
        public const string SparkPrefabName = "FX_LightningRabbit_Spark";
        public const string BodySparkPrefabName = "FX_LightningRabbit_Spark_Body";
        public const string ArcPrefabName = "FX_LightningRabbit_EarArc";
        public const string GlowSuffix = "_Glow";
        public const string LeftLensName = "GloveLens_L";
        public const string RightLensName = "GloveLens_R";
        public const string LeftEarTipName = "EarTip_L";
        public const string RightEarTipName = "EarTip_R";

        /// <summary>Triangles under glow-mask texels brighter than this (within one texel) get a glow copy.</summary>
        const float GlowThreshold = 0.02f;

        /// <summary>The design's glow: the blue-white of the bolt marks, battery tubes and lenses (#59D7F1 → white).</summary>
        public static readonly Color GlowColor = new Color(0.35f, 0.84f, 0.95f);
        public static readonly Color SparkCore = new Color(1f, 0.984f, 0.902f);   // Palette lightning core #FFFBE6
        public static readonly Color SparkEdge = new Color(0.35f, 0.84f, 0.95f);

        // ---------------------------------------------------------------- textures and materials

        /// <summary>Import settings of the four baked masks (linear data; the glow masks are single channel).</summary>
        public static void ConfigureMasks()
        {
            ConfigureSurfaceMask(LightningRabbitAssetFactory.SurfaceMaskPath);
            ConfigureSurfaceMask(FirstPersonSurfaceMaskPath);
            ConfigureGlowMask(GlowMaskPath);
            ConfigureGlowMask(FirstPersonGlowMaskPath);
        }

        static void ConfigureSurfaceMask(string path)
        {
            var importer = FoxGunnerAssetFactory.GetImporter<TextureImporter>(path);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false; // parameters, not colours
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC7 keeps the four channels apart
            importer.SaveAndReimport();
        }

        static void ConfigureGlowMask(string path)
        {
            var importer = FoxGunnerAssetFactory.GetImporter<TextureImporter>(path);
            importer.textureType = TextureImporterType.SingleChannel;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(settings);
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.textureCompression = TextureImporterCompression.CompressedHQ; // BC4
            importer.SaveAndReimport();
        }

        public static Material EnsureGlowMaterial(string path, string baseMapPath, string glowMaskPath)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(GlowShaderPath);
            Material m = EnsureMaterial(path, shader);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseMapPath));
            m.SetTexture("_GlowMask", AssetDatabase.LoadAssetAtPath<Texture2D>(glowMaskPath));
            m.SetColor("_GlowColor", GlowColor * 1.4f);
            m.SetFloat("_ColorFromBase", 0.6f);
            m.SetFloat("_Intensity", 1.8f);
            m.SetFloat("_Flicker", 0.3f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static Material EnsureGhostMaterial()
        {
            Material m = EnsureMaterial(GhostMaterialPath, AssetDatabase.LoadAssetAtPath<Shader>(GhostShaderPath));
            m.SetColor("_GhostColor", GlowColor * 1.2f);
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material EnsureMaterial(string path, Shader shader)
        {
            if (shader == null)
            {
                throw new FileNotFoundException("shader for " + path + " is missing");
            }

            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                EditorUtil.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                m = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = shader;
            return m;
        }

        // ---------------------------------------------------------------- glow meshes

        /// <summary>Glow mask as floats (row 0 = v 0), read from the PNG so the import settings do not matter.</summary>
        public static float[] ReadMask(string path, out int size)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.LoadImage(File.ReadAllBytes(path));
            size = texture.width;
            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            var mask = new float[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                mask[i] = pixels[i].r / 255f;
            }

            return mask;
        }

        /// <summary>
        /// Indices of the triangles of <paramref name="source"/> whose UV footprint (grown by one texel) touches a glowing
        /// texel of <paramref name="mask"/>.
        /// </summary>
        public static List<int> GlowingTriangles(Mesh source, float[] mask, int size)
        {
            Vector2[] uv = source.uv;
            int[] tris = source.triangles;
            var keep = new List<int>();
            for (int t = 0; t < tris.Length; t += 3)
            {
                Vector2 a = uv[tris[t]], b = uv[tris[t + 1]], c = uv[tris[t + 2]];
                int x0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) * size) - 1, 0, size - 1);
                int x1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) * size) + 1, 0, size - 1);
                int y0 = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)) * size) - 1, 0, size - 1);
                int y1 = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)) * size) + 1, 0, size - 1);
                bool glows = false;
                for (int y = y0; y <= y1 && !glows; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        if (mask[y * size + x] > GlowThreshold)
                        {
                            glows = true;
                            break;
                        }
                    }
                }

                if (glows)
                {
                    keep.Add(t / 3);
                }
            }

            return keep;
        }

        /// <summary>
        /// A mesh asset holding only the glowing triangles of <paramref name="source"/>: same positions, normals, UVs,
        /// skin weights and bind poses, so it skins exactly like the source on the same bones. Updates the asset in place.
        /// </summary>
        public static Mesh BuildGlowMesh(Mesh source, float[] mask, int size, string path)
        {
            List<int> keep = GlowingTriangles(source, mask, size);
            int[] tris = source.triangles;
            Vector3[] positions = source.vertices;
            Vector3[] normals = source.normals;
            Vector2[] uv = source.uv;
            BoneWeight[] weights = source.boneWeights;
            var remap = new Dictionary<int, int>();
            var vOut = new List<Vector3>();
            var nOut = new List<Vector3>();
            var uvOut = new List<Vector2>();
            var wOut = new List<BoneWeight>();
            var iOut = new List<int>(keep.Count * 3);
            foreach (int t in keep)
            {
                for (int k = 0; k < 3; k++)
                {
                    int v = tris[t * 3 + k];
                    if (!remap.TryGetValue(v, out int nv))
                    {
                        nv = vOut.Count;
                        remap[v] = nv;
                        vOut.Add(positions[v]);
                        nOut.Add(normals[v]);
                        uvOut.Add(uv[v]);
                        wOut.Add(weights[v]);
                    }

                    iOut.Add(nv);
                }
            }

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create)
            {
                mesh = new Mesh();
            }

            mesh.Clear();
            mesh.name = Path.GetFileNameWithoutExtension(path);
            mesh.indexFormat = vOut.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vOut);
            mesh.SetNormals(nOut);
            mesh.SetUVs(0, uvOut);
            mesh.boneWeights = wOut.ToArray();
            mesh.bindposes = source.bindposes;
            mesh.SetTriangles(iOut, 0);
            mesh.bounds = source.bounds;
            mesh.UploadMeshData(false);
            if (create)
            {
                EditorUtil.EnsureFolder(MeshFolder);
                AssetDatabase.CreateAsset(mesh, path);
            }

            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>A skinned renderer for <paramref name="glow"/> beside <paramref name="source"/>, on the same bones.</summary>
        public static SkinnedMeshRenderer AddGlowRenderer(SkinnedMeshRenderer source, Mesh glow, Material material)
        {
            string name = source.name + GlowSuffix;
            Transform existing = source.transform.parent.Find(name);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject(name) { layer = source.gameObject.layer };
            go.transform.SetParent(source.transform.parent, false);
            go.transform.SetLocalPositionAndRotation(source.transform.localPosition, source.transform.localRotation);
            go.transform.localScale = source.transform.localScale;
            var skinned = go.AddComponent<SkinnedMeshRenderer>();
            skinned.sharedMesh = glow;
            skinned.bones = source.bones;
            skinned.rootBone = source.rootBone;
            skinned.localBounds = source.localBounds;
            skinned.sharedMaterial = material;
            skinned.quality = source.quality;
            skinned.updateWhenOffscreen = source.updateWhenOffscreen;
            skinned.skinnedMotionVectors = false;
            skinned.shadowCastingMode = ShadowCastingMode.Off;
            skinned.receiveShadows = false;
            skinned.lightProbeUsage = LightProbeUsage.Off;
            skinned.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return skinned;
        }

        /// <summary>
        /// A marker under <paramref name="bone"/> at the bind-pose centre of the glow mesh's vertices that mostly follow
        /// that bone (the lens on the back of a glove). Null when no glowing vertex follows it.
        /// </summary>
        public static Transform AddGlowMarker(SkinnedMeshRenderer glow, Transform bone, string markerName)
        {
            Mesh mesh = glow.sharedMesh;
            int boneIndex = Array.IndexOf(glow.bones, bone);
            if (boneIndex < 0)
            {
                return null;
            }

            Vector3[] positions = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Vector3 sum = Vector3.zero;
            int n = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (weights[i].boneIndex0 == boneIndex && weights[i].weight0 >= 0.5f)
                {
                    sum += positions[i];
                    n++;
                }
            }

            if (n == 0)
            {
                return null;
            }

            Vector3 world = glow.transform.TransformPoint(sum / n); // the bind pose: the instance is not posed
            return AddMarker(bone, markerName, world);
        }

        /// <summary>A marker under <paramref name="bone"/> at the farthest point of the mesh that follows it (an ear tip).</summary>
        public static Transform AddTipMarker(SkinnedMeshRenderer body, Transform bone, string markerName)
        {
            Mesh mesh = body.sharedMesh;
            int boneIndex = Array.IndexOf(body.bones, bone);
            Vector3[] positions = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Vector3 origin = body.transform.InverseTransformPoint(bone.position);
            Vector3 best = origin;
            float far = -1f;
            for (int i = 0; i < positions.Length; i++)
            {
                if (weights[i].boneIndex0 == boneIndex && weights[i].weight0 >= 0.5f)
                {
                    float d = (positions[i] - origin).sqrMagnitude;
                    if (d > far)
                    {
                        far = d;
                        best = positions[i];
                    }
                }
            }

            return AddMarker(bone, markerName, body.transform.TransformPoint(best));
        }

        static Transform AddMarker(Transform bone, string markerName, Vector3 world)
        {
            Transform existing = bone.Find(markerName);
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var marker = new GameObject(markerName) { layer = bone.gameObject.layer }.transform;
            marker.SetParent(bone, false);
            marker.position = world;
            return marker;
        }

        // ---------------------------------------------------------------- effects

        public sealed class FxSet
        {
            public GameObject Spark;
            public GameObject BodySpark;
            public GameObject EarArc;
            public Material Ghost;
        }

        /// <summary>Spark sizes: first person (the gloves are 0.3-0.5 m from the eyes) and on the body seen from metres away.</summary>
        sealed class SparkScale
        {
            public string Name;
            public (float, float) Life, Speed, Size, FlashSize;
            public short Count;
            public float Stretch, Noise, Spread;
        }

        static readonly SparkScale FirstPersonSparkScale = new SparkScale
        {
            Name = SparkPrefabName, Life = (0.05f, 0.11f), Speed = (0.15f, 0.5f), Size = (0.0018f, 0.0032f), FlashSize = (0.008f, 0.012f),
            Count = 5, Stretch = 0.06f, Noise = 0.35f, Spread = 0.006f,
        };

        static readonly SparkScale BodySparkScale = new SparkScale
        {
            Name = BodySparkPrefabName, Life = (0.08f, 0.18f), Speed = (0.6f, 1.6f), Size = (0.006f, 0.012f), FlashSize = (0.04f, 0.06f),
            Count = 7, Stretch = 0.03f, Noise = 1.2f, Spread = 0.015f,
        };

        public static FxSet EnsureFx()
        {
            EditorUtil.EnsureFolder(FxFolder);
            return new FxSet
            {
                Spark = FxPrefabBuilder.EnsureIn(FxFolder, SparkPrefabName, true, () => BuildSpark(FirstPersonSparkScale)),
                BodySpark = FxPrefabBuilder.EnsureIn(FxFolder, BodySparkPrefabName, true, () => BuildSpark(BodySparkScale)),
                EarArc = FxPrefabBuilder.EnsureIn(FxFolder, ArcPrefabName, true, BuildEarArc),
                Ghost = EnsureGhostMaterial(),
            };
        }

        /// <summary>A few white-to-cyan streaks on short jagged paths and a pin-point flash, simulated in the hand's space so they ride along.</summary>
        static GameObject BuildSpark(SparkScale s)
        {
            var root = new GameObject(s.Name);
            root.AddComponent<PooledFx>().Lifetime = 0.3f;
            Material streak = GameAssetFactory.Fx("M_FX_LightningRabbit_Spark", SparkEdge, 4f, 1f, 1.6f);
            Material flash = GameAssetFactory.Fx("M_FX_LightningRabbit_SparkFlash", Color.Lerp(SparkCore, SparkEdge, 0.4f), 2.6f, 1f, 2.2f);

            ParticleSystem sparks = FxPrefabBuilder.AddSystem(root, "Sparks", streak);
            FxPrefabBuilder.Configure(sparks, s.Life, s.Speed, s.Size, SparkCore, SparkEdge, s.Count, 24);
            FxPrefabBuilder.SetShape(sparks, ParticleSystemShapeType.Sphere, s.Spread, 0f);
            FxPrefabBuilder.Stretch(sparks, s.Stretch);
            ParticleSystem.MainModule main = sparks.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            ParticleSystem.NoiseModule noise = sparks.noise; // jagged paths
            noise.enabled = true;
            noise.strength = s.Noise;
            noise.frequency = 8f;
            noise.scrollSpeed = 3f;
            noise.quality = ParticleSystemNoiseQuality.Low;

            ParticleSystem pin = FxPrefabBuilder.AddSystem(root, "Flash", flash);
            FxPrefabBuilder.Configure(pin, (0.04f, 0.06f), (0f, 0f), s.FlashSize, SparkCore, SparkEdge, 1, 2);
            ParticleSystem.MainModule pinMain = pin.main;
            pinMain.simulationSpace = ParticleSystemSimulationSpace.Local;
            return root;
        }

        /// <summary>A short flickering arc (a few centimetres) leaping off an ear tip.</summary>
        static GameObject BuildEarArc()
        {
            var root = new GameObject(ArcPrefabName);
            root.AddComponent<PooledFx>().Lifetime = 0.16f;
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = GameAssetFactory.Fx("M_FX_LightningRabbit_Arc", SparkEdge, 3f, 0f, 1.4f);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.006f;
            line.numCapVertices = 1;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = SparkCore;
            line.endColor = SparkEdge;
            var arc = root.AddComponent<LightningArcFx>();
            EditorUtil.SetInt(arc, "segments", 6);
            EditorUtil.SetFloat(arc, "jitter", 0.3f);
            EditorUtil.SetFloat(arc, "maxJitter", 0.025f);
            EditorUtil.SetFloat(arc, "rerollInterval", 0.03f);
            EditorUtil.SetFloat(arc, "width", 0.006f);
            EditorUtil.SetFloat(arc, "duration", 0.14f);
            return root;
        }

        // ---------------------------------------------------------------- kit

        /// <summary>
        /// Adds the first-person arms' glow copy and lens markers to the kit and an <see cref="ElectricBodyFx"/> wired to
        /// the body's and the arms' glow, markers and the effects.
        /// </summary>
        public static void DecorateKit(CharacterKit kitPrefab, FxSet fx, Material firstPersonGlow)
        {
            string path = AssetDatabase.GetAssetPath(kitPrefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var kit = root.GetComponent<CharacterKit>();
                Transform body = kit.Body.transform;

                // first-person arms: their own UVs and glow mask
                SkinnedMeshRenderer arms = kit.Arms.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .First(s => !s.name.EndsWith(GlowSuffix, StringComparison.Ordinal));
                float[] mask = ReadMask(FirstPersonGlowMaskPath, out int size);
                Mesh glowMesh = BuildGlowMesh(arms.sharedMesh, mask, size, MeshFolder + "/LightningRabbit_FPArms_Glow.asset");
                SkinnedMeshRenderer armsGlow = AddGlowRenderer(arms, glowMesh, firstPersonGlow);
                Transform viewLeft = AddGlowMarker(armsGlow, kit.Arms.Hand(true), LeftLensName);
                Transform viewRight = AddGlowMarker(armsGlow, kit.Arms.Hand(false), RightLensName);

                // body (glow renderers and markers come from the body prefab)
                SkinnedMeshRenderer[] skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Renderer[] bodyGlow = skins.Where(s => s.name.EndsWith(GlowSuffix, StringComparison.Ordinal)).Cast<Renderer>().ToArray();
                Renderer[] surfaces = skins.Where(s => !s.name.EndsWith(GlowSuffix, StringComparison.Ordinal)).Cast<Renderer>().ToArray();
                SkinnedMeshRenderer ghost = skins.Where(s => s.name.EndsWith("_LOD2", StringComparison.Ordinal)).FirstOrDefault()
                                            ?? (SkinnedMeshRenderer)surfaces.Last();
                Transform[] hands = { Find(body, LeftLensName), Find(body, RightLensName) };
                Transform[] ears = { Find(body, LeftEarTipName), Find(body, RightEarTipName) };

                if (!root.TryGetComponent(out ElectricBodyFx electric))
                {
                    electric = root.AddComponent<ElectricBodyFx>();
                }

                electric.Configure(bodyGlow, new Renderer[] { armsGlow }, surfaces, hands.Where(h => h != null).ToArray(),
                    ears.Where(e => e != null).ToArray(), ghost, fx.Ghost, new[] { viewLeft, viewRight }.Where(h => h != null).ToArray(),
                    fx.Spark, fx.BodySpark, fx.EarArc);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static Transform Find(Transform root, string name) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

        /// <summary>Triangle counts of a glow copy and its source, for logs and checks.</summary>
        public static string Describe(SkinnedMeshRenderer glow, SkinnedMeshRenderer source) =>
            $"{glow.name} {glow.sharedMesh.triangles.Length / 3} of {source.sharedMesh.triangles.Length / 3} triangles";
    }
}
