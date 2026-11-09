using System.IO;
using System.Linq;
using CoopDeck.Characters;
using CoopDeck.Core;
using CoopDeck.Effects;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The Lightning Rabbit's look and its own lightning (Docs/LIGHTNING_RABBIT.md §3.7): the per-part surface mask and the
    /// glow mask baked in Blender, the glowing triangles copied per LOD and for the first-person arms on the same bones, the
    /// glove-lens and ear-tip markers, the kit's ElectricBodyFx and its effects, and the pulse rules (plain C#).
    /// </summary>
    public class LightningRabbitLookTests
    {
        const string Art = "Assets/_Game/Art/Characters/LightningRabbit";
        const string SurfaceMaskPath = Art + "/Textures/T_LightningRabbit_SurfaceMask.png";
        const string FirstPersonSurfaceMaskPath = Art + "/Textures/T_LightningRabbit_FPArms_SurfaceMask.png";
        const string GlowMaskPath = Art + "/Textures/T_LightningRabbit_GlowMask.png";
        const string FirstPersonGlowMaskPath = Art + "/Textures/T_LightningRabbit_FPArms_GlowMask.png";
        const string BodyPrefabPath = "Assets/_Game/Prefabs/Characters/PF_LightningRabbit_Body.prefab";
        const string KitPath = "Assets/_Game/Prefabs/Characters/PF_CharacterKit_LightningRabbit.prefab";
        const string GlowShader = "CoopDeck/Character/Glow Lines";

        // FoxGunnerLookFactory.Presets (softness, smoothness, metallic, warmth) as 8-bit texels
        static readonly (string name, Color32 texel)[] Presets =
        {
            ("Fur", Texel(0.85f, 0.12f, 0f, 0.55f)), ("Face", Texel(1f, 0.16f, 0f, 0.8f)), ("Eye", Texel(0.5f, 0.75f, 0f, 0f)),
            ("Cloth", Texel(0.6f, 0.06f, 0f, 0.15f)), ("Leather", Texel(0.3f, 0.42f, 0f, 0f)), ("Metal", Texel(0.15f, 0.62f, 0.85f, 0f)),
            ("Glass", Texel(0.2f, 0.85f, 0f, 0f)),
        };

        static Color32 Texel(float r, float g, float b, float a) => new Color(r, g, b, a);

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path + " missing: run the bootstrap");
            return asset;
        }

        static Color32[] Pixels(string path, out int size)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            texture.LoadImage(File.ReadAllBytes(path));
            size = texture.width;
            Color32[] pixels = texture.GetPixels32();
            Object.DestroyImmediate(texture);
            return pixels;
        }

        /// <summary>Share of the used texels (any channel set) holding each preset, within 2/255.</summary>
        static float[] PresetShares(string path)
        {
            Color32[] pixels = Pixels(path, out int size);
            Assert.That(size, Is.EqualTo(2048), path);
            var counts = new int[Presets.Length];
            int used = 0;
            foreach (Color32 p in pixels)
            {
                if (p.r == 0 && p.g == 0 && p.b == 0 && p.a == 0)
                {
                    continue;
                }

                used++;
                for (int k = 0; k < Presets.Length; k++)
                {
                    Color32 q = Presets[k].texel;
                    if (Mathf.Abs(p.r - q.r) <= 2 && Mathf.Abs(p.g - q.g) <= 2 && Mathf.Abs(p.b - q.b) <= 2 && Mathf.Abs(p.a - q.a) <= 2)
                    {
                        counts[k]++;
                        break;
                    }
                }
            }

            Assert.That(used, Is.GreaterThan(pixels.Length / 4), path + ": the mask covers the UV islands");
            float[] shares = counts.Select(c => (float)c / used).ToArray();
            Assert.That(shares.Sum(), Is.GreaterThan(0.97f), path + ": every used texel holds one of the presets");
            return shares;
        }

        static float GlowShare(string path)
        {
            Color32[] pixels = Pixels(path, out int size);
            Assert.That(size, Is.EqualTo(2048), path);
            return (float)pixels.Count(p => p.r > 12) / pixels.Length;
        }

        static void AssertDataTexture(string path, TextureImporterType type)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            Assert.That(importer, Is.Not.Null, path);
            Assert.That((importer.textureType, importer.sRGBTexture), Is.EqualTo((type, false)), path + ": linear data");
        }

        [Test]
        public void SurfaceMasks_HoldEveryPart_AsLinearData()
        {
            AssertDataTexture(SurfaceMaskPath, TextureImporterType.Default);
            AssertDataTexture(FirstPersonSurfaceMaskPath, TextureImporterType.Default);
            float[] body = PresetShares(SurfaceMaskPath);
            string Shares(float[] s) => string.Join(", ", Presets.Select((p, i) => $"{p.name} {s[i]:P1}"));
            TestContext.WriteLine("body: " + Shares(body));
            Assert.That(body[0], Is.InRange(0.15f, 0.45f), "fur: hair, arms, ears, tail, midriff");
            Assert.That(body[1], Is.InRange(0.02f, 0.10f), "face and inner ears");
            Assert.That(body[2], Is.InRange(0.0005f, 0.01f), "eyes");
            Assert.That(body[3], Is.InRange(0.2f, 0.5f), "cloth: top, vest, scarf, trousers");
            Assert.That(body[4], Is.InRange(0.12f, 0.4f), "leather: gloves, belt, boots, straps");
            Assert.That(body[5], Is.InRange(0.02f, 0.12f), "metal: headset, buckle, toe caps, grommets");
            Assert.That(body[6], Is.InRange(0.005f, 0.04f), "glass: lenses, battery tubes, glowing marks");

            float[] arms = PresetShares(FirstPersonSurfaceMaskPath);
            TestContext.WriteLine("first-person arms: " + Shares(arms));
            Assert.That(arms[0], Is.GreaterThan(0.25f), "furred forearms and fingers");
            Assert.That(arms[4], Is.GreaterThan(0.2f), "gloves and wrist bands");
            Assert.That(arms[5] + arms[6], Is.GreaterThan(0.03f), "glove plates and lenses");
            Assert.That(arms[1] + arms[2], Is.LessThan(0.01f), "no face or eyes on the arms");
        }

        [Test]
        public void GlowMasks_MarkOnlyTheGlowingLines()
        {
            AssertDataTexture(GlowMaskPath, TextureImporterType.SingleChannel);
            AssertDataTexture(FirstPersonGlowMaskPath, TextureImporterType.SingleChannel);
            Assert.That(GlowShare(GlowMaskPath), Is.InRange(0.001f, 0.02f), "bolt marks, lenses and battery tubes only");
            Assert.That(GlowShare(FirstPersonGlowMaskPath), Is.InRange(0.002f, 0.03f), "the glove lenses");
        }

        [Test]
        public void Materials_UseTheBakedMasks()
        {
            var soft = Load<Material>(Art + "/Materials/M_LightningRabbit_SoftToon.mat");
            var fp = Load<Material>(Art + "/Materials/M_LightningRabbit_SoftToon_FirstPerson.mat");
            Assert.That(AssetDatabase.GetAssetPath(soft.GetTexture("_SurfaceMask")), Is.EqualTo(SurfaceMaskPath));
            Assert.That(AssetDatabase.GetAssetPath(fp.GetTexture("_SurfaceMask")), Is.EqualTo(FirstPersonSurfaceMaskPath));
            var glow = Load<Material>(Art + "/Materials/M_LightningRabbit_Glow.mat");
            var fpGlow = Load<Material>(Art + "/Materials/M_LightningRabbit_Glow_FirstPerson.mat");
            Assert.That((glow.shader.name, fpGlow.shader.name), Is.EqualTo((GlowShader, GlowShader)));
            Assert.That(AssetDatabase.GetAssetPath(glow.GetTexture("_GlowMask")), Is.EqualTo(GlowMaskPath));
            Assert.That(AssetDatabase.GetAssetPath(fpGlow.GetTexture("_GlowMask")), Is.EqualTo(FirstPersonGlowMaskPath));
            Assert.That(glow.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
        }

        [Test]
        public void EachLod_HasAGlowCopy_OnTheSameBones()
        {
            var prefab = Load<GameObject>(BodyPrefabPath);
            LOD[] lods = prefab.GetComponent<LODGroup>().GetLODs();
            Assert.That(lods.Length, Is.EqualTo(3));
            for (int i = 0; i < lods.Length; i++)
            {
                var source = lods[i].renderers.OfType<SkinnedMeshRenderer>().Single(r => r.name.EndsWith("_LOD" + i));
                var glow = lods[i].renderers.OfType<SkinnedMeshRenderer>().Single(r => r.name == source.name + "_Glow");
                int sourceTris = source.sharedMesh.triangles.Length / 3, glowTris = glow.sharedMesh.triangles.Length / 3;
                TestContext.WriteLine($"LOD{i}: glow {glowTris} of {sourceTris} triangles");
                Assert.That((float)glowTris / sourceTris, Is.InRange(0.002f, 0.06f), $"LOD{i}: only the glowing triangles");
                Assert.That(glow.bones, Is.EqualTo(source.bones), $"LOD{i}: same bones");
                Assert.That(glow.rootBone, Is.SameAs(source.rootBone));
                Assert.That(glow.sharedMesh.bindposes, Is.EqualTo(source.sharedMesh.bindposes), $"LOD{i}: same bind poses");
                Assert.That(glow.sharedMaterial.shader.name, Is.EqualTo(GlowShader));
                Assert.That(glow.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            }
        }

        [Test]
        public void Markers_SitOnTheGloveLensesAndEarTips()
        {
            var prefab = Load<GameObject>(BodyPrefabPath);
            Transform Find(string n) => prefab.GetComponentsInChildren<Transform>(true).Single(t => t.name == n);
            foreach ((string lens, string hand) in new[] { ("GloveLens_L", "LeftHand"), ("GloveLens_R", "RightHand") })
            {
                Transform marker = Find(lens);
                Assert.That(marker.parent.name, Is.EqualTo(hand));
                Assert.That(Vector3.Distance(marker.position, marker.parent.position), Is.LessThan(0.12f), lens + " on the back of the hand");
            }

            foreach ((string tip, string bone) in new[] { ("EarTip_L", "Ear_L3"), ("EarTip_R", "Ear_R3") })
            {
                Transform marker = Find(tip);
                Assert.That(marker.parent.name, Is.EqualTo(bone));
                Assert.That(marker.position.y, Is.GreaterThan(marker.parent.position.y + 0.03f), tip + " above the last ear joint");
                Assert.That(marker.position.y, Is.InRange(1.45f, 1.9f), tip + " at the top of the 1.7 m rabbit");
            }
        }

        [Test]
        public void Kit_HasTheElectricEffects_WiredToBodyAndArms()
        {
            var kit = Load<GameObject>(KitPath);
            var fx = kit.GetComponent<ElectricBodyFx>();
            Assert.That(fx, Is.Not.Null, "ElectricBodyFx on the kit root");
            Assert.That(fx.BodyGlow.Count, Is.EqualTo(3), "a glow copy per LOD");
            Assert.That(fx.ViewGlow.Count, Is.EqualTo(1), "the first-person arms' glow copy");
            Renderer viewGlow = fx.ViewGlow[0];
            Assert.That(viewGlow.gameObject.layer, Is.EqualTo(GameLayers.ViewModelIndex), "drawn by the view-model camera");
            Assert.That(viewGlow.sharedMaterial.name, Is.EqualTo("M_LightningRabbit_Glow_FirstPerson"));
            var arms = kit.GetComponentInChildren<CoopDeck.Animation.FirstPersonArms>(true);
            Assert.That(viewGlow.transform.parent, Is.SameAs(arms.GetComponentsInChildren<SkinnedMeshRenderer>(true).First().transform.parent));
            Assert.That(fx.ViewHands.Select(h => h.parent).ToArray(), Is.EqualTo(new[] { arms.Hand(true), arms.Hand(false) }), "sparks leave both gloves");
            Assert.That(fx.BodyHands.Count, Is.EqualTo(2));
            Assert.That(fx.EarTips.Count, Is.EqualTo(2));

            SerializedObject so = new SerializedObject(fx);
            var spark = (GameObject)so.FindProperty("sparkFx").objectReferenceValue;
            var arc = (GameObject)so.FindProperty("arcFx").objectReferenceValue;
            var ghost = (Material)so.FindProperty("ghostMaterial").objectReferenceValue;
            Assert.That(spark != null && spark.GetComponent<PooledFx>() != null, Is.True, "pooled spark prefab");
            Assert.That(spark.GetComponentsInChildren<ParticleSystem>().All(p => p.main.simulationSpace == ParticleSystemSimulationSpace.Local),
                Is.True, "sparks ride along with the hand");
            var bodySpark = (GameObject)so.FindProperty("bodySparkFx").objectReferenceValue;
            Assert.That(bodySpark != null && bodySpark.GetComponent<PooledFx>() != null, Is.True, "pooled body-scale spark prefab");
            float Size(GameObject p) => p.transform.Find("Sparks").GetComponent<ParticleSystem>().main.startSize.constantMax;
            Assert.That(Size(bodySpark), Is.GreaterThan(Size(spark) * 2f), "body sparks read from metres away; first-person ones stay small");
            Assert.That(arc != null && arc.GetComponent<LightningArcFx>() != null, Is.True, "pooled arc prefab");
            Assert.That(ghost != null && ghost.shader.name == "CoopDeck/Character/Electric Ghost", Is.True, "afterimage material");
            Assert.That(so.FindProperty("ghostSource").objectReferenceValue.name, Does.EndWith("_LOD2"), "afterimages bake the lowest LOD");
        }

        // ---------------------------------------------------------------- ElectricPulse (plain C#)

        static int Sparks(ElectricPulse pulse, float seconds, bool sprinting)
        {
            int n = 0;
            for (float t = 0f; t < seconds; t += 1f / 60f)
            {
                pulse.Step(1f / 60f, sprinting);
                if (pulse.TakeSpark()) n++;
            }

            return n;
        }

        [Test]
        public void Pulse_BreathesAtRest_AndBurnsBrighterWhileSprinting()
        {
            var pulse = new ElectricPulse(3);
            float low = float.MaxValue, high = 0f;
            for (int i = 0; i < 600; i++)
            {
                pulse.Step(1f / 60f, false);
                low = Mathf.Min(low, pulse.Boost);
                high = Mathf.Max(high, pulse.Boost);
            }

            Assert.That(low, Is.GreaterThanOrEqualTo(0f));
            Assert.That(high, Is.InRange(ElectricPulse.BreathAmplitude * 0.9f, ElectricPulse.BreathAmplitude * 1.01f), "a slow breath");
            for (int i = 0; i < 120; i++)
            {
                pulse.Step(1f / 60f, true);
            }

            Assert.That(pulse.Boost, Is.GreaterThan(ElectricPulse.SprintBoost * 0.95f), "sprinting raises the glow");
            Assert.That(pulse.Activity, Is.InRange(0.95f, 1.05f));
        }

        [Test]
        public void Pulse_SpikesOnKicks_AndSettles()
        {
            var pulse = new ElectricPulse(5);
            pulse.Step(1f / 60f, false);
            pulse.Kick(ElectricPulse.DashSpike);
            pulse.Kick(ElectricPulse.HitSpike); // weaker: does not cut the dash spike
            pulse.Step(1f / 60f, false);
            Assert.That(pulse.Boost, Is.GreaterThan(ElectricPulse.DashSpike * 0.9f));
            for (int i = 0; i < 60; i++)
            {
                pulse.Step(1f / 60f, false);
            }

            Assert.That(pulse.Spike, Is.LessThan(0.05f), "back to the breath within a second");
            Assert.That(pulse.Boost, Is.LessThan(ElectricPulse.BreathAmplitude + 0.05f));
        }

        [Test]
        public void Pulse_FollowsTheWeaponCharge_AndFlaresOnItsDischarge()
        {
            var pulse = new ElectricPulse(17);
            float Settle(float charge)
            {
                for (int i = 0; i < 60; i++)
                {
                    pulse.Step(1f / 60f, false, charge);
                }

                return pulse.Boost;
            }

            float empty = Settle(0f);
            float half = Settle(0.5f);
            Assert.That(pulse.Charge, Is.EqualTo(0.5f).Within(0.01f), "the glow follows the charge within a second");
            float full = Settle(1f);
            // the breath (0..BreathAmplitude) runs underneath, so compare within its swing
            Assert.That(half - empty, Is.InRange(ElectricPulse.ChargeGlow * 0.5f - ElectricPulse.BreathAmplitude,
                ElectricPulse.ChargeGlow * 0.5f + ElectricPulse.BreathAmplitude), "the marks brighten as the charge builds");
            Assert.That(full, Is.GreaterThan(ElectricPulse.ChargeGlow * 0.95f));
            Assert.That(pulse.Activity, Is.GreaterThan(0.95f), "a full charge sparks like a sprint");

            pulse.Kick(ElectricPulse.BurstSpike); // the discharge; the charge is spent
            pulse.Step(1f / 60f, false, 0f);
            Assert.That(pulse.Boost, Is.GreaterThan(ElectricPulse.BurstSpike * 0.9f), "the biggest flare");
            Assert.That(ElectricPulse.BurstSpike, Is.GreaterThan(ElectricPulse.DashSpike));
            Assert.That(Settle(0f), Is.LessThan(ElectricPulse.BreathAmplitude + 0.05f), "back to the breath");
        }

        [Test]
        public void Pulse_SparksOftenerWhenBusy()
        {
            int idle = Sparks(new ElectricPulse(11), 20f, false);
            int busy = Sparks(new ElectricPulse(11), 20f, true);
            TestContext.WriteLine($"sparks in 20 s: idle {idle}, sprinting {busy}");
            Assert.That(idle, Is.InRange(10, 25), "about one a second at rest");
            Assert.That(busy, Is.GreaterThan(idle * 2), "several a second while sprinting");

            var kicked = new ElectricPulse(13);
            kicked.Step(1f / 60f, false);
            kicked.Kick(ElectricPulse.CastSpike);
            int soon = 0;
            for (int i = 0; i < 30; i++)
            {
                kicked.Step(1f / 60f, false);
                if (kicked.TakeSpark()) soon++;
            }

            Assert.That(soon, Is.GreaterThanOrEqualTo(1), "a cast makes a spark right away");
        }
    }
}
