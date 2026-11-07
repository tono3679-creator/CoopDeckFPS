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

        static ParticleSystem AddSystem(GameObject root, string name, Material material)
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

        static void Configure(ParticleSystem ps, (float min, float max) lifetime, (float min, float max) speed,
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

        static void SetShape(ParticleSystem ps, ParticleSystemShapeType type, float radius, float angle)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = radius;
            shape.angle = angle;
        }

        static void Stretch(ParticleSystem ps, float velocityScale)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = velocityScale;
            renderer.lengthScale = 1f;
        }
    }
}
