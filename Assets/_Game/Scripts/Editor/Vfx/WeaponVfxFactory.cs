using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEditor;
using UnityEngine;
using static CoopDeck.EditorTools.VfxKit;

namespace CoopDeck.EditorTools
{
    /// <summary>The four shot effects of one weapon (muzzle, tracer, impact on the world, impact on a character).</summary>
    public sealed class WeaponVfxSet
    {
        public GameObject Muzzle;
        public GameObject Tracer;
        public GameObject Impact;
        public GameObject ImpactHit;
    }

    /// <summary>
    /// Shot effects at the fireball's quality bar (Docs/WEAPON_SKILL_VFX.md): layered muzzle flashes, a two-layer tracer
    /// (<see cref="ShotTracerFx"/>), impacts with a flash, sparks, debris, smoke and a scorch on the world, and an
    /// element burst on characters. Fire (IGNIS-27) uses the fireball's fire graphs; electric (VOLT-9) uses the
    /// lightning graphs in the rabbit's cyan. Same prefab paths as before, so the weapon data keeps its references.
    ///
    /// Frames: <c>WeaponFx</c> spawns the muzzle under the muzzle socket on the ViewModel layer (drawn by the view-model
    /// camera, +Z = barrel), the tracer from the muzzle to the hit, the impact at the hit with +Z = the surface normal.
    /// </summary>
    public static class WeaponVfxFactory
    {
        public const string MaterialFolder = GamePaths.Root + "/Art/VFX/Weapons/Materials";
        public const string MeshFolder = GamePaths.Root + "/Art/VFX/Weapons/Meshes";
        public const string IgnisFolder = GamePaths.Root + "/Prefabs/VFX/Ignis";
        public const string ThunderhopFolder = GamePaths.Root + "/Prefabs/VFX/Thunderhop";

        /// <summary>The VOLT-9's electric colours (the rabbit's cyan, Docs/LIGHTNING_VFX.md).</summary>
        public static readonly Color VoltCore = Palette.Hex(0xF4FDFF);
        public static readonly Color VoltCyan = Palette.Hex(0x5ADCF4);
        public static readonly Color VoltEdge = Palette.Hex(0x1E8FE6);
        public static readonly ElementTones VoltTones = new ElementTones(VoltCore, VoltCyan, VoltEdge);

        static Mesh quadXY;

