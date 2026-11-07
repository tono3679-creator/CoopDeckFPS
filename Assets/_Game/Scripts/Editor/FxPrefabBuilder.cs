using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    public sealed class FxPrefabSet
    {
        public GameObject MuzzleFlash;
        public GameObject Tracer;
        public GameObject ImpactEnvironment;
        public GameObject ImpactDamageable;
        public GameObject EnemyDeath;
    }

    /// <summary>Per-element skill effect prefabs referenced by card definitions.</summary>
    public sealed class SkillFxSet
    {
        public readonly Dictionary<Element, GameObject> Beams = new Dictionary<Element, GameObject>();
        public readonly Dictionary<Element, GameObject> Bursts = new Dictionary<Element, GameObject>();
        public readonly Dictionary<Element, GameObject> Areas = new Dictionary<Element, GameObject>();
        public readonly Dictionary<Element, GameObject> Auras = new Dictionary<Element, GameObject>();
        public GameObject Telegraph;

        public GameObject Beam(Element element) => Beams.TryGetValue(element, out GameObject fx) ? fx : null;
        public GameObject Burst(Element element) => Bursts.TryGetValue(element, out GameObject fx) ? fx : null;
        public GameObject Area(Element element) => Areas.TryGetValue(element, out GameObject fx) ? fx : null;
        public GameObject Aura(Element element) => Auras.TryGetValue(element, out GameObject fx) ? fx : null;
    }

    /// <summary>
    /// Builds placeholder effect prefabs from Particle Systems. They are meant to be replaced by
    /// artist-made prefabs (Shader Graph / flipbooks) later; gameplay only references the prefab.
    /// </summary>
    public static class FxPrefabBuilder
    {
        public static FxPrefabSet EnsureFxPrefabs(MaterialSet materials, bool overwrite)
        {
            EditorUtil.EnsureFolder(GamePaths.VfxPrefabs);
            return new FxPrefabSet
            {
                MuzzleFlash = Ensure("FX_MuzzleFlash", overwrite, () => BuildMuzzleFlash(materials)),
                Tracer = Ensure("FX_Tracer", overwrite, () => BuildTracer(materials)),
                ImpactEnvironment = Ensure("FX_Impact_Environment", overwrite, () => BuildImpact("FX_Impact_Environment", materials.FxSpark, new Color(1f, 0.9f, 0.6f), 10)),
                ImpactDamageable = Ensure("FX_Impact_Damageable", overwrite, () => BuildImpact("FX_Impact_Damageable", materials.FxEnemySpark, new Color(1f, 0.45f, 0.85f), 14)),
                EnemyDeath = Ensure("FX_EnemyDeath", overwrite, () => BuildDeath(materials)),
            };
        }

        static readonly Element[] AllElements = { Element.None, Element.Fire, Element.Ice, Element.Lightning, Element.Gravity };

        /// <summary>
        /// Skill effects per element: beam, impact burst, area burst (authored for a 1 m radius and
        /// scaled by the caster), ally aura, plus the delayed-impact telegraph.
        /// </summary>
        public static SkillFxSet EnsureSkillFx(bool overwrite)
        {
            const string folder = GamePaths.Root + "/Prefabs/Skills";
            EditorUtil.EnsureFolder(folder);
            var set = new SkillFxSet();
            foreach (Element element in AllElements)
            {
                Color color = ElementStyle.ColorOf(element);
                string n = element.ToString();
                Material dot = GameAssetFactory.Fx($"M_FX_Skill_{n}", color, 2.6f, 1f, 1.3f);
                Material band = GameAssetFactory.Fx($"M_FX_SkillBeam_{n}", color, 2.6f, 0f, 1.4f);
                set.Beams[element] = EnsureIn(folder, $"FX_Skill_Beam_{n}", overwrite, () => BuildSkillBeam($"FX_Skill_Beam_{n}", band, color));
                set.Bursts[element] = EnsureIn(folder, $"FX_Skill_Burst_{n}", overwrite, () => BuildSkillBurst($"FX_Skill_Burst_{n}", dot, color));
                set.Areas[element] = EnsureIn(folder, $"FX_Skill_Area_{n}", overwrite, () => BuildSkillArea($"FX_Skill_Area_{n}", dot, color));
                set.Auras[element] = EnsureIn(folder, $"FX_Skill_Aura_{n}", overwrite, () => BuildSkillAura($"FX_Skill_Aura_{n}", dot, color));
            }

            Material warning = GameAssetFactory.Fx("M_FX_Telegraph", new Color(1f, 0.3f, 0.18f), 1.8f, 1f, 0.7f);
            set.Telegraph = EnsureIn(folder, "FX_Skill_Telegraph", overwrite, () => BuildTelegraph(warning));
            AssetDatabase.SaveAssets();
            return set;
        }

        static GameObject BuildSkillBeam(string name, Material material, Color color)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = 0.45f;
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.1f;
            line.numCapVertices = 2;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = Color.Lerp(color, Color.white, 0.6f);
            line.endColor = color;
            var tracer = root.AddComponent<TracerFx>();
            EditorUtil.SetFloat(tracer, "speed", 160f);
            EditorUtil.SetFloat(tracer, "streakLength", 60f);
            EditorUtil.SetFloat(tracer, "width", 0.1f);
            return root;
        }

        static GameObject BuildSkillBurst(string name, Material material, Color color)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = 0.7f;

            ParticleSystem flash = AddSystem(root, "Flash", material);
            Configure(flash, lifetime: (0.1f, 0.14f), speed: (0f, 0f), size: (0.9f, 1.1f), colorA: Color.Lerp(color, Color.white, 0.5f), colorB: color, burst: 1, max: 4);
            SetShape(flash, ParticleSystemShapeType.Sphere, 0.001f, 0f);

            ParticleSystem shards = AddSystem(root, "Shards", material);
            Configure(shards, lifetime: (0.25f, 0.5f), speed: (2f, 7f), size: (0.08f, 0.2f), colorA: color, colorB: Color.white, burst: 22, max: 48);
            SetShape(shards, ParticleSystemShapeType.Sphere, 0.15f, 0f);
            ParticleSystem.MainModule main = shards.main;
            main.gravityModifier = 0.4f;

            AddFlashLight(root, color, 5f);
            return root;
        }

        static GameObject BuildSkillArea(string name, Material material, Color color)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = 0.9f;

            ParticleSystem glow = AddSystem(root, "GroundGlow", material);
            Configure(glow, lifetime: (0.45f, 0.5f), speed: (0f, 0f), size: (2f, 2f), colorA: color, colorB: color, burst: 1, max: 2);
            SetShape(glow, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            glow.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.SizeOverLifetimeModule grow = glow.sizeOverLifetime;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, 1f));

            ParticleSystem ring = AddSystem(root, "RingSparks", material);
            Configure(ring, lifetime: (0.4f, 0.7f), speed: (0.4f, 1.4f), size: (0.12f, 0.26f), colorA: color, colorB: Color.Lerp(color, Color.white, 0.5f), burst: 36, max: 64);
            SetShape(ring, ParticleSystemShapeType.Circle, 1f, 0f);
            ParticleSystem.ShapeModule ringShape = ring.shape;
            ringShape.radiusThickness = 0f;
            ringShape.rotation = new Vector3(90f, 0f, 0f);
            ParticleSystem.MainModule ringMain = ring.main;
            ringMain.gravityModifier = -0.25f;

            ParticleSystem column = AddSystem(root, "Column", material);
            Configure(column, lifetime: (0.3f, 0.5f), speed: (2f, 4f), size: (0.2f, 0.4f), colorA: color, colorB: Color.white, burst: 10, max: 24);
            SetShape(column, ParticleSystemShapeType.Cone, 0.4f, 10f);
            ParticleSystem.ShapeModule columnShape = column.shape;
            columnShape.rotation = new Vector3(-90f, 0f, 0f);

            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>())
            {
                // Authored for a 1 m radius: the caster scales the root to the effect radius. The
                // flat ground glow scales fully; sparks and the column only spread over the larger
                // area (Shape) so a 6 m skill does not throw 6x larger, 6x faster blobs at the camera.
                ParticleSystem.MainModule m = ps.main;
                bool flat = ps == glow;
                m.scalingMode = flat ? ParticleSystemScalingMode.Hierarchy : ParticleSystemScalingMode.Shape;
                m.simulationSpace = ParticleSystemSimulationSpace.Local;
                if (!flat)
                {
                    ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 0.18f;
                }
            }

            AddFlashLight(root, color, 8f);
            return root;
        }

        static GameObject BuildSkillAura(string name, Material material, Color color)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = 1f;

            ParticleSystem rise = AddSystem(root, "Rise", material);
            Configure(rise, lifetime: (0.5f, 0.8f), speed: (2f, 3.5f), size: (0.08f, 0.16f), colorA: color, colorB: Color.white, burst: 26, max: 48);
            SetShape(rise, ParticleSystemShapeType.Cone, 0.6f, 4f);
            ParticleSystem.ShapeModule riseShape = rise.shape;
            riseShape.radiusThickness = 0f;
            riseShape.rotation = new Vector3(-90f, 0f, 0f);

            ParticleSystem glow = AddSystem(root, "GroundGlow", material);
            Configure(glow, lifetime: (0.5f, 0.55f), speed: (0f, 0f), size: (1.8f, 1.8f), colorA: color, colorB: color, burst: 1, max: 2);
            SetShape(glow, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            glow.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            return root;
        }

        static GameObject BuildTelegraph(Material material)
        {
            var root = new GameObject("FX_Skill_Telegraph");
            root.AddComponent<PooledFx>().Lifetime = 1f;
            var steady = new Gradient();
            steady.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f) });

            ParticleSystem zone = AddSystem(root, "Zone", material);
            Configure(zone, lifetime: (0.9f, 0.9f), speed: (0f, 0f), size: (2f, 2f), colorA: Color.white, colorB: Color.white, burst: 1, max: 2);
            SetShape(zone, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            zone.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.ColorOverLifetimeModule zoneColor = zone.colorOverLifetime;
            zoneColor.color = new ParticleSystem.MinMaxGradient(steady);
            ParticleSystem.SizeOverLifetimeModule zoneSize = zone.sizeOverLifetime;
            zoneSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 1f));

            ParticleSystem converge = AddSystem(root, "Converge", material);
            Configure(converge, lifetime: (0.9f, 0.9f), speed: (0f, 0f), size: (1.6f, 1.6f), colorA: Color.white, colorB: Color.white, burst: 1, max: 2);
            SetShape(converge, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            converge.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
            ParticleSystem.SizeOverLifetimeModule convergeSize = converge.sizeOverLifetime;
            convergeSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.1f));

            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>())
            {
                ParticleSystem.MainModule m = ps.main;
                m.scalingMode = ParticleSystemScalingMode.Hierarchy;
                m.simulationSpace = ParticleSystemSimulationSpace.Local;
                m.duration = 0.9f;
            }

            return root;
        }

        /// <summary>Enemy attack telegraph: a glow that swells over the wind-up (follows the enemy).</summary>
        public static GameObject EnsureEnemyWindupFx(bool overwrite)
        {
            Material material = GameAssetFactory.Fx("M_FX_EnemyWindup", new Color(1f, 0.28f, 0.2f), 2.4f, 1f, 1.1f);
            return Ensure("FX_Enemy_Windup", overwrite, () =>
            {
                var root = new GameObject("FX_Enemy_Windup");
                root.AddComponent<PooledFx>().Lifetime = 0.8f;

                var ramp = new Gradient();
                ramp.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0.2f, 0f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });

                ParticleSystem charge = AddSystem(root, "Charge", material);
                Configure(charge, lifetime: (0.68f, 0.68f), speed: (0f, 0f), size: (0.45f, 0.45f), colorA: Color.white, colorB: Color.white, burst: 1, max: 2);
                SetShape(charge, ParticleSystemShapeType.Sphere, 0.001f, 0f);
                ParticleSystem.ColorOverLifetimeModule chargeColor = charge.colorOverLifetime;
                chargeColor.color = new ParticleSystem.MinMaxGradient(ramp);
                ParticleSystem.SizeOverLifetimeModule chargeSize = charge.sizeOverLifetime;
                chargeSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2.6f));
                ParticleSystem.MainModule main = charge.main;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;

                ParticleSystem sparks = AddSystem(root, "Sparks", material);
                Configure(sparks, lifetime: (0.25f, 0.4f), speed: (-2.5f, -1.5f), size: (0.05f, 0.1f), colorA: Color.white, colorB: new Color(1f, 0.6f, 0.3f), burst: 12, max: 24);
                SetShape(sparks, ParticleSystemShapeType.Sphere, 0.8f, 0f);
                ParticleSystem.MainModule sparksMain = sparks.main;
                sparksMain.simulationSpace = ParticleSystemSimulationSpace.Local;
                return root;
            });
        }

        internal static void AddFlashLight(GameObject root, Color color, float range)
        {
            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.color = color;
            light.shadows = LightShadows.None;
            light.intensity = 0f;
            var flash = lightGo.AddComponent<LightFlash>();
            EditorUtil.SetFloat(flash, "duration", 0.15f);
            EditorUtil.SetFloat(flash, "peakIntensity", 6f);
        }

        internal static GameObject EnsureIn(string folder, string name, bool overwrite, System.Func<GameObject> build)
        {
            string path = $"{folder}/{name}.prefab";
            if (!overwrite && EditorUtil.Exists(path))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            GameObject go = build();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject Ensure(string name, bool overwrite, System.Func<GameObject> build)
        {
            string path = $"{GamePaths.VfxPrefabs}/{name}.prefab";
            if (!overwrite && EditorUtil.Exists(path))
            {
                return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }

            GameObject go = build();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static GameObject BuildMuzzleFlash(MaterialSet m)
        {
            var root = new GameObject("FX_MuzzleFlash");
            root.AddComponent<PooledFx>().Lifetime = 0.12f;

            // Core flash: one big soft sprite.
            ParticleSystem core = AddSystem(root, "Core", m.FxMuzzle);
            Configure(core, lifetime: (0.035f, 0.05f), speed: (0f, 0f), size: (0.28f, 0.36f),
                colorA: new Color(1f, 0.9f, 0.6f), colorB: new Color(1f, 0.7f, 0.35f), burst: 1, max: 4);
            SetShape(core, ParticleSystemShapeType.Sphere, 0.001f, 0f);

            // Sparks thrown forward.
            ParticleSystem sparks = AddSystem(root, "Sparks", m.FxSpark);
            Configure(sparks, lifetime: (0.05f, 0.09f), speed: (3f, 7f), size: (0.03f, 0.06f),
                colorA: new Color(1f, 0.85f, 0.45f), colorB: new Color(1f, 0.6f, 0.2f), burst: 6, max: 24);
            SetShape(sparks, ParticleSystemShapeType.Cone, 0.01f, 14f);
            Stretch(sparks, 0.025f);

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 4f;
            light.color = new Color(1f, 0.75f, 0.4f);
            light.shadows = LightShadows.None;
            light.intensity = 0f;
            lightGo.AddComponent<LightFlash>();
            return root;
        }

        static GameObject BuildTracer(MaterialSet m)
        {
            var root = new GameObject("FX_Tracer");
            root.AddComponent<PooledFx>().Lifetime = 0.3f;
            var line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = m.FxTracer;
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.widthMultiplier = 0.035f;
            line.numCapVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.startColor = new Color(1f, 0.95f, 0.75f, 1f);
            line.endColor = new Color(1f, 0.7f, 0.35f, 1f);
            root.AddComponent<TracerFx>();
            return root;
        }

        static GameObject BuildImpact(string name, Material sparkMaterial, Color color, short sparkCount)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = 0.6f;

            ParticleSystem flash = AddSystem(root, "Flash", sparkMaterial);
            Configure(flash, lifetime: (0.05f, 0.07f), speed: (0f, 0f), size: (0.22f, 0.3f), colorA: color, colorB: color, burst: 1, max: 4);
            SetShape(flash, ParticleSystemShapeType.Sphere, 0.001f, 0f);

            ParticleSystem sparks = AddSystem(root, "Sparks", sparkMaterial);
            Configure(sparks, lifetime: (0.15f, 0.35f), speed: (3f, 8f), size: (0.025f, 0.05f), colorA: color, colorB: Color.white, burst: sparkCount, max: 48);
            SetShape(sparks, ParticleSystemShapeType.Cone, 0.02f, 38f);
            ParticleSystem.MainModule main = sparks.main;
            main.gravityModifier = 1.2f;
            Stretch(sparks, 0.03f);
            return root;
        }

        static GameObject BuildDeath(MaterialSet m)
        {
            var root = new GameObject("FX_EnemyDeath");
            root.AddComponent<PooledFx>().Lifetime = 1.2f;

            ParticleSystem burst = AddSystem(root, "Burst", m.FxDeath);
            Configure(burst, lifetime: (0.35f, 0.7f), speed: (4f, 9f), size: (0.12f, 0.3f),
                colorA: new Color(1f, 0.55f, 0.3f), colorB: new Color(1f, 0.3f, 0.7f), burst: 36, max: 64);
            SetShape(burst, ParticleSystemShapeType.Sphere, 0.3f, 0f);
            ParticleSystem.MainModule main = burst.main;
            main.gravityModifier = 0.6f;

            ParticleSystem ring = AddSystem(root, "Glow", m.FxDeath);
            Configure(ring, lifetime: (0.18f, 0.22f), speed: (0f, 0f), size: (1.8f, 2.2f),
                colorA: new Color(1f, 0.7f, 0.4f), colorB: new Color(1f, 0.5f, 0.8f), burst: 1, max: 2);
            SetShape(ring, ParticleSystemShapeType.Sphere, 0.001f, 0f);
            return root;
        }

        internal static ParticleSystem AddSystem(GameObject root, string name, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        internal static void Configure(ParticleSystem ps, (float min, float max) lifetime, (float min, float max) speed,
            (float min, float max) size, Color colorA, Color colorB, short burst, int max)
        {
            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.min, lifetime.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, burst) });

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            colorOverLifetime.color = new ParticleSystem.MinMaxGradient(fade);

            ParticleSystem.SizeOverLifetimeModule sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));
        }

        internal static void SetShape(ParticleSystem ps, ParticleSystemShapeType type, float radius, float angle)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = radius;
            shape.angle = angle;
        }

        internal static void Stretch(ParticleSystem ps, float velocityScale)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = velocityScale;
            renderer.lengthScale = 1f;
        }
    }
}
