using System;
using System.Collections.Generic;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Shared building blocks for the high-quality effects (weapons, skills): the fireball's Shader Graphs and the
    /// lightning graphs re-coloured per element, plus particle, mesh and prefab helpers. The fireball established the
    /// quality bar (Docs/FIREBALL_VFX.md); every effect built here uses the same shaders and conventions:
    /// <list type="bullet">
    /// <item>Colours come from <see cref="Palette"/> tones on the material (a 4-stop ramp), never from particle colour.</item>
    /// <item>Particles pass UV, StableRandom.x and AgePercent to the shader (erode / cool with age).</item>
    /// <item>Flame bodies and smoke are alpha blended (keep their colour over bright scenes); cores, sparks, flashes add light.</item>
    /// <item>Extra layers sit under <see cref="VfxQualityGate"/> children; heavy meshes and shaders are never dropped by quality.</item>
    /// </list>
    /// </summary>
    internal static class VfxKit
    {
        public const string FireShaderFolder = GamePaths.Root + "/Art/VFX/Fireball/Shaders";
        public const string FireMeshFolder = GamePaths.Root + "/Art/VFX/Fireball/Meshes";
        public const string LightningShaderFolder = GamePaths.Root + "/Art/VFX/Lightning/Shaders";

        /// <summary>Emit along +Y (a surface normal) instead of the shape's default +Z.</summary>
        public static readonly Vector3 Up = new Vector3(-90f, 0f, 0f);

        // ================================================================== shaders

        public static Shader FireShader(string graph) => LoadShader($"{FireShaderFolder}/{graph}.shadergraph");

        public static Shader LightningShader(string graph) => LoadShader($"{LightningShaderFolder}/{graph}.shadergraph");

        public static Shader LoadShader(string path)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                throw new InvalidOperationException(path + " is missing: run the fireball / lightning generators first");
            }

            return shader;
        }

        public static Mesh FireMesh(string name)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>($"{FireMeshFolder}/{name}.asset");
            if (mesh == null)
            {
                throw new InvalidOperationException($"{FireMeshFolder}/{name}.asset is missing: run FireballVfxFactory first");
            }

            return mesh;
        }

        // ================================================================== materials

        /// <summary>
        /// A material on one of the fireball graphs (SG_FX_FireCore / FlameVolume / FireParticle / FireGlow / Smoke /
        /// Shockwave) with the element's ramp: deep → main → hot → core. <paramref name="smoke"/> tints smoke and soot,
        /// <paramref name="glow"/> the young smoke and the cinders.
        /// </summary>
        public static Material ToneMat(string folder, string name, string graph, ElementTones tones, Action<Material> configure,
            Color? hot = null, Color? smoke = null, Color? glow = null)
        {
            Material m = LoadOrCreate(folder, name, FireShader(graph));
            m.SetColor("_CoreColor", tones.Core);
            m.SetColor("_HotColor", hot ?? Color.Lerp(tones.Main, tones.Core, 0.55f));
            m.SetColor("_MidColor", tones.Main);
            m.SetColor("_DeepColor", tones.Deep);
            m.SetColor("_SmokeColor", smoke ?? Palette.Smoke);
            m.SetColor("_GlowColor", glow ?? tones.Deep);
            Set(m, ("_Intensity", 1.2f), ("_CoreBoost", 3f), ("_Bands", 0.3f), ("_BandCount", 5f), ("_NoiseScale", 2f), ("_FlowSpeed", 2f),
                ("_Swirl", 2f), ("_Seed", 0f), ("_Dissolve", 0f), ("_AgeDissolve", 1f), ("_AgeHeat", 0.8f), ("_Opacity", 1f), ("_EdgeSoftness", 0.12f),
                ("_Mode", 0f), ("_PulseSpeed", 9f), ("_RimPower", 1.2f), ("_Displace", 0.15f), ("_TailFade", 0f), ("_HeatBias", 0f),
                ("_TrailFlip", 0f), ("_NearFade", 0f));
            configure?.Invoke(m);
            Finish(m, instancing: false);
            return m;
        }

        /// <summary>A material on the lightning graphs (SG_FX_LightningBolt strips, SG_FX_LightningFlash flash / ring / glow).</summary>
        public static Material BoltMat(string folder, string name, string graph, Color core, Color glow, Color edge, Action<Material> configure)
        {
            Material m = LoadOrCreate(folder, name, LightningShader(graph));
            m.SetColor("_CoreColor", core);
            m.SetColor("_GlowColor", glow);
            m.SetColor("_EdgeColor", edge);
            configure?.Invoke(m);
            Finish(m, instancing: true);
            return m;
        }

        public static Material LoadOrCreate(string folder, string name, Shader shader)
        {
            EditorUtil.EnsureFolder(folder);
            string path = $"{folder}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = shader;
            return m;
        }

        static void Finish(Material m, bool instancing)
        {
            m.enableInstancing = instancing;
            // Graph shaders report the Geometry queue to URP's automatic queue; effects draw after the opaque world and sky.
            if (m.HasProperty("_QueueControl"))
            {
                m.SetFloat("_QueueControl", 1f);
            }

            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
        }

        public static Material Set(Material m, params (string name, float value)[] values)
        {
            foreach ((string name, float value) in values)
            {
                m.SetFloat(name, value);
            }

            return m;
        }

        // ================================================================== prefabs

        public static GameObject Save(string folder, string name, Func<GameObject> build)
        {
            EditorUtil.EnsureFolder(folder);
            GameObject go = build();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{folder}/{name}.prefab");
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        public static GameObject Root(string name, float lifetime)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = lifetime;
            return root;
        }

        public static Transform Child(Transform parent, string name, Vector3 position = default, Vector3 euler = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(euler);
            return go.transform;
        }

        public static Transform Gate(Transform parent, string name, VfxQuality minimum)
        {
            Transform t = Child(parent, name);
            t.gameObject.AddComponent<VfxQualityGate>().MinimumQuality = minimum;
            return t;
        }

        public static Mesh SaveMesh(string folder, string name, Func<Mesh> build)
        {
            EditorUtil.EnsureFolder(folder);
            string path = $"{folder}/{name}.asset";
            Mesh built = build();
            built.name = name;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(built, path);
                return built;
            }

            EditorUtility.CopySerialized(built, existing); // keep the asset (and its references), replace the data
            UnityEngine.Object.DestroyImmediate(built);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        // ================================================================== particles

        static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Normal,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomX,
            ParticleSystemVertexStream.AgePercent,
        };

        public static ParticleSystem Ps(Transform parent, string name, Material material, (float min, float max) life, (float min, float max) speed,
            (float min, float max) size, int maxParticles, bool world = true, Vector3 position = default)
        {
            Transform t = Child(parent, name, position);
            var ps = t.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var renderer = t.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 0.5f;
            renderer.SetActiveVertexStreams(Streams);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.min, life.max);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.min, speed.max);
            main.startSize = new ParticleSystem.MinMaxCurve(size.min, size.max);
            main.startColor = Color.white; // colours come from the material's ramp
            main.simulationSpace = world ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = maxParticles;
            main.gravityModifier = 0f;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.rateOverDistance = 0f;

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;

            // The shader erodes with age; vertex alpha only fades in (no pop) and lets go at the very end.
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Alpha((0f, 0f), (1f, 0.06f), (1f, 0.85f), (0.6f, 1f)));
            return ps;
        }

        public static Gradient Alpha(params (float alpha, float time)[] keys)
        {
            var g = new Gradient();
            var alphas = new GradientAlphaKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                alphas[i] = new GradientAlphaKey(keys[i].alpha, keys[i].time);
            }

            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, alphas);
            return g;
        }

        public static void Fade(ParticleSystem ps, params (float alpha, float time)[] keys)
        {
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(Alpha(keys));
        }

        public static void Burst(ParticleSystem ps, short count, float time = 0f)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(time, count) });
        }

        public static void Bursts(ParticleSystem ps, params (float time, short count)[] bursts)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            var list = new ParticleSystem.Burst[bursts.Length];
            for (int i = 0; i < bursts.Length; i++)
            {
                list[i] = new ParticleSystem.Burst(bursts[i].time, bursts[i].count);
            }

            emission.SetBursts(list);
            ParticleSystem.MainModule main = ps.main;
            main.duration = Mathf.Max(main.duration, bursts[bursts.Length - 1].time + 0.05f);
        }

        public static void Rate(ParticleSystem ps, float overTime, float overDistance = 0f, float duration = 0.2f, bool loop = false)
        {
            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = overTime;
            emission.rateOverDistance = overDistance;
            ParticleSystem.MainModule main = ps.main;
            main.duration = duration;
            main.loop = loop;
        }

        public static void Shape(ParticleSystem ps, ParticleSystemShapeType type, float radius, float angle = 0f, Vector3 rotation = default,
            float thickness = 1f, Vector3 scale = default, Vector3 position = default, float arc = 360f)
        {
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = type;
            shape.radius = radius;
            shape.angle = angle;
            shape.radiusThickness = thickness;
            shape.rotation = rotation;
            shape.position = position;
            shape.scale = scale == default ? Vector3.one : scale;
            shape.arc = arc;
        }

        public static void SizeOverLife(ParticleSystem ps, params Keyframe[] keys)
        {
            ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(keys));
        }

        public static void Velocity(ParticleSystem ps, Vector3 linear, Vector3 linearSpread = default, float orbitalY = 0f, float orbitalZ = 0f,
            float radial = 0f, bool local = true, float orbitalX = 0f)
        {
            ParticleSystem.VelocityOverLifetimeModule v = ps.velocityOverLifetime;
            v.enabled = true;
            v.space = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            v.x = new ParticleSystem.MinMaxCurve(linear.x - linearSpread.x, linear.x + linearSpread.x);
            v.y = new ParticleSystem.MinMaxCurve(linear.y - linearSpread.y, linear.y + linearSpread.y);
            v.z = new ParticleSystem.MinMaxCurve(linear.z - linearSpread.z, linear.z + linearSpread.z);
            v.orbitalX = new ParticleSystem.MinMaxCurve(orbitalX, orbitalX);
            v.orbitalY = new ParticleSystem.MinMaxCurve(orbitalY, orbitalY);
            v.orbitalZ = new ParticleSystem.MinMaxCurve(orbitalZ, orbitalZ);
            v.radial = new ParticleSystem.MinMaxCurve(radial, radial);
        }

        public static void Drag(ParticleSystem ps, float drag)
        {
            ParticleSystem.LimitVelocityOverLifetimeModule limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = drag;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = true;
        }

        public static void Spin(ParticleSystem ps, float startDegrees, float spinDegrees)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startRotation = new ParticleSystem.MinMaxCurve(-startDegrees * Mathf.Deg2Rad, startDegrees * Mathf.Deg2Rad);
            if (spinDegrees > 0f)
            {
                ParticleSystem.RotationOverLifetimeModule rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-spinDegrees * Mathf.Deg2Rad, spinDegrees * Mathf.Deg2Rad);
            }
        }

        public static void Noise(ParticleSystem ps, float strength, float frequency)
        {
            ParticleSystem.NoiseModule noise = ps.noise;
            noise.enabled = true;
            noise.strength = strength;
            noise.frequency = frequency;
            noise.scrollSpeed = 0.6f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            noise.damping = true;
        }

        public static void Stretch(ParticleSystem ps, float velocityScale, float lengthScale = 1f)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = velocityScale;
            renderer.lengthScale = lengthScale;
        }

        public static void MeshParticles(ParticleSystem ps, Mesh mesh, bool alignLocal, bool randomRotation)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = mesh;
            renderer.alignment = alignLocal ? ParticleSystemRenderSpace.Local : ParticleSystemRenderSpace.World;
            renderer.maxParticleSize = 100f;
            if (randomRotation)
            {
                ParticleSystem.MainModule main = ps.main;
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
            }
        }

        /// <summary>Flat particles lying in the emitter's XY plane (decals, ground rings): a local-aligned quad.</summary>
        public static void Flat(ParticleSystem ps, Mesh quadXY)
        {
            MeshParticles(ps, quadXY, alignLocal: true, randomRotation: false);
            ParticleSystem.MainModule main = ps.main;
            main.startRotation3D = true;
            main.startRotationX = 0f;
            main.startRotationY = 0f;
            main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        }

        public static void Order(ParticleSystem ps, float sortingFudge) => ps.GetComponent<ParticleSystemRenderer>().sortingFudge = sortingFudge;

        public static void Gravity(ParticleSystem ps, float g)
        {
            ParticleSystem.MainModule main = ps.main;
            main.gravityModifier = g;
        }

        public static void Delay(ParticleSystem ps, float seconds)
        {
            ParticleSystem.MainModule main = ps.main;
            main.startDelay = seconds;
        }

        public static void MaxSize(ParticleSystem ps, float screenFraction) => ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = screenFraction;

        public static MeshRenderer MeshLayer(Transform parent, string name, Mesh mesh, Material material, float scale, Quaternion rotation = default)
        {
            Transform t = Child(parent, name);
            t.localScale = Vector3.one * scale;
            t.localRotation = rotation == default ? Quaternion.identity : rotation;
            t.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = t.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        public static TrailRenderer Trail(Transform parent, string name, Material material, float time, float width)
        {
            Transform t = Child(parent, name);
            var trail = t.gameObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.time = time;
            trail.widthMultiplier = width;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.35f, 0.8f), new Keyframe(1f, 0f));
            trail.colorGradient = Alpha((1f, 0f), (0.9f, 0.5f), (0f, 1f));
            trail.minVertexDistance = 0.12f;
            trail.numCapVertices = 2;
            trail.textureMode = LineTextureMode.Stretch;
            trail.alignment = LineAlignment.View;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            trail.autodestruct = false;
            trail.emitting = true;
            return trail;
        }

        public static LineRenderer Line(Transform parent, string name, Material material, float width, int points)
        {
            Transform t = Child(parent, name);
            var line = t.gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = points;
            line.widthMultiplier = width;
            line.numCapVertices = 0;
            line.textureMode = LineTextureMode.Stretch;
            line.alignment = LineAlignment.View;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        /// <summary>A short light pulse (<see cref="LightFlash"/>: only with High effects, never on the Handheld tier).</summary>
        public const float LightRangeScale = 1.3f;
        public const float LightPeakScale = 1.6f;
        public const float LightDurationScale = 1.3f;

        public static void FlashLight(GameObject root, Color color, float range, float duration, float peak, float height = 0.5f)
        {
            // 2026-11-09 ('more spectacular'): every effect light reaches further, burns brighter and a little longer.
            range *= LightRangeScale;
            peak *= LightPeakScale;
            duration *= LightDurationScale;
            Transform t = Child(root.transform, "Light", new Vector3(0f, height, 0f));
            var light = t.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.color = color;
            light.shadows = LightShadows.None;
            light.intensity = 0f;
            var flash = t.gameObject.AddComponent<LightFlash>();
            EditorUtil.SetFloat(flash, "duration", duration);
            EditorUtil.SetFloat(flash, "peakIntensity", peak);
        }
    }
}