        /// <summary>A unit quad in the XY plane facing +Z (decals and rings on a surface whose normal is +Z).</summary>
        public static Mesh QuadXY => quadXY != null ? quadXY : quadXY = SaveMesh(MeshFolder, "M_Vfx_QuadXY", () =>
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f) };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        });

        // ================================================================== IGNIS-27 (fire)

        sealed class FireMats
        {
            public Material Core, Flame, Spark, Streak, Wake, MuzzleSmoke, Dust, Scorch, Ember, Billow, Chip;
        }

        static FireMats Fire()
        {
            ElementTones t = Palette.Fire;
            Color dust = Color.Lerp(Palette.Smoke, Palette.Floor, 0.45f);
            return new FireMats
            {
                // Additive white-hot: the muzzle core and flashes, sparks, the tracer's bolt.
                Core = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Core", "SG_FX_FireGlow", t, m => Set(m, ("_Mode", 1f), ("_AgeHeat", 0.55f),
                    ("_EdgeSoftness", 0.26f), ("_Intensity", 1.25f), ("_CoreBoost", 3.5f), ("_HeatBias", 0.12f))),
                Spark = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Spark", "SG_FX_FireGlow", t, m => Set(m, ("_Mode", 1f), ("_AgeHeat", 1f),
                    ("_EdgeSoftness", 0.195f), ("_Intensity", 1.1f), ("_CoreBoost", 3f))),
                Streak = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Streak", "SG_FX_FireGlow", t, m => Set(m, ("_Mode", 2f), ("_TrailFlip", 1f),
                    ("_NoiseScale", 1.4f), ("_FlowSpeed", 14f), ("_EdgeSoftness", 0.078f), ("_Intensity", 1.1f), ("_CoreBoost", 3.25f), ("_HeatBias", 0.3f))),
                // Alpha blended: flame bodies keep their orange over a bright sky.
                Flame = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Flame", "SG_FX_FireParticle", t, m => Set(m, ("_Mode", 0f), ("_NoiseScale", 2.4f),
                    ("_FlowSpeed", 5f), ("_AgeHeat", 0.85f), ("_EdgeSoftness", 0.058f), ("_Intensity", 1f), ("_CoreBoost", 2.5f), ("_HeatBias", 0.18f), ("_Bands", 0.45f))),
                Wake = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Wake", "SG_FX_FireParticle", t, m => Set(m, ("_Mode", 2f), ("_TrailFlip", 1f),
                    ("_NoiseScale", 1.2f), ("_FlowSpeed", 6f), ("_EdgeSoftness", 0.091f), ("_Intensity", 1f), ("_CoreBoost", 2.25f), ("_HeatBias", 0.22f),
                    ("_Opacity", 0.85f), ("_Bands", 0.45f))),
                MuzzleSmoke = ToneMat(MaterialFolder, "M_FX_Shot_Fire_MuzzleSmoke", "SG_FX_Smoke", t, m => Set(m, ("_Mode", 0f), ("_Opacity", 0.35f),
                    ("_NoiseScale", 2.2f), ("_FlowSpeed", 0.9f), ("_EdgeSoftness", 0.3f))),
                Dust = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Dust", "SG_FX_Smoke", t, m => Set(m, ("_Mode", 0f), ("_Opacity", 0.45f),
                    ("_NoiseScale", 2f), ("_FlowSpeed", 0.7f), ("_EdgeSoftness", 0.25f), ("_NearFade", 0.8f)), smoke: dust, glow: t.Main),
                Scorch = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Scorch", "SG_FX_Smoke", t, m => Set(m, ("_Mode", 1f), ("_Opacity", 0.85f),
                    ("_NoiseScale", 4f), ("_EdgeSoftness", 0.117f)), glow: t.Main),
                Ember = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Ember", "SG_FX_FireCore", t, m => Set(m, ("_Mode", 1f), ("_Intensity", 1.1f),
                    ("_CoreBoost", 2.75f), ("_NoiseScale", 9f))),
                Billow = ToneMat(MaterialFolder, "M_FX_Shot_Fire_Billow", "SG_FX_FlameVolume", t, m => Set(m, ("_TailFade", 0f), ("_Displace", 0.2f),
                    ("_NoiseScale", 1.8f), ("_FlowSpeed", 2.2f), ("_Swirl", 1.5f), ("_RimPower", 0.9f), ("_HeatBias", 0.26f), ("_EdgeSoftness", 0.065f),
                    ("_Intensity", 1f), ("_CoreBoost", 2.88f), ("_AgeDissolve", 1.15f), ("_NearFade", 0.8f), ("_Bands", 0.45f))),
                Chip = ChipMaterial(),
            };
        }

        /// <summary>Stone chips knocked off by a hit: lit, the walls' slate.</summary>
        static Material ChipMaterial()
        {
            Material m = LoadOrCreate(MaterialFolder, "M_FX_Shot_Chip", Shader.Find("Universal Render Pipeline/Lit"));
            m.SetColor("_BaseColor", Palette.Trim);
            m.SetFloat("_Smoothness", 0.1f);
            m.renderQueue = -1;
            EditorUtility.SetDirty(m);
            return m;
        }

        public static WeaponVfxSet EnsureIgnis()
        {
            FireMats m = Fire();
            Mesh billow = FireMesh("M_Fireball_Billow");
            Mesh ember = FireMesh("M_Fireball_EmberChunk");
            var set = new WeaponVfxSet
            {
                Muzzle = Save(IgnisFolder, "FX_Ignis_Muzzle", () => FireMuzzle(m)),
                Tracer = Save(IgnisFolder, "FX_Ignis_Tracer", () => Tracer("FX_Ignis_Tracer", m.Streak, m.Wake, 230f, 3.6f, 0.095f, 0.032f, 0.14f, 0f)),
                Impact = Save(IgnisFolder, "FX_Ignis_Impact", () => FireImpact(m, ember)),
                ImpactHit = Save(IgnisFolder, "FX_Ignis_Impact_Hit", () => FireImpactHit(m, billow)),
            };
            AssetDatabase.SaveAssets();
            return set;
        }

        /// <summary>
        /// A white-hot core, a star of hot streaks, a short orange flame thrown forward, sparks and a wisp of smoke.
        /// The flash lives two to three frames; only the smoke and sparks outlast it.
        /// </summary>
        static GameObject FireMuzzle(FireMats m)
        {
            GameObject root = Root("FX_Ignis_Muzzle", 0.6f);
            Transform t = root.transform;

            ParticleSystem bloom = Ps(t, "Bloom", m.Core, (0.045f, 0.06f), (0f, 0f), (0.36f, 0.44f), 2, world: false, position: new Vector3(0f, 0f, 0.05f));
            Burst(bloom, 1);
            Fade(bloom, (0.3f, 0f), (0.15f, 0.5f), (0f, 1f));
            Order(bloom, -3f);

            ParticleSystem core = Ps(t, "Core", m.Core, (0.04f, 0.05f), (0f, 0f), (0.33f, 0.4f), 2, world: false, position: new Vector3(0f, 0f, 0.04f));
            Burst(core, 1);
            Spin(core, 180f, 0f);
            SizeOverLife(core, new Keyframe(0f, 0.7f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.55f));
            Order(core, -2f);

            ParticleSystem star = Ps(t, "Star", m.Core, (0.035f, 0.05f), (10f, 17f), (0.07f, 0.1f), 14, world: false);
            Burst(star, 10);
            Shape(star, ParticleSystemShapeType.Cone, 0.005f, 42f);
            Stretch(star, 0.025f, 2.2f);
            Order(star, -1f);

            ParticleSystem flame = Ps(t, "Flame", m.Flame, (0.05f, 0.075f), (3.5f, 6.5f), (0.18f, 0.3f), 10, world: false, position: new Vector3(0f, 0f, 0.05f));
            Burst(flame, 7);
            Shape(flame, ParticleSystemShapeType.Cone, 0.01f, 16f);
            Spin(flame, 30f, 0f);
            SizeOverLife(flame, new Keyframe(0f, 0.6f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.15f));

            ParticleSystem sparks = Ps(t, "Sparks", m.Spark, (0.07f, 0.16f), (10f, 18f), (0.014f, 0.024f), 14);
            Burst(sparks, 10);
            Shape(sparks, ParticleSystemShapeType.Cone, 0.01f, 24f);
            Stretch(sparks, 0.02f, 1.5f);
            Gravity(sparks, 0.35f);

            Transform medium = Gate(t, "Medium", VfxQuality.Medium);
            ParticleSystem smoke = Ps(medium, "Smoke", m.MuzzleSmoke, (0.35f, 0.5f), (0.25f, 0.6f), (0.07f, 0.11f), 4, position: new Vector3(0f, 0f, 0.05f));
            Burst(smoke, 1);
            Shape(smoke, ParticleSystemShapeType.Cone, 0.01f, 20f);
            Velocity(smoke, new Vector3(0f, 0.25f, 0f), local: false);
            Drag(smoke, 2.5f);
            Spin(smoke, 180f, 40f);
            SizeOverLife(smoke, new Keyframe(0f, 0.6f), new Keyframe(1f, 2.6f));

            FlashLight(root, Palette.Fire.Main, 4f, 0.05f, 3f);
            return root;
        }

        /// <summary>
        /// On the world (+Z = surface normal): a flash, sparks that arc down, a lick of flame, burning chips, a puff of
        /// dust and a small scorch that cools over three seconds.
        /// </summary>
        static GameObject FireImpact(FireMats m, Mesh ember)
        {
            GameObject root = Root("FX_Ignis_Impact", 3.2f);
            Transform t = root.transform;

            ParticleSystem flash = Ps(t, "Flash", m.Core, (0.06f, 0.08f), (0f, 0f), (0.95f, 1.15f), 2, world: false, position: new Vector3(0f, 0f, 0.06f));
            Burst(flash, 1);
            Spin(flash, 180f, 0f);
            SizeOverLife(flash, new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.4f));
            Order(flash, -2f);

            ParticleSystem sparks = Ps(t, "Sparks", m.Spark, (0.2f, 0.45f), (3.5f, 10f), (0.022f, 0.038f), 30);
            Burst(sparks, 20);
            Shape(sparks, ParticleSystemShapeType.Cone, 0.02f, 58f);
            Stretch(sparks, 0.03f, 1.3f);
            Gravity(sparks, 0.9f);
            Drag(sparks, 0.6f);

            ParticleSystem lick = Ps(t, "Flame", m.Flame, (0.14f, 0.22f), (0.7f, 1.6f), (0.2f, 0.33f), 6, position: new Vector3(0f, 0f, 0.05f));
            Burst(lick, 4);
            Shape(lick, ParticleSystemShapeType.Cone, 0.04f, 25f);
            Velocity(lick, new Vector3(0f, 0.8f, 0f), local: false);
            SizeOverLife(lick, new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.8f));

            Transform medium = Gate(t, "Medium", VfxQuality.Medium);
            ParticleSystem chips = Ps(medium, "Embers", m.Ember, (0.4f, 0.7f), (2f, 5f), (0.025f, 0.045f), 6);
            Burst(chips, 4);
            Shape(chips, ParticleSystemShapeType.Cone, 0.02f, 50f);
            MeshParticles(chips, ember, alignLocal: false, randomRotation: true);
            Gravity(chips, 1.1f);
            Spin(chips, 0f, 0f);

            ParticleSystem dust = Ps(medium, "Dust", m.Dust, (0.5f, 0.8f), (0.4f, 1f), (0.22f, 0.36f), 4, position: new Vector3(0f, 0f, 0.08f));
            Burst(dust, 2);
            Shape(dust, ParticleSystemShapeType.Cone, 0.03f, 30f);
            Drag(dust, 3f);
            Spin(dust, 180f, 30f);
            SizeOverLife(dust, new Keyframe(0f, 0.5f), new Keyframe(1f, 1.9f));

            ParticleSystem scorch = Ps(medium, "Scorch", m.Scorch, (3f, 3f), (0f, 0f), (0.3f, 0.38f), 2, world: false, position: new Vector3(0f, 0f, 0.012f));
            Burst(scorch, 1);
            Flat(scorch, QuadXY);
            Fade(scorch, (1f, 0f), (1f, 0.8f), (0f, 1f));
            Order(scorch, 5f);

            FlashLight(root, Palette.Fire.Main, 3f, 0.06f, 2.5f);
            return root;
        }

        /// <summary>On a character: a bigger flash, a ball of fire bursting back toward the shooter and a spray of embers.</summary>
        static GameObject FireImpactHit(FireMats m, Mesh billow)
        {
            GameObject root = Root("FX_Ignis_Impact_Hit", 1f);
            Transform t = root.transform;

            ParticleSystem flash = Ps(t, "Flash", m.Core, (0.07f, 0.09f), (0f, 0f), (1.3f, 1.55f), 2, world: false, position: new Vector3(0f, 0f, 0.08f));
            Burst(flash, 1);
            Spin(flash, 180f, 0f);
            SizeOverLife(flash, new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.4f));
            Order(flash, -2f);

            ParticleSystem fire = Ps(t, "Fire", m.Billow, (0.24f, 0.34f), (0.9f, 2.1f), (0.34f, 0.48f), 7, position: new Vector3(0f, 0f, 0.06f));
            Burst(fire, 5);
            Shape(fire, ParticleSystemShapeType.Cone, 0.05f, 50f);
            MeshParticles(fire, billow, alignLocal: false, randomRotation: true);
            Drag(fire, 2f);
            SizeOverLife(fire, new Keyframe(0f, 0.55f), new Keyframe(0.4f, 1.15f), new Keyframe(1f, 1.3f));

            ParticleSystem embers = Ps(t, "Embers", m.Spark, (0.22f, 0.5f), (4.5f, 10.5f), (0.022f, 0.036f), 32);
            Burst(embers, 28);
            Shape(embers, ParticleSystemShapeType.Cone, 0.03f, 60f);
            Stretch(embers, 0.03f, 1.3f);
            Gravity(embers, 0.6f);
            Drag(embers, 0.8f);

            Transform medium = Gate(t, "Medium", VfxQuality.Medium);
            ParticleSystem smoke = Ps(medium, "Smoke", m.Dust, (0.45f, 0.7f), (0.3f, 0.8f), (0.2f, 0.32f), 3, position: new Vector3(0f, 0f, 0.1f));
            Burst(smoke, 2);
            Shape(smoke, ParticleSystemShapeType.Cone, 0.04f, 35f);
            Velocity(smoke, new Vector3(0f, 0.5f, 0f), local: false);
            Drag(smoke, 3f);
            SizeOverLife(smoke, new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f));

            FlashLight(root, Palette.Fire.Main, 3.5f, 0.07f, 3.5f);
            return root;
        }

        // ================================================================== VOLT-9 (electric)

        sealed class VoltMats
        {
            public Material Flash, Ring, Glow, Bolt, Wake, Arc, Spark, Scorch, Dust;
        }

        static VoltMats Volt()
        {
            Color glow = VoltCyan * 1.2f;
            return new VoltMats
            {
                Flash = BoltMat(MaterialFolder, "M_FX_Shot_Volt_Flash", "SG_FX_LightningFlash", VoltCore, VoltCyan * 1.35f, VoltEdge,
                    m => Set(m, ("_Mode", 0f), ("_Intensity", 1f), ("_CoreBoost", 5.62f), ("_GlowFalloff", 12f), ("_Rays", 6f), ("_HaloOpacity", 1f))),
                Ring = BoltMat(MaterialFolder, "M_FX_Shot_Volt_Ring", "SG_FX_LightningFlash", VoltCore, VoltCyan * 1.6f, VoltEdge,
                    m => Set(m, ("_Mode", 1f), ("_Intensity", 1f), ("_CoreBoost", 3.25f), ("_GlowFalloff", 4f), ("_HaloOpacity", 1f))),
                Glow = BoltMat(MaterialFolder, "M_FX_Shot_Volt_Glow", "SG_FX_LightningFlash", VoltCore, glow, VoltEdge,
                    m => Set(m, ("_Mode", 2f), ("_Intensity", 1f), ("_CoreBoost", 1.25f), ("_GlowFalloff", 3f), ("_HaloOpacity", 0.4f))),
                Bolt = BoltMat(MaterialFolder, "M_FX_Shot_Volt_Bolt", "SG_FX_LightningBolt", VoltCore, glow, VoltEdge,
                    m => Set(m, ("_Intensity", 1f), ("_CoreBoost", 3.75f), ("_CoreWidth", 0.16f), ("_GlowFalloff", 1.8f), ("_HaloOpacity", 0.85f),
                        ("_NoiseScale", 8f), ("_FlowSpeed", 14f), ("_PulseCount", 0f), ("_PulseSpeed", 0f), ("_Seed", 3f))),
                Wake = BoltMat(MaterialFolder, "M_FX_Shot_Volt_Wake", "SG_FX_LightningBolt", VoltCore, VoltCyan, VoltEdge,
                    m => Set(m, ("_Intensity", 0.85f), ("_CoreBoost", 2.25f), ("_CoreWidth", 0.22f), ("_GlowFalloff", 2.4f), ("_HaloOpacity", 0.6f),
                        ("_NoiseScale", 10f), ("_FlowSpeed", 18f), ("_PulseCount", 0f), ("_PulseSpeed", 0f), ("_Seed", 9f))),
                Arc = BoltMat(MaterialFolder, "M_FX_Shot_Volt_Arc", "SG_FX_LightningBolt", VoltCore, glow, VoltEdge,
                    m => Set(m, ("_Intensity", 1f), ("_CoreBoost", 3.25f), ("_CoreWidth", 0.18f), ("_GlowFalloff", 2.2f), ("_HaloOpacity", 0.85f),
                        ("_NoiseScale", 10f), ("_FlowSpeed", 18f), ("_PulseCount", 0f), ("_PulseSpeed", 0f), ("_Seed", 13f))),
                // Sparks on the fire glow graph with the electric ramp: white-hot, cooling to cyan and azure.
                Spark = ToneMat(MaterialFolder, "M_FX_Shot_Volt_Spark", "SG_FX_FireGlow", VoltTones, m => Set(m, ("_Mode", 1f), ("_AgeHeat", 0.9f),
                    ("_EdgeSoftness", 0.195f), ("_Intensity", 1.1f), ("_CoreBoost", 2.75f), ("_Bands", 0.2f))),
                Scorch = ToneMat(MaterialFolder, "M_FX_Shot_Volt_Scorch", "SG_FX_Smoke", VoltTones, m => Set(m, ("_Mode", 1f), ("_Opacity", 0.75f),
                    ("_NoiseScale", 4.5f), ("_EdgeSoftness", 0.117f)), glow: VoltCyan),
                Dust = ToneMat(MaterialFolder, "M_FX_Shot_Volt_Dust", "SG_FX_Smoke", VoltTones, m => Set(m, ("_Mode", 0f), ("_Opacity", 0.4f),
                    ("_NoiseScale", 2f), ("_FlowSpeed", 0.7f), ("_EdgeSoftness", 0.25f), ("_NearFade", 0.8f)),
                    smoke: Color.Lerp(Palette.Smoke, Palette.Floor, 0.45f), glow: VoltCyan),
            };
        }

        public static WeaponVfxSet EnsureThunderhop()
        {
            VoltMats m = Volt();
            var set = new WeaponVfxSet
            {
                Muzzle = Save(ThunderhopFolder, "FX_Thunderhop_Muzzle", () => VoltMuzzle(m)),
                Tracer = Save(ThunderhopFolder, "FX_Thunderhop_Tracer", () => Tracer("FX_Thunderhop_Tracer", m.Bolt, m.Wake, 280f, 3.4f, 0.085f, 0.03f, 0.12f, 0.045f)),
                Impact = Save(ThunderhopFolder, "FX_Thunderhop_Impact", () => VoltImpact(m, onCharacter: false)),
                ImpactHit = Save(ThunderhopFolder, "FX_Thunderhop_Impact_Hit", () => VoltImpact(m, onCharacter: true)),
            };
            AssetDatabase.SaveAssets();
            return set;
        }

        /// <summary>A six-ray star with a cyan halo, tiny arcs crackling at the muzzle and a spray of electric sparks.</summary>
        static GameObject VoltMuzzle(VoltMats m)
        {
            GameObject root = Root("FX_Thunderhop_Muzzle", 0.3f);
            Transform t = root.transform;

            ParticleSystem halo = Ps(t, "Halo", m.Glow, (0.055f, 0.07f), (0f, 0f), (0.85f, 0.98f), 2, world: false, position: new Vector3(0f, 0f, 0.04f));
            Burst(halo, 1);
            Order(halo, -1f);

            ParticleSystem star = Ps(t, "Star", m.Flash, (0.04f, 0.05f), (0f, 0f), (0.52f, 0.62f), 2, world: false, position: new Vector3(0f, 0f, 0.04f));
            Burst(star, 1);
            Spin(star, 180f, 0f);
            SizeOverLife(star, new Keyframe(0f, 0.75f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.6f));
            Order(star, -2f);

            ParticleSystem sparks = Ps(t, "Sparks", m.Spark, (0.06f, 0.12f), (9f, 17f), (0.012f, 0.022f), 14);
            Burst(sparks, 11);
            Shape(sparks, ParticleSystemShapeType.Cone, 0.01f, 32f);
            Stretch(sparks, 0.02f, 1.6f);

            Arcs(t, m.Arc, count: 4, radius: 0.12f, duration: 0.07f, width: 0.018f);

            FlashLight(root, VoltCyan, 4f, 0.05f, 3f);
            return root;
        }

        /// <summary>
        /// A star flash, a shock ring on the surface, current crawling around the hit, white-blue sparks; on the world
        /// also a scorch with dying blue cinders and a wisp of smoke. On a character the burst is bigger and has no scorch.
        /// </summary>
        static GameObject VoltImpact(VoltMats m, bool onCharacter)
        {
            string name = onCharacter ? "FX_Thunderhop_Impact_Hit" : "FX_Thunderhop_Impact";
            float s = onCharacter ? 1.7f : 1.3f; // 1.3 / 1 until the 2026-11-09 punch-up
            GameObject root = Root(name, onCharacter ? 0.8f : 2.6f);
            Transform t = root.transform;

            ParticleSystem halo = Ps(t, "Halo", m.Glow, (0.08f, 0.1f), (0f, 0f), (0.7f * s, 0.85f * s), 2, world: false, position: new Vector3(0f, 0f, 0.06f));
            Burst(halo, 1);
            Order(halo, -1f);

            ParticleSystem star = Ps(t, "Star", m.Flash, (0.05f, 0.065f), (0f, 0f), (0.75f * s, 0.9f * s), 2, world: false, position: new Vector3(0f, 0f, 0.07f));
            Burst(star, 1);
            Spin(star, 180f, 0f);
            SizeOverLife(star, new Keyframe(0f, 0.7f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.45f));
            Order(star, -2f);

            ParticleSystem ring = Ps(t, "Ring", m.Ring, (0.14f, 0.16f), (0f, 0f), (0.65f * s, 0.75f * s), 2, world: false, position: new Vector3(0f, 0f, 0.02f));
            Burst(ring, 1);
            Flat(ring, QuadXY);

            ParticleSystem sparks = Ps(t, "Sparks", m.Spark, (0.15f, 0.38f), (3.5f, 10f), (0.02f, 0.034f), 30);
            Burst(sparks, (short)(onCharacter ? 24 : 20));
            Shape(sparks, ParticleSystemShapeType.Cone, 0.02f, 62f);
            Stretch(sparks, 0.03f, 1.4f);
            Gravity(sparks, 0.6f);
            Drag(sparks, 0.6f);

            Arcs(t, m.Arc, count: 4, radius: onCharacter ? 0.45f : 0.3f, duration: onCharacter ? 0.22f : 0.16f, width: onCharacter ? 0.055f : 0.04f);

            if (!onCharacter)
            {
                Transform medium = Gate(t, "Medium", VfxQuality.Medium);
                ParticleSystem scorch = Ps(medium, "Scorch", m.Scorch, (2.4f, 2.4f), (0f, 0f), (0.28f, 0.34f), 2, world: false, position: new Vector3(0f, 0f, 0.012f));
                Burst(scorch, 1);
                Flat(scorch, QuadXY);
                Fade(scorch, (1f, 0f), (1f, 0.8f), (0f, 1f));
                Order(scorch, 5f);

                ParticleSystem dust = Ps(medium, "Smoke", m.Dust, (0.4f, 0.65f), (0.3f, 0.8f), (0.16f, 0.26f), 3, position: new Vector3(0f, 0f, 0.08f));
                Burst(dust, 1);
                Shape(dust, ParticleSystemShapeType.Cone, 0.03f, 30f);
                Drag(dust, 3f);
                SizeOverLife(dust, new Keyframe(0f, 0.5f), new Keyframe(1f, 1.8f));
            }

            FlashLight(root, VoltCyan, 3f, 0.06f, onCharacter ? 3.5f : 2.5f);
            return root;
        }

        // ================================================================== shared

        static GameObject Tracer(string name, Material streakMat, Material wakeMat, float speed, float length, float width, float wakeWidth,
            float wakeFade, float jitter)
        {
            GameObject root = Root(name, 0.6f);
            LineRenderer streak = Line(root.transform, "Streak", streakMat, width, 6);
            streak.widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 1f));
            streak.colorGradient = Alpha((0f, 0f), (1f, 0.45f), (1f, 1f));
            streak.numCapVertices = 2;
            LineRenderer wake = Line(root.transform, "Wake", wakeMat, wakeWidth, 10);
            wake.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1f));
            var tracer = root.AddComponent<ShotTracerFx>();
            tracer.Configure(streak, wake, speed, length, width, wakeWidth, wakeFade, jitter);
            return root;
        }

        static void Arcs(Transform parent, Material material, int count, float radius, float duration, float width)
        {
            Transform arcRoot = Child(parent, "Arcs");
            var lines = new LineRenderer[count];
            for (int i = 0; i < count; i++)
            {
                lines[i] = Line(arcRoot, $"Arc{i}", material, width, 6);
            }

            var arcs = arcRoot.gameObject.AddComponent<SurfaceArcFx>();
            arcs.Configure(lines, radius, duration);
            SerializedObject so = new SerializedObject(arcs);
            so.FindProperty("width").floatValue = width;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
