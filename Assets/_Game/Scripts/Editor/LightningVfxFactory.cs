using System;
using System.Collections.Generic;
using System.IO;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CoopDeck.EditorTools
{
    public sealed class LightningVfxSet
    {
        /// <summary>Main bolt with branches (LightningBoltFx); its strength sets the first strike vs a chained one.</summary>
        public GameObject Bolt;

        /// <summary>First-strike hit ("ドン"): chest star flash, glow, shards, sparks, floor ring, light, residual arcs, sound.</summary>
        public GameObject Hit;

        /// <summary>Chained hit ("バチッ"): the same parts at ~70 % and a higher electric sound.</summary>
        public GameObject HitChain;
    }

    /// <summary>
    /// Stylized lightning (Docs/LIGHTNING_VFX.md): writes SG_FX_LightningBolt / SG_FX_LightningFlash (Custom Function graphs
    /// over Art/VFX/Lightning/Shaders/LightningVfx.hlsl, like the fireball's), their materials, and the bolt and hit
    /// prefabs. Everything is rebuilt each run. Reusable by any lightning effect (the Thunderhop's 帯電連射 first).
    /// Quality: branches and residual arcs follow PerformanceSettings.Vfx at play time; the shock ring needs Medium,
    /// the light needs High (and effect lights are off on the Handheld tier anyway).
    /// </summary>
    public static class LightningVfxFactory
    {
        public const string ArtFolder = GamePaths.Root + "/Art/VFX/Lightning";
        public const string ShaderFolder = ArtFolder + "/Shaders";
        public const string MaterialFolder = ArtFolder + "/Materials";
        public const string PrefabFolder = GamePaths.VfxPrefabs + "/Lightning";
        public const string HlslPath = ShaderFolder + "/LightningVfx.hlsl";

        // Design: thin white-hot core, wide cyan body (the Thunderhop's emissive #5ADCF4) deepening to azure at the edge.
        // A dark navy outline was tried and vanished against the blue sky; the cyan body is what reads.
        static readonly Color CoreWhite = Palette.Hex(0xF4FDFF);
        static readonly Color Cyan = Palette.Hex(0x5ADCF4);
        static readonly Color EdgeBlue = Palette.Hex(0x1E8FE6);
        static readonly Color SparkCore = Palette.Hex(0xE8FBFF);
        static readonly Color ShardWhite = Palette.Hex(0xFFFFFF);
        static readonly Color ShardWarm = Palette.Hex(0xFFF1B8);

        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomX,
            ParticleSystemVertexStream.AgePercent,
        };

        sealed class Materials
        {
            public Material Bolt;
            public Material Branch;
            public Material Residual;
            public Material Flash;
            public Material Ring;
            public Material Glow;
            public Material Spark;
            public Material Shard;
        }

        public static LightningVfxSet EnsureAll()
        {
            EditorUtil.EnsureFolder(ShaderFolder);
            EditorUtil.EnsureFolder(MaterialFolder);
            EditorUtil.EnsureFolder(PrefabFolder);
            Materials m = EnsureMaterials(EnsureShaders());
            AudioClip burst = ProceduralAudioFactory.EnsureDischargeSound(ProceduralAudioFactory.DischargeSound.Burst, true);
            AudioClip zap = ProceduralAudioFactory.EnsureDischargeSound(ProceduralAudioFactory.DischargeSound.Chain, true);
            return new LightningVfxSet
            {
                Bolt = FxPrefabBuilder.EnsureIn(PrefabFolder, "FX_Lightning_Bolt", true, () => BuildBolt(m)),
                Hit = FxPrefabBuilder.EnsureIn(PrefabFolder, "FX_Lightning_Hit", true, () => BuildHit("FX_Lightning_Hit", m, 1f, burst, 0.8f)),
                HitChain = FxPrefabBuilder.EnsureIn(PrefabFolder, "FX_Lightning_Hit_Chain", true, () => BuildHit("FX_Lightning_Hit_Chain", m, ChainHitScale, zap, 0.65f)),
            };
        }

        // ------------------------------------------------------------ shaders and materials

        /// <summary>The chained enemy's burst relative to the first one's.</summary>
        public const float ChainHitScale = 0.7f;

        static Dictionary<string, Shader> EnsureShaders()
        {
            AssetDatabase.ImportAsset(HlslPath, ImportAssetOptions.ForceSynchronousImport);
            string hlsl = AssetDatabase.AssetPathToGUID(HlslPath);
            if (string.IsNullOrEmpty(hlsl))
            {
                throw new InvalidOperationException(HlslPath + " is missing");
            }

            const ShaderGraphWriter.Input uv = ShaderGraphWriter.Input.UV0;
            const ShaderGraphWriter.Input color = ShaderGraphWriter.Input.VertexColor;
            return new Dictionary<string, Shader>
            {
                ["SG_FX_LightningBolt"] = WriteGraph("SG_FX_LightningBolt", w => w.FragmentFunction(hlsl, "LightningBolt", ("UV", uv), ("VertexColor", color))),
                ["SG_FX_LightningFlash"] = WriteGraph("SG_FX_LightningFlash", w => w.FragmentFunction(hlsl, "LightningFlash", ("UV", uv), ("VertexColor", color))),
            };
        }

        static Shader WriteGraph(string name, Action<ShaderGraphWriter> functions)
        {
            // Alpha blending: the glow and the edge cover the background, so the colour survives on bright scenes
            // (additive cyan over a bright sky only turned white). The HDR core and glow still bloom.
            var w = new ShaderGraphWriter(name) { BlendMode = ShaderGraphWriter.Blend.Alpha, RenderFace = ShaderGraphWriter.Faces.Both };
            w.ColorProperty("Core Color", "_CoreColor", CoreWhite);
            w.ColorProperty("Glow Color", "_GlowColor", Cyan);
            w.ColorProperty("Edge Color", "_EdgeColor", EdgeBlue);
            w.FloatProperty("Intensity", "_Intensity", 1f, 0f, 4f);
            w.FloatProperty("Core Boost", "_CoreBoost", 4f, 1f, 10f);
            w.FloatProperty("Core Width", "_CoreWidth", 0.24f, 0.02f, 1f);
            w.FloatProperty("Glow Falloff", "_GlowFalloff", 3f, 0.2f, 20f);
            w.FloatProperty("Halo Opacity", "_HaloOpacity", 0.85f, 0f, 1f);
            w.FloatProperty("Noise Scale", "_NoiseScale", 6f, 0.1f, 30f);
            w.FloatProperty("Flow Speed", "_FlowSpeed", 9f, 0f, 40f);
            w.FloatProperty("Pulse Count", "_PulseCount", 1.5f, 0f, 8f);
            w.FloatProperty("Pulse Speed", "_PulseSpeed", 5f, 0f, 30f);
            w.FloatProperty("Rays", "_Rays", 7f, 2f, 16f);
            w.FloatProperty("Mode", "_Mode", 0f, 0f, 2f);
            w.FloatProperty("Seed", "_Seed", 0f, 0f, 100f, slider: false);
            w.Begin();
            functions(w);

            string path = $"{ShaderFolder}/{name}.shadergraph";
            string text = w.Build();
            if (!File.Exists(path) || File.ReadAllText(path) != text)
            {
                File.WriteAllText(path, text);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                throw new InvalidOperationException(path + " did not import as a shader");
            }

            return shader;
        }

        static Materials EnsureMaterials(Dictionary<string, Shader> s)
        {
            Shader bolt = s["SG_FX_LightningBolt"];
            Shader flash = s["SG_FX_LightningFlash"];
            return new Materials
            {
                // Main bolt: a thin white-hot core in a wide cyan body that deepens to azure, covering the background.
                // The core stays at ~3x (4.5x bloomed into a white smear that drowned the colour on bright scenes).
                Bolt = Mat("M_FX_Lightning_Bolt", bolt, m => Set(Glow(m, 1.2f), ("_Intensity", 1f), ("_CoreBoost", 3.2f), ("_CoreWidth", 0.11f), ("_GlowFalloff", 1.5f),
                    ("_HaloOpacity", 0.9f), ("_NoiseScale", 6f), ("_FlowSpeed", 9f), ("_PulseCount", 1.5f), ("_PulseSpeed", 5f), ("_Seed", 0f))),
                Branch = Mat("M_FX_Lightning_Branch", bolt, m => Set(Glow(m, 1.1f), ("_Intensity", 0.95f), ("_CoreBoost", 2.6f), ("_CoreWidth", 0.15f), ("_GlowFalloff", 1.8f),
                    ("_HaloOpacity", 0.85f), ("_NoiseScale", 8f), ("_FlowSpeed", 12f), ("_PulseCount", 0f), ("_PulseSpeed", 0f), ("_Seed", 7f))),
                Residual = Mat("M_FX_Lightning_Residual", bolt, m => Set(Glow(m, 1.1f), ("_Intensity", 1f), ("_CoreBoost", 2.6f), ("_CoreWidth", 0.17f), ("_GlowFalloff", 2.2f),
                    ("_HaloOpacity", 0.85f), ("_NoiseScale", 10f), ("_FlowSpeed", 18f), ("_PulseCount", 0f), ("_PulseSpeed", 0f), ("_Seed", 13f))),
                // Star flash: a white middle about half the body wide (falloff 16 over the 2.1 m quad), spikes past the outline.
                Flash = Mat("M_FX_Lightning_Flash", flash, m => Set(Glow(m, 1.4f), ("_Mode", 0f), ("_Intensity", 1f), ("_CoreBoost", 5f), ("_GlowFalloff", 16f), ("_Rays", 8f), ("_HaloOpacity", 1f))),
                // Floor shock ring: seen at a grazing angle, so a bright, broad band with a white-hot middle.
                Ring = Mat("M_FX_Lightning_Ring", flash, m => Set(Glow(m, 2f), ("_Mode", 1f), ("_Intensity", 1f), ("_CoreBoost", 3f), ("_GlowFalloff", 4f), ("_HaloOpacity", 1f))),
                // A see-through blue tint around the hit (it used to be a white blob that hid the enemy).
                Glow = Mat("M_FX_Lightning_Glow", flash, m => Set(Glow(m, 1.2f), ("_Mode", 2f), ("_Intensity", 1f), ("_CoreBoost", 1f), ("_GlowFalloff", 3f), ("_HaloOpacity", 0.45f))),
                Spark = GameAssetFactory.Fx("M_FX_Lightning_Spark", SparkCore, 3f, 1f, 1.2f),
                Shard = GameAssetFactory.Fx("M_FX_Lightning_Shard", ShardWhite, 3.4f, 1f, 1.1f),
            };
        }

        static Material Mat(string name, Shader shader, Action<Material> configure)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_CoreColor", CoreWhite);
            material.SetColor("_GlowColor", Cyan);
            material.SetColor("_EdgeColor", EdgeBlue);
            configure(material);
            material.renderQueue = (int)RenderQueue.Transparent;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>The cyan glow a little above 1 (HDR): it blooms a little, but much more than ~1.2 turns white in the tonemapper.</summary>
        static Material Glow(Material m, float boost)
        {
            m.SetColor("_GlowColor", Cyan * boost);
            return m;
        }

        static void Set(Material m, params (string name, float value)[] values)
        {
            foreach ((string name, float value) in values)
            {
                m.SetFloat(name, value);
            }
        }

        // ------------------------------------------------------------ prefabs

        static GameObject Root(string name, float lifetime)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = lifetime;
            return root;
        }

        static LineRenderer Strip(Transform parent, string name, Material material, AnimationCurve widthCurve)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.alignment = LineAlignment.View;
            line.textureMode = LineTextureMode.Stretch;
            line.numCornerVertices = 2; // thick strips keep their joints closed at the zig-zag kinks
            line.numCapVertices = 0;    // the shader fades the ends
            line.widthCurve = widthCurve;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.allowOcclusionWhenDynamic = false;
            line.enabled = false;
            return line;
        }

        static GameObject BuildBolt(Materials m)
        {
            GameObject root = Root("FX_Lightning_Bolt", 0.3f); // longer than a hop's travel plus its fade (LightningBoltFx.Duration)
            // Thin at the far end (the sky, or the enemy a hop leaves), thickest at the impact: the white core gathers there.
            var mainCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.45f, 0.65f), new Keyframe(0.82f, 1.1f), new Keyframe(1f, 1.05f));
            var branchCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.15f));
            LineRenderer main = Strip(root.transform, "Main", m.Bolt, mainCurve);
            var branches = new LineRenderer[4];
            for (int i = 0; i < branches.Length; i++)
            {
                branches[i] = Strip(root.transform, "Branch" + i, m.Branch, branchCurve);
            }

            var boltFx = root.AddComponent<LightningBoltFx>();
            boltFx.Configure(main, branches);
            EditorUtil.SetFloat(boltFx, "width", 0.6f);
            return root;
        }

        /// <param name="scale">1 = the first strike; <see cref="ChainHitScale"/> = a chained enemy (smaller and a little dimmer).</param>
        /// <param name="sound">Played as the hit is spawned (the impact frame).</param>
        static GameObject BuildHit(string name, Materials m, float scale, AudioClip sound, float volume)
        {
            GameObject root = Root(name, 0.5f);
            float dim = Mathf.Lerp(0.85f, 1f, (scale - 0.6f) / 0.4f);
            var white = new Color(dim, dim, dim, 1f);

            // On the chest: a white star whose middle is about half the body wide and whose spikes reach past the
            // outline (peak ~45 ms), and a see-through cyan tint around the body (the peak is the enemy lighting up,
            // never a white screen). Screen caps keep it off the whole view up close.
            ParticleSystem flash = Particles(root, "Flash", m.Flash, (0.045f, 0.05f), (2f * scale, 2.2f * scale), white, white, 1, 0.22f);
            RandomRotation(flash);
            NoSizeOverLifetime(flash);

            ParticleSystem glow = Particles(root, "Glow", m.Glow, (0.12f, 0.15f), (1.7f * scale, 1.9f * scale), white, white, 1, 0.3f);
            NoSizeOverLifetime(glow);

            // 8–12 short thick white-to-warm-white shards thrown outward, plus a few thin sparks.
            ParticleSystem shards = FxPrefabBuilder.AddSystem(root, "Shards", m.Shard);
            FxPrefabBuilder.Configure(shards, (0.08f, 0.14f), (7f * scale, 12f * scale), (0.07f * scale, 0.11f * scale), ShardWhite, ShardWarm, 10, 16);
            ParticleSystem.EmissionModule shardBurst = shards.emission;
            shardBurst.SetBursts(new[] { new ParticleSystem.Burst(0f, 8, 12) });
            FxPrefabBuilder.SetShape(shards, ParticleSystemShapeType.Sphere, 0.12f, 0f);
            FxPrefabBuilder.Stretch(shards, 0.025f);
            ParticleSystem.MainModule shardMain = shards.main;
            shardMain.gravityModifier = 0.5f;

            ParticleSystem sparks = FxPrefabBuilder.AddSystem(root, "Sparks", m.Spark);
            FxPrefabBuilder.Configure(sparks, (0.08f, 0.2f), (6f * scale, 13f * scale), (0.016f, 0.03f), SparkCore, Cyan, (short)Mathf.RoundToInt(10 * scale), 16);
            FxPrefabBuilder.SetShape(sparks, ParticleSystemShapeType.Sphere, 0.1f, 0f);
            FxPrefabBuilder.Stretch(sparks, 0.035f);
            ParticleSystem.MainModule sparkMain = sparks.main;
            sparkMain.gravityModifier = 0.8f;

            // On the floor under the enemy (LightningImpactFx moves it there; none in the air): a cyan shock ring that
            // spreads and fades in ~0.2 s from just after the flash peak, so the hit reads as an upright burst.
            ParticleSystem ring = Particles(root, "GroundRing", m.Ring, (0.18f, 0.2f), (3f * scale, 3f * scale), white, white, 1, 0.6f);
            NoSizeOverLifetime(ring);
            ring.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.MainModule ringMain = ring.main;
            ringMain.startDelay = 0.02f;
            ring.gameObject.AddComponent<VfxQualityGate>().MinimumQuality = VfxQuality.Medium;

            // Light pulse (High only; effect lights are off on Handheld).
            FxPrefabBuilder.AddFlashLight(root, Cyan, 5f * scale);
            root.transform.Find("Light").gameObject.AddComponent<VfxQualityGate>().MinimumQuality = VfxQuality.High;

            // Current crawling on the enemy after the burst, briefly.
            var residualGo = new GameObject("Residual");
            residualGo.transform.SetParent(root.transform, false);
            var arcCurve = new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.6f));
            var arcs = new LineRenderer[3];
            for (int i = 0; i < arcs.Length; i++)
            {
                arcs[i] = Strip(residualGo.transform, "Arc" + i, m.Residual, arcCurve);
            }

            var residual = residualGo.AddComponent<SurfaceArcFx>();
            residual.Configure(arcs, 0.42f * Mathf.Lerp(0.8f, 1f, scale), scale >= 0.9f ? 0.18f : 0.15f);

            // The beat's sound, on the frame the hit shows.
            var soundGo = new GameObject("Sound");
            soundGo.transform.SetParent(root.transform, false);
            var source = soundGo.AddComponent<AudioSource>();
            source.clip = sound;
            source.playOnAwake = false;
            source.volume = volume;
            source.spatialBlend = 0.7f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 3f;
            source.maxDistance = 55f;
            source.dopplerLevel = 0f;

            // The whole burst follows the enemy's size (LightningImpactFx scales the root).
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }

            root.AddComponent<LightningImpactFx>().Configure(ring.transform, source, WeaponBodyWidth);
            EditorUtil.SetBool(root.GetComponent<PooledFx>(), "scaleWithEffectDensity", false); // 8–12 shards at every quality
            return root;
        }

        /// <summary>The body width the hit prefabs are built for (WeaponFx.DefaultBodyWidth, a humanoid chest).</summary>
        const float WeaponBodyWidth = 0.8f;

        static ParticleSystem Particles(GameObject root, string name, Material material, (float min, float max) life, (float min, float max) size,
            Color a, Color b, short count, float maxScreenSize)
        {
            ParticleSystem ps = FxPrefabBuilder.AddSystem(root, name, material);
            FxPrefabBuilder.Configure(ps, life, (0f, 0f), size, a, b, count, Mathf.Max(2, count));
            FxPrefabBuilder.SetShape(ps, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.SetActiveVertexStreams(Streams);
            renderer.maxParticleSize = maxScreenSize;
            return ps;
        }

        static void RandomRotation(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        static void NoSizeOverLifetime(ParticleSystem ps)
        {
            ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
            size.enabled = false;
        }
    }
}
