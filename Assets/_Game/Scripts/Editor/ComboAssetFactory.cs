using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    public sealed class ComboAssetSet
    {
        public readonly List<ComboDefinition> Combos = new List<ComboDefinition>();
        public GameObject FireAura;
        public GameObject IceAura;
        public GameObject LightningAura;
        public GameObject GravityAura;
        public GameObject CoopFx;
    }

    /// <summary>
    /// Element combos: the three <see cref="ComboDefinition"/> assets (numbers are kept once the
    /// asset exists; effect references are refreshed) and their effects, built from particles,
    /// trails and line arcs with the project's own additive shader. Each combo has three tiers
    /// (normal / enhanced / ultimate) that add layers; optional layers carry a
    /// <see cref="VfxQualityGate"/>. Particles are capped to a fraction of the screen so a combo
    /// right next to the camera never hides the enemies.
    /// </summary>
    public static class ComboAssetFactory
    {
        public const string DataFolder = GamePaths.Root + "/ScriptableObjects/Combos";
        public const string FxFolder = GamePaths.Root + "/Prefabs/Combos";

        /// <summary>No particle may grow beyond this fraction of the screen.</summary>
        const float MaxScreenFraction = 0.18f;

        static readonly string[] TierNames = { "Normal", "Enhanced", "Ultimate" };

        static readonly Color IceColor = new Color(0.45f, 0.88f, 1f);
        static readonly Color LightningColor = new Color(1f, 0.92f, 0.35f);
        static readonly Color FireColor = new Color(1f, 0.45f, 0.15f);
        static readonly Color GravityColor = new Color(0.62f, 0.32f, 1f);

        public static ComboAssetSet EnsureCombos()
        {
            EditorUtil.EnsureFolder(DataFolder);
            EditorUtil.EnsureFolder(FxFolder);
            var set = new ComboAssetSet();

            // ---------------- superconductor burst: ice + lightning, chained lightning
            GameObject arc = Fx("FX_Combo_Arc", BuildArc);
            GameObject scHit = Fx("FX_Combo_Superconductor_Hit", () => BuildSmallBurst("FX_Combo_Superconductor_Hit", IceColor, LightningColor));
            ComboDefinition superconductor = EnsureDefinition("CB_Superconductor", d =>
            {
                d.comboId = "combo.superconductor";
                d.displayName = "超伝導バースト";
                d.color = new Color(0.55f, 0.92f, 1f);
                d.elementA = Element.Ice;
                d.elementB = Element.Lightning;
                d.kind = ComboKind.ChainBurst;
                d.tiers = new[]
                {
                    new ComboTierSettings { damage = 45f, radius = 0f, chainCount = 3, chainRange = 9f, chainFalloff = 0.85f, shake = 0.18f },
                    new ComboTierSettings { damage = 60f, radius = 3f, chainCount = 5, chainRange = 11f, chainFalloff = 0.88f, shake = 0.26f },
                    new ComboTierSettings { damage = 85f, radius = 6f, chainCount = 8, chainRange = 14f, chainFalloff = 0.9f, shake = 0.38f },
                };
            });
            superconductor.arcFx = arc;
            for (int t = 0; t < ComboDefinition.TierCount; t++)
            {
                int tier = t;
                string name = $"FX_Combo_Superconductor_{TierNames[t]}";
                superconductor.tiers[t].fx = Fx(name, () => BuildSuperconductor(name, tier));
                superconductor.tiers[t].hitFx = scHit;
            }

            // ---------------- burning singularity: fire + gravity, pull then blast
            ComboDefinition singularity = EnsureDefinition("CB_BurningSingularity", d =>
            {
                d.comboId = "combo.burning_singularity";
                d.displayName = "灼熱特異点";
                d.color = new Color(1f, 0.5f, 0.35f);
                d.elementA = Element.Fire;
                d.elementB = Element.Gravity;
                d.kind = ComboKind.Singularity;
                d.tiers = new[]
                {
                    new ComboTierSettings { damage = 60f, radius = 4f, pullRadius = 7f, pullSpeed = 8f, pullDuration = 0.9f, shake = 0.22f },
                    new ComboTierSettings { damage = 80f, radius = 5.5f, pullRadius = 9f, pullSpeed = 10f, pullDuration = 1f, shake = 0.3f },
                    new ComboTierSettings { damage = 115f, radius = 8f, pullRadius = 12f, pullSpeed = 12f, pullDuration = 1.1f, shake = 0.42f },
                };
            });
            for (int t = 0; t < ComboDefinition.TierCount; t++)
            {
                int tier = t;
                string core = $"FX_Combo_Singularity_{TierNames[t]}";
                string blast = $"FX_Combo_SingularityBlast_{TierNames[t]}";
                float pull = singularity.tiers[t].pullDuration;
                singularity.tiers[t].fx = Fx(core, () => BuildSingularityCore(core, tier, pull));
                singularity.tiers[t].hitFx = Fx(blast, () => BuildExplosion(blast, tier, FireColor, GravityColor, fused: tier == 2));
            }

            // ---------------- overload: fire + lightning, one big explosion
            ComboDefinition overload = EnsureDefinition("CB_Overload", d =>
            {
                d.comboId = "combo.overload";
                d.displayName = "オーバーロード";
                d.color = new Color(1f, 0.78f, 0.3f);
                d.elementA = Element.Fire;
                d.elementB = Element.Lightning;
                d.kind = ComboKind.Explosion;
                d.tiers = new[]
                {
                    new ComboTierSettings { damage = 55f, radius = 4f, shake = 0.22f },
                    new ComboTierSettings { damage = 75f, radius = 6f, shake = 0.3f },
                    new ComboTierSettings { damage = 105f, radius = 8.5f, shake = 0.42f },
                };
            });
            for (int t = 0; t < ComboDefinition.TierCount; t++)
            {
                int tier = t;
                string name = $"FX_Combo_Overload_{TierNames[t]}";
                overload.tiers[t].fx = Fx(name, () => BuildExplosion(name, tier, FireColor, LightningColor, fused: tier == 2));
                overload.tiers[t].hitFx = null;
            }

            // Co-op overlay (shared by the three combos) and the combo sounds.
            GameObject coopFx = Fx("FX_Combo_Coop", BuildCoopBurst);
            set.CoopFx = coopFx;
            superconductor.sound = ProceduralAudioFactory.EnsureComboSound(ProceduralAudioFactory.ComboSound.Superconductor, false);
            superconductor.coopSound = ProceduralAudioFactory.EnsureComboSound(ProceduralAudioFactory.ComboSound.Superconductor, true);
            singularity.sound = ProceduralAudioFactory.EnsureComboSound(ProceduralAudioFactory.ComboSound.Singularity, false);
            singularity.coopSound = ProceduralAudioFactory.EnsureComboSound(ProceduralAudioFactory.ComboSound.Singularity, true);
            overload.sound = ProceduralAudioFactory.EnsureComboSound(ProceduralAudioFactory.ComboSound.Overload, false);
            overload.coopSound = ProceduralAudioFactory.EnsureComboSound(ProceduralAudioFactory.ComboSound.Overload, true);

            foreach (ComboDefinition combo in new[] { superconductor, singularity, overload })
            {
                combo.coopFx = coopFx;
                if (combo.coopShakeMultiplier < 1f) combo.coopShakeMultiplier = 1.4f;
                EditorUtility.SetDirty(combo);
                set.Combos.Add(combo);
            }

            set.FireAura = Fx("FX_Aura_Fire", () => BuildAura("FX_Aura_Fire", Element.Fire));
            set.IceAura = Fx("FX_Aura_Ice", () => BuildAura("FX_Aura_Ice", Element.Ice));
            set.LightningAura = Fx("FX_Aura_Lightning", () => BuildAura("FX_Aura_Lightning", Element.Lightning));
            set.GravityAura = Fx("FX_Aura_Gravity", () => BuildAura("FX_Aura_Gravity", Element.Gravity));

            AssetDatabase.SaveAssets();
            return set;
        }

        static ComboDefinition EnsureDefinition(string assetName, System.Action<ComboDefinition> initialize)
        {
            string path = $"{DataFolder}/{assetName}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<ComboDefinition>(path);
            if (definition != null)
            {
                if (definition.tiers == null || definition.tiers.Length != ComboDefinition.TierCount)
                {
                    initialize(definition); // repair a broken asset
                }

                return definition;
            }

            definition = ScriptableObject.CreateInstance<ComboDefinition>();
            initialize(definition);
            AssetDatabase.CreateAsset(definition, path);
            return definition;
        }

        static GameObject Fx(string name, System.Func<GameObject> build) => FxPrefabBuilder.EnsureIn(FxFolder, name, true, build);

        // ------------------------------------------------------------ materials

        static Material Dot(string name, Color color, float intensity = 2.4f, float falloff = 1.3f) =>
            GameAssetFactory.Fx(name, color, intensity, 1f, falloff);

        static Material Band(string name, Color color, float intensity = 2.8f) =>
            GameAssetFactory.Fx(name, color, intensity, 0f, 1.4f);

        static Material Ring(string name, Color color, float intensity, float thickness)
        {
            Material m = GameAssetFactory.Fx(name, color, intensity, 1f, 1.2f);
            m.SetFloat("_Ring", thickness);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------ building blocks

        static ParticleSystem Burst(GameObject root, string name, Material material, (float, float) life, (float, float) speed,
            (float, float) size, Color a, Color b, short count, int max, VfxQuality? gate = null, float delay = 0f)
        {
            GameObject parent = root;
            if (gate.HasValue)
            {
                parent = new GameObject(name + "_Layer");
                parent.transform.SetParent(root.transform, false);
                parent.AddComponent<VfxQualityGate>().MinimumQuality = gate.Value;
            }

            ParticleSystem ps = FxPrefabBuilder.AddSystem(parent, name, material);
            FxPrefabBuilder.Configure(ps, life, speed, size, a, b, count, max);
            ParticleSystem.MainModule main = ps.main;
            main.startDelay = delay;
            main.scalingMode = ParticleSystemScalingMode.Shape; // the root scale grows the area, not the sprite
            ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = MaxScreenFraction;
            return ps;
        }

        /// <summary>A flat expanding (or shrinking) ring on the ground, sized to the effect radius.</summary>
        static ParticleSystem GroundRing(GameObject root, string name, Material material, Color color, float life, float fromSize, float toSize,
            VfxQuality? gate = null, float delay = 0f, float height = 0.06f)
        {
            ParticleSystem ps = Burst(root, name, material, (life, life), (0f, 0f), (2f, 2f), color, color, 1, 2, gate, delay);
            ps.transform.localPosition = new Vector3(0f, height, 0f);
            FxPrefabBuilder.SetShape(ps, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; // diameter 2 = the effect radius
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.maxParticleSize = 50f; // flat on the ground: never covers the view
            ParticleSystem.SizeOverLifetimeModule grow = ps.sizeOverLifetime;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, fromSize / 2f, 1f, toSize / 2f));
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.color = new ParticleSystem.MinMaxGradient(Fade(0.9f, 0.6f));
            FullLayers.Add(ps.transform);
            return ps;
        }

        static Gradient Fade(float startAlpha, float holdUntil)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(startAlpha, 0f), new GradientAlphaKey(startAlpha * 0.8f, holdUntil), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        static readonly List<Transform> FullLayers = new List<Transform>();
        static readonly List<Transform> PartialLayers = new List<Transform>();

        static GameObject Root(string name, float lifetime)
        {
            FullLayers.Clear();
            PartialLayers.Clear();
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = lifetime;
            return root;
        }

        /// <summary>Lets the combo system size the rings and areas to the combo radius (see RadiusScaledFx).</summary>
        static GameObject AttachSizing(GameObject root, SpiralTrailFx spiral)
        {
            root.AddComponent<RadiusScaledFx>().Configure(FullLayers.ToArray(), PartialLayers.ToArray(), spiral);
            return root;
        }

        static void Light(GameObject root, Color color, float range, VfxQuality gate = VfxQuality.High)
        {
            var holder = new GameObject("LightLayer");
            holder.transform.SetParent(root.transform, false);
            holder.AddComponent<VfxQualityGate>().MinimumQuality = gate;
            FxPrefabBuilder.AddFlashLight(holder, color, range);
        }

        // ------------------------------------------------------------ superconductor

        static GameObject BuildSuperconductor(string name, int tier)
        {
            GameObject root = Root(name, 1.2f + tier * 0.2f);
            Material dot = Dot("M_FX_Combo_Ice", IceColor, 2.6f);
            Material spark = Dot("M_FX_Combo_Spark", LightningColor, 3f, 1.6f);
            Material ring = Ring("M_FX_Combo_Ring_Ice", IceColor, 2.4f, 0.22f);
            Material ring2 = Ring("M_FX_Combo_Ring_Lightning", LightningColor, 2.2f, 0.14f);
            Vector3 chest = new Vector3(0f, 1.1f, 0f);

            ParticleSystem flash = Burst(root, "Flash", dot, (0.12f, 0.16f), (0f, 0f), (1.4f + tier * 0.5f, 1.8f + tier * 0.6f),
                Color.white, IceColor, 1, 2);
            flash.transform.localPosition = chest;
            FxPrefabBuilder.SetShape(flash, ParticleSystemShapeType.Sphere, 0.001f, 0f);

            short shards = (short)(18 + tier * 14);
            ParticleSystem ice = Burst(root, "IceShards", dot, (0.35f, 0.7f), (4f + tier, 9f + tier * 2.5f), (0.08f, 0.2f),
                IceColor, Color.white, shards, 96);
            ice.transform.localPosition = chest;
            FxPrefabBuilder.SetShape(ice, ParticleSystemShapeType.Sphere, 0.25f, 0f);
            FxPrefabBuilder.Stretch(ice, 0.03f);
            ParticleSystem.MainModule iceMain = ice.main;
            iceMain.gravityModifier = 0.7f;
            PartialLayers.Add(ice.transform);

            ParticleSystem sparks = Burst(root, "Sparks", spark, (0.15f, 0.32f), (6f, 13f + tier * 3f), (0.03f, 0.07f),
                LightningColor, Color.white, (short)(12 + tier * 12), 96, VfxQuality.Medium);
            sparks.transform.localPosition = chest;
            FxPrefabBuilder.SetShape(sparks, ParticleSystemShapeType.Sphere, 0.2f, 0f);
            FxPrefabBuilder.Stretch(sparks, 0.025f);

            GroundRing(root, "FrostRing", ring, IceColor, 0.4f + tier * 0.05f, 0.3f, 2.2f);
            if (tier >= 1)
            {
                GroundRing(root, "VoltRing", ring2, LightningColor, 0.35f, 0.2f, 2.6f, VfxQuality.Medium, 0.08f);
                ParticleSystem mist = Burst(root, "FrostMist", Dot("M_FX_Combo_Mist", IceColor, 0.9f, 2f), (0.5f, 0.8f), (0.3f, 1f), (0.8f, 1.3f),
                    IceColor, Color.white, (short)(6 + tier * 4), 24, VfxQuality.Medium);
                mist.transform.localPosition = new Vector3(0f, 0.5f, 0f);
                FxPrefabBuilder.SetShape(mist, ParticleSystemShapeType.Sphere, 0.8f, 0f);
                PartialLayers.Add(mist.transform);
            }

            if (tier >= 2)
            {
                // Fused: a frozen pillar shot through with lightning.
                ParticleSystem pillar = Burst(root, "CrystalPillar", dot, (0.5f, 0.8f), (6f, 11f), (0.14f, 0.3f),
                    IceColor, Color.white, 30, 64, VfxQuality.Medium);
                FxPrefabBuilder.SetShape(pillar, ParticleSystemShapeType.Cone, 0.35f, 6f);
                ParticleSystem.ShapeModule shape = pillar.shape;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                FxPrefabBuilder.Stretch(pillar, 0.05f);

                ParticleSystem orb = Burst(root, "StormOrb", spark, (0.35f, 0.45f), (0f, 0f), (2.4f, 2.8f),
                    LightningColor, IceColor, 1, 2, VfxQuality.Medium);
                orb.transform.localPosition = chest;
                FxPrefabBuilder.SetShape(orb, ParticleSystemShapeType.Sphere, 0.001f, 0f);
                GroundRing(root, "OuterRing", ring, IceColor, 0.55f, 1f, 2.4f, VfxQuality.High, 0.15f);
            }

            Light(root, IceColor, 6f + tier * 3f);
            return AttachSizing(root, null);
        }

        static GameObject BuildSmallBurst(string name, Color a, Color b)
        {
            GameObject root = Root(name, 0.6f);
            Material dot = Dot("M_FX_Combo_Ice", IceColor, 2.6f);
            Material spark = Dot("M_FX_Combo_Spark", LightningColor, 3f, 1.6f);
            ParticleSystem flash = Burst(root, "Flash", dot, (0.08f, 0.12f), (0f, 0f), (0.8f, 1f), Color.white, a, 1, 2);
            FxPrefabBuilder.SetShape(flash, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            ParticleSystem sparks = Burst(root, "Sparks", spark, (0.12f, 0.25f), (4f, 9f), (0.03f, 0.06f), b, Color.white, 12, 32);
            FxPrefabBuilder.SetShape(sparks, ParticleSystemShapeType.Sphere, 0.15f, 0f);
            FxPrefabBuilder.Stretch(sparks, 0.025f);
            return root;
        }

        static GameObject BuildArc()
        {
            GameObject root = Root("FX_Combo_Arc", 0.45f);
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = Band("M_FX_Combo_Arc", Color.Lerp(LightningColor, IceColor, 0.35f), 3.2f);
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.14f;
            line.numCapVertices = 2;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = Color.white;
            line.endColor = Color.Lerp(LightningColor, IceColor, 0.5f);
            var arc = root.AddComponent<LightningArcFx>();
            EditorUtil.SetFloat(arc, "width", 0.14f);
            EditorUtil.SetFloat(arc, "duration", 0.38f);
            return root;
        }

        // ------------------------------------------------------------ burning singularity

        static GameObject BuildSingularityCore(string name, int tier, float pullDuration)
        {
            GameObject root = Root(name, pullDuration + 0.25f);
            Material violet = Dot("M_FX_Combo_Gravity", GravityColor, 2.2f, 1.1f);
            Material ember = Dot("M_FX_Combo_Ember", FireColor, 2.1f, 1.6f);
            Material ring = Ring("M_FX_Combo_Ring_Gravity", GravityColor, 2f, 0.3f);
            Vector3 core = new Vector3(0f, 1f, 0f);

            // Dark-violet core that swells while it pulls.
            ParticleSystem heart = Burst(root, "Core", violet, (pullDuration, pullDuration), (0f, 0f), (0.9f + tier * 0.3f, 1.1f + tier * 0.3f),
                GravityColor, FireColor, 1, 2);
            heart.transform.localPosition = core;
            FxPrefabBuilder.SetShape(heart, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            ParticleSystem.SizeOverLifetimeModule swell = heart.sizeOverLifetime;
            swell.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.3f, 1f, 1.2f));

            // Embers drawn inward along the ground plane (negative speed from a flat ring), so
            // nothing floats up into the sky and the pull reads as a horizontal suction.
            ParticleSystem inward = Burst(root, "Inflow", ember, (0.5f, 0.65f), (-11f, -7f), (0.06f, 0.13f),
                FireColor, GravityColor, (short)(20 + tier * 15), 96);
            inward.transform.localPosition = new Vector3(0f, 0.6f, 0f);
            FxPrefabBuilder.SetShape(inward, ParticleSystemShapeType.Circle, 1f, 0f);
            ParticleSystem.ShapeModule inShape = inward.shape;
            inShape.radiusThickness = 0.25f;
            inShape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.MainModule inMain = inward.main;
            inMain.scalingMode = ParticleSystemScalingMode.Shape;
            inMain.duration = pullDuration * 0.7f;
            ParticleSystem.EmissionModule emission = inward.emission;
            emission.rateOverTime = 40f + tier * 30f;
            FullLayers.Add(inward.transform);

            // Vortex ring shrinking toward the centre on the ground.
            GroundRing(root, "Vortex", ring, GravityColor, pullDuration, 2.2f, 0.4f);

            // Trail embers on an inward spiral.
            var spiralGo = new GameObject("SpiralTrails");
            spiralGo.transform.SetParent(root.transform, false);
            spiralGo.AddComponent<VfxQualityGate>().MinimumQuality = tier >= 1 ? VfxQuality.Medium : VfxQuality.High;
            var spiral = spiralGo.AddComponent<SpiralTrailFx>();
            EditorUtil.SetFloat(spiral, "duration", pullDuration);
            EditorUtil.SetFloat(spiral, "startRadius", 1f);
            EditorUtil.SetFloat(spiral, "endRadius", 0.08f);
            EditorUtil.SetFloat(spiral, "turnsPerSecond", 1.2f + tier * 0.3f);
            EditorUtil.SetFloat(spiral, "height", 1.2f);
            Material trailMaterial = Band("M_FX_Combo_Trail", FireColor, 2.6f);
            int trails = 6 + tier * 4;
            for (int i = 0; i < trails; i++)
            {
                var emitter = new GameObject($"Ember{i}");
                emitter.transform.SetParent(spiralGo.transform, false);
                var trail = emitter.AddComponent<TrailRenderer>();
                trail.sharedMaterial = trailMaterial;
                trail.time = 0.25f;
                trail.minVertexDistance = 0.05f;
                trail.widthMultiplier = 0.09f;
                trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
                var colors = new Gradient();
                colors.SetKeys(
                    new[] { new GradientColorKey(Color.Lerp(FireColor, Color.white, 0.4f), 0f), new GradientColorKey(GravityColor, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                trail.colorGradient = colors;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.numCapVertices = 1;
            }

            if (tier >= 1)
            {
                // Violet sparks circling the core.
                ParticleSystem orbit = Burst(root, "OrbitSparks", Dot("M_FX_Combo_GravitySpark", GravityColor, 3f, 1.6f), (0.3f, 0.5f), (-3f, -1.5f),
                    (0.04f, 0.08f), GravityColor, Color.white, 0, 64, VfxQuality.Medium);
                orbit.transform.localPosition = core;
                FxPrefabBuilder.SetShape(orbit, ParticleSystemShapeType.Sphere, 1.4f, 0f);
                FxPrefabBuilder.Stretch(orbit, 0.03f);
                ParticleSystem.MainModule orbitMain = orbit.main;
                orbitMain.duration = pullDuration * 0.8f;
                ParticleSystem.EmissionModule orbitEmission = orbit.emission;
                orbitEmission.SetBursts(new ParticleSystem.Burst[0]);
                orbitEmission.rateOverTime = 30f + tier * 20f;
            }

            if (tier >= 2)
            {
                GroundRing(root, "OuterVortex", Ring("M_FX_Combo_Ring_Fire", FireColor, 1.8f, 0.18f), FireColor, pullDuration, 2.4f, 0.6f, VfxQuality.Medium);
            }

            return AttachSizing(root, spiral);
        }

        // ------------------------------------------------------------ explosions (overload, singularity blast)

        static GameObject BuildExplosion(string name, int tier, Color main, Color accent, bool fused)
        {
            GameObject root = Root(name, 1.2f + tier * 0.2f);
            string mainName = main == FireColor ? "Fire" : main == GravityColor ? "Gravity" : "Lightning";
            string accentName = accent == FireColor ? "Fire" : accent == GravityColor ? "Gravity" : "Lightning";
            Material mainDot = Dot($"M_FX_Combo_{mainName}Dot", main, 2.2f, 1.5f);
            Material accentDot = Dot($"M_FX_Combo_{accentName}Dot", accent, 3f, 1.6f);
            Material ring = Ring($"M_FX_Combo_Ring_{mainName}", main, 2.4f, 0.2f);
            Material accentRing = Ring($"M_FX_Combo_Ring_{accentName}", accent, 2.2f, 0.14f);
            Vector3 center = new Vector3(0f, 0.9f, 0f);

            ParticleSystem flash = Burst(root, "Flash", mainDot, (0.12f, 0.18f), (0f, 0f), (1.6f + tier * 0.6f, 2f + tier * 0.7f),
                Color.white, main, 1, 2);
            flash.transform.localPosition = center;
            FxPrefabBuilder.SetShape(flash, ParticleSystemShapeType.Sphere, 0.001f, 0f);

            // Short-lived and slow, so the fireball stays a ball instead of drifting off as blobs.
            ParticleSystem fireball = Burst(root, "Fireball", mainDot, (0.22f, 0.4f), (0.4f, 1.6f + tier * 0.4f), (0.3f, 0.55f),
                main, Color.Lerp(main, Color.white, 0.4f), (short)(10 + tier * 8), 64);
            fireball.transform.localPosition = center;
            FxPrefabBuilder.SetShape(fireball, ParticleSystemShapeType.Sphere, 0.5f, 0f);
            PartialLayers.Add(fireball.transform);

            ParticleSystem sparks = Burst(root, "Sparks", accentDot, (0.18f, 0.4f), (7f, 14f + tier * 3f), (0.03f, 0.08f),
                accent, Color.white, (short)(16 + tier * 14), 128, VfxQuality.Medium);
            sparks.transform.localPosition = center;
            FxPrefabBuilder.SetShape(sparks, ParticleSystemShapeType.Sphere, 0.3f, 0f);
            FxPrefabBuilder.Stretch(sparks, 0.03f);
            ParticleSystem.MainModule sparkMain = sparks.main;
            sparkMain.gravityModifier = 0.5f;

            GroundRing(root, "Shockwave", ring, main, 0.42f, 0.3f, 2.2f);
            if (tier >= 1)
            {
                GroundRing(root, "Shockwave2", accentRing, accent, 0.38f, 0.2f, 2.5f, VfxQuality.Medium, 0.07f);
                ParticleSystem debris = Burst(root, "Embers", mainDot, (0.6f, 1f), (2f, 5f), (0.05f, 0.11f),
                    main, accent, (short)(14 + tier * 8), 64, VfxQuality.Medium);
                debris.transform.localPosition = new Vector3(0f, 0.3f, 0f);
                FxPrefabBuilder.SetShape(debris, ParticleSystemShapeType.Hemisphere, 0.6f, 0f);
                ParticleSystem.MainModule debrisMain = debris.main;
                debrisMain.gravityModifier = -0.15f;
                PartialLayers.Add(debris.transform);
            }

            if (fused)
            {
                // Ultimate: a tall fused column and a second, wider wave.
                ParticleSystem column = Burst(root, "Column", accentDot, (0.45f, 0.7f), (8f, 13f), (0.18f, 0.36f),
                    accent, main, 34, 64, VfxQuality.Medium);
                FxPrefabBuilder.SetShape(column, ParticleSystemShapeType.Cone, 0.4f, 5f);
                ParticleSystem.ShapeModule shape = column.shape;
                shape.rotation = new Vector3(-90f, 0f, 0f);
                FxPrefabBuilder.Stretch(column, 0.05f);

                ParticleSystem orb = Burst(root, "FusionOrb", accentDot, (0.35f, 0.45f), (0f, 0f), (2.6f, 3f), accent, main, 1, 2, VfxQuality.Medium);
                orb.transform.localPosition = center;
                FxPrefabBuilder.SetShape(orb, ParticleSystemShapeType.Sphere, 0.001f, 0f);
                GroundRing(root, "OuterWave", ring, main, 0.6f, 1f, 2.4f, VfxQuality.High, 0.16f);
            }

            Light(root, main, 7f + tier * 3f);
            return AttachSizing(root, null);
        }

        // ------------------------------------------------------------ co-op overlay

        static readonly Color CoopGold = new Color(1f, 0.82f, 0.3f);

        /// <summary>
        /// Played on top of the normal combo effect when two allies made the combo: a gold flash,
        /// a wide double ring and rising sparks. Base layers are cheap; the extra rings, sparks and
        /// the light are quality-gated (Medium / High) so the handheld tier stays light.
        /// </summary>
        static GameObject BuildCoopBurst()
        {
            GameObject root = Root("FX_Combo_Coop", 1.3f);
            Material gold = Dot("M_FX_Combo_CoopDot", CoopGold, 3f, 1.4f);
            Material white = Dot("M_FX_Combo_CoopWhite", new Color(1f, 0.97f, 0.85f), 3.2f, 1.6f);
            Material ring = Ring("M_FX_Combo_Ring_Coop", CoopGold, 2.8f, 0.16f);
            Vector3 chest = new Vector3(0f, 1.1f, 0f);

            ParticleSystem flash = Burst(root, "CoopFlash", white, (0.16f, 0.2f), (0f, 0f), (2.2f, 2.6f), Color.white, CoopGold, 1, 2);
            flash.transform.localPosition = chest;
            FxPrefabBuilder.SetShape(flash, ParticleSystemShapeType.Sphere, 0.001f, 0f);

            GroundRing(root, "CoopRing", ring, CoopGold, 0.55f, 0.4f, 2.4f);
            GroundRing(root, "CoopRing2", ring, Color.white, 0.5f, 0.2f, 2.0f, VfxQuality.Medium, 0.1f);

            ParticleSystem rise = Burst(root, "CoopRise", gold, (0.5f, 0.9f), (2.5f, 5f), (0.05f, 0.12f), CoopGold, Color.white, 22, 64, VfxQuality.Medium);
            rise.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            FxPrefabBuilder.SetShape(rise, ParticleSystemShapeType.Cone, 0.6f, 12f);
            ParticleSystem.ShapeModule riseShape = rise.shape;
            riseShape.rotation = new Vector3(-90f, 0f, 0f);
            FxPrefabBuilder.Stretch(rise, 0.04f);
            FullLayers.Add(rise.transform);

            ParticleSystem stars = Burst(root, "CoopStars", white, (0.3f, 0.5f), (5f, 10f), (0.04f, 0.08f), Color.white, CoopGold, 18, 48, VfxQuality.High);
            stars.transform.localPosition = chest;
            FxPrefabBuilder.SetShape(stars, ParticleSystemShapeType.Sphere, 0.3f, 0f);
            FxPrefabBuilder.Stretch(stars, 0.03f);

            Light(root, CoopGold, 9f);
            return AttachSizing(root, null);
        }

        // ------------------------------------------------------------ element auras (looping, attached to the target)

        static GameObject BuildAura(string name, Element element)
        {
            GameObject root = Root(name, 3600f); // released by ElementAuraPresenter when the element ends
            Color color = element == Element.Fire ? FireColor : element == Element.Ice ? IceColor : element == Element.Lightning ? LightningColor : GravityColor;
            Material dot = Dot($"M_FX_Aura_{element}", color, 1.8f, 1.4f);
            Material ring = Ring($"M_FX_Aura_Ring_{element}", color, 1.4f, 0.18f);

            switch (element)
            {
                case Element.Ice:
                    // The body itself turns icy blue (HitFlash); these add frost around it.
                    Loop(root, "FrostGlow", dot, (0.5f, 0.6f), (0f, 0f), (1.7f, 1.9f), color, Color.white, 6f, 8, new Vector3(0f, 1f, 0f), ParticleSystemShapeType.Sphere, 0.05f, 0.55f);
                    Loop(root, "Crystals", dot, (0.6f, 0.9f), (0.2f, 0.6f), (0.08f, 0.17f), color, Color.white, 22f, 32, new Vector3(0f, 0.9f, 0f), ParticleSystemShapeType.Sphere, 0.6f, 1f);
                    LoopRing(root, "FrostRing", ring, color, 1.7f, 0.06f);
                    break;

                case Element.Fire:
                    ParticleSystem flames = Loop(root, "Flames", dot, (0.4f, 0.6f), (1.2f, 2.2f), (0.14f, 0.3f), color, new Color(1f, 0.8f, 0.3f), 22f, 32, new Vector3(0f, 0.4f, 0f), ParticleSystemShapeType.Cone, 0.4f, 1f);
                    ParticleSystem.ShapeModule flameShape = flames.shape;
                    flameShape.rotation = new Vector3(-90f, 0f, 0f);
                    flameShape.angle = 8f;
                    LoopRing(root, "HeatRing", ring, color, 1.5f, 0.05f);
                    break;

                case Element.Lightning:
                    ParticleSystem crackle = Loop(root, "Crackle", dot, (0.08f, 0.16f), (3f, 6f), (0.03f, 0.06f), color, Color.white, 30f, 32, new Vector3(0f, 1f, 0f), ParticleSystemShapeType.Sphere, 0.5f, 1f);
                    FxPrefabBuilder.Stretch(crackle, 0.03f);
                    Loop(root, "Charge", dot, (0.2f, 0.3f), (0f, 0f), (0.9f, 1.2f), color, Color.white, 5f, 6, new Vector3(0f, 1f, 0f), ParticleSystemShapeType.Sphere, 0.05f, 0.3f);
                    break;

                default: // gravity
                    Loop(root, "Inflow", dot, (0.5f, 0.7f), (-1.6f, -1f), (0.06f, 0.12f), color, Color.white, 16f, 24, new Vector3(0f, 1f, 0f), ParticleSystemShapeType.Sphere, 0.9f, 1f);
                    LoopRing(root, "WaistRing", ring, color, 1.4f, 1f);
                    LoopRing(root, "GroundRing", ring, color, 1.9f, 0.06f);
                    break;
            }

            return root;
        }

        static ParticleSystem Loop(GameObject root, string name, Material material, (float, float) life, (float, float) speed, (float, float) size,
            Color a, Color b, float rate, int max, Vector3 position, ParticleSystemShapeType shape, float radius, float alpha)
        {
            ParticleSystem ps = FxPrefabBuilder.AddSystem(root, name, material);
            FxPrefabBuilder.Configure(ps, life, speed, size, a, b, 0, max);
            ps.transform.localPosition = position;
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.Local; // follows the enemy
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new ParticleSystem.Burst[0]);
            emission.rateOverTime = rate;
            FxPrefabBuilder.SetShape(ps, shape, radius, 0f);
            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.color = new ParticleSystem.MinMaxGradient(Fade(alpha, 0.5f));
            ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = MaxScreenFraction;
            return ps;
        }

        static void LoopRing(GameObject root, string name, Material material, Color color, float diameter, float height)
        {
            ParticleSystem ps = Loop(root, name, material, (0.9f, 0.9f), (0f, 0f), (diameter, diameter), color, color, 1.6f, 3,
                new Vector3(0f, height, 0f), ParticleSystemShapeType.Sphere, 0.001f, 0.8f);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            renderer.maxParticleSize = 50f;
            ParticleSystem.SizeOverLifetimeModule pulse = ps.sizeOverLifetime;
            pulse.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.8f, 1f, 1.05f));
        }
    }
}
