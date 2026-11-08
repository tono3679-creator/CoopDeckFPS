#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Animation;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Player;
using CoopDeck.Skills;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Review images of the fireball (run on demand, after the bootstrap): the real player prefab casts the real card
    /// in a small lit stage with the sandbox post-processing. Screenshots/Fireball_*.png:
    ///   Fireball_FirstPerson  - the charge in the left hand, the launch, the shot leaving, a blast at range
    ///   Fireball_Flight       - the shot from the side, three-quarter front and behind
    ///   Fireball_EnemyHit     - flash, swelling blast, billows, aftermath and the burning enemy
    ///   Fireball_GroundWall   - blasts on the floor and on a wall, the scorch and lingering smoke
    ///   Fireball_Quality      - Low / Medium / High in flight and at the blast
    ///   Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter FireballCaptureTests
    /// </summary>
    [Explicit("writes review images; run on demand")]
    [Category("Capture")]
    public class FireballCaptureTests
    {
        const float Dt = 1f / 60f;
        const int W = 640;
        const int H = 400;
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const string DummyPath = "Assets/_Game/Prefabs/Enemies/PF_TargetDummy.prefab";
        const string CardPath = "Assets/_Game/ScriptableObjects/Cards/CD_Fireball.asset";
        const string VolumePath = "Assets/_Game/Settings/Rendering/VP_Sandbox.asset";
        const string BurningPath = "Assets/_Game/Prefabs/Skills/Fireball/FX_Burning.prefab";

        readonly List<Object> spawned = new List<Object>();
        readonly List<(string label, Texture2D image)> shots = new List<(string, Texture2D)>();
        readonly List<ProjectileImpact> landed = new List<ProjectileImpact>();

        GameObject player;
        FpsLook look;
        SkillCaster caster;
        CharacterAnimatorDriver driver;
        CardDefinition card;
        Camera review;
        RenderTexture target;
        RenderTexture firstPerson;

        sealed class Idle : ICharacterCommandSource
        {
            public CharacterCommands GetCommands(float deltaTime) => default;
        }

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = Dt;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            PerformanceSettings.ResetVfxQuality();
            foreach (Object o in spawned.Where(o => o != null))
            {
                Object.Destroy(o);
            }

            spawned.Clear();
        }

        T Track<T>(T o) where T : Object
        {
            spawned.Add(o);
            return o;
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        Material Lit(Color color)
        {
            var m = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", 0.15f);
            return m;
        }

        GameObject Block(Vector3 position, Vector3 scale, Color color)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Lit(color);
            return go;
        }

        IEnumerator Stage()
        {
            Block(new Vector3(0f, -0.5f, 10f), new Vector3(60f, 1f, 60f), Palette.Floor);
            Block(new Vector3(0f, 3f, 24f), new Vector3(30f, 6f, 1f), Palette.Wall);
            Block(new Vector3(-6f, 1.5f, 12f), new Vector3(1f, 3f, 1f), Palette.Trim);
            var sun = Track(new GameObject("Sun")).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = Color.Lerp(Color.white, Palette.SkyHorizon, 0.5f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(32f, 240f, 0f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.SkyTop * 0.8f;
            RenderSettings.ambientEquatorColor = Palette.SkyHorizon * 0.6f;
            RenderSettings.ambientGroundColor = Palette.Shadow * 0.6f;
            var volumeGo = Track(new GameObject("Volume"));
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);

            var auras = Track(new GameObject("Auras")).AddComponent<ElementAuraPresenter>();
            auras.Configure(null, null, null, null, AssetDatabase.LoadAssetAtPath<GameObject>(BurningPath));

            player = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), new Vector3(0f, 0.05f, 0f), Quaternion.identity));
            player.GetComponent<FpsCharacter>().CommandSource = new Idle();
            look = player.GetComponent<FpsLook>();
            caster = player.GetComponent<SkillCaster>();
            driver = player.GetComponent<FpsCharacter>().BodyAnimator;
            foreach (Camera c in player.GetComponentsInChildren<Camera>(true))
            {
                c.enabled = false;
            }

            foreach (AudioListener l in player.GetComponentsInChildren<AudioListener>(true))
            {
                l.enabled = false;
            }

            card = AssetDatabase.LoadAssetAtPath<CardDefinition>(CardPath);
            Assert.That(card, Is.Not.Null, "run the bootstrap");
            caster.ProjectileEnded += impact => landed.Add(impact);

            review = Track(new GameObject("ReviewCamera")).AddComponent<Camera>();
            review.fieldOfView = 46f;
            review.nearClipPlane = 0.05f;
            review.clearFlags = CameraClearFlags.SolidColor;
            review.backgroundColor = Palette.SkyTop;
            review.allowHDR = true;
            UniversalAdditionalCameraData data = review.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            review.cullingMask = ~(1 << GameLayers.ViewModelIndex);
            review.enabled = false;
            target = Track(new RenderTexture(W, H, 24) { antiAliasing = 4 });
            firstPerson = Track(new RenderTexture(W, H, 24) { antiAliasing = 4 });

            foreach (Camera c in player.GetComponentsInChildren<Camera>(true))
            {
                c.GetUniversalAdditionalCameraData().renderPostProcessing = c.name == "PlayerCamera";
                c.allowHDR = true;
            }

            yield return Frames(30);
        }

        Health Dummy(Vector3 position)
        {
            GameObject dummy = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DummyPath), position, Quaternion.Euler(0f, 180f, 0f)));
            Physics.SyncTransforms();
            var health = dummy.GetComponent<Health>();
            health.SetMaxHealth(100000f, true); // survives every shot of the session, so each blast is on it
            return health;
        }

        void AimAt(Vector3 point)
        {
            Vector3 eye = look.PitchPivot.position;
            Vector3 d = (point - eye).normalized;
            look.SetRotation(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, Mathf.Asin(d.y) * Mathf.Rad2Deg);
        }

        void Cast()
        {
            landed.Clear();
            driver.Skill(SkillMotion.QuickCast);
            caster.CastCard(card);
        }

        void Shoot(string label, Vector3 eye, Vector3 at, float fov = 46f)
        {
            review.transform.position = eye;
            review.transform.LookAt(at);
            review.fieldOfView = fov;
            review.targetTexture = target;
            review.Render();
            review.targetTexture = null;
            shots.Add((label, Read(target)));
        }

        /// <summary>The player's own view: the world camera with its view-model overlay, rendered as one URP camera stack.</summary>
        void FirstPerson(string label)
        {
            Camera world = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "PlayerCamera");
            Camera viewModel = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            viewModel.enabled = true; // overlays render only while enabled; as an overlay it never draws to the screen on its own
            var request = new RenderPipeline.StandardRequest { destination = firstPerson };
            Assert.That(RenderPipeline.SupportsRenderRequest(world, request), Is.True, "URP renders the stack into a texture");
            RenderPipeline.SubmitRenderRequest(world, request);
            viewModel.enabled = false;
            shots.Add((label, Read(firstPerson)));
        }

        static Texture2D Read(RenderTexture rt)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;
            var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            return image;
        }

        void Save(string name, int cols)
        {
            int rows = (shots.Count + cols - 1) / cols;
            var sheet = new Texture2D(W * cols, H * rows, TextureFormat.RGB24, false);
            sheet.SetPixels(Enumerable.Repeat(new Color(0.1f, 0.1f, 0.12f), W * cols * H * rows).ToArray());
            for (int i = 0; i < shots.Count; i++)
            {
                sheet.SetPixels(i % cols * W, (rows - 1 - i / cols) * H, W, H, shots[i].image.GetPixels());
            }

            sheet.Apply();
            Directory.CreateDirectory("Screenshots");
            string path = Path.Combine("Screenshots", name + ".png");
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Debug.Log($"[Fireball] {path}: " + string.Join(" | ", shots.Select((s, i) => $"{i + 1}:{s.label}")));
            foreach ((string _, Texture2D image) in shots)
            {
                Object.Destroy(image);
            }

            shots.Clear();
            Object.Destroy(sheet);
        }

        IEnumerator UntilLanded()
        {
            for (int i = 0; i < 180 && landed.Count == 0; i++)
            {
                yield return null;
            }

            Assert.That(landed.Count, Is.EqualTo(1));
        }

        Transform Projectile() => Object.FindObjectsByType<FireballProjectileFx>().FirstOrDefault(p => !p.IsEnding)?.transform;

        [UnityTest]
        public IEnumerator Capture_Fireball()
        {
            yield return Stage();
            PerformanceSettings.SetVfxQuality(VfxQuality.High);
            Health dummy = Dummy(new Vector3(0f, 0f, 16f));
            Vector3 chest = dummy.transform.position + Vector3.up * 1.15f;

            // ---- first person: charge, launch, leaving, blast at range
            AimAt(chest);
            yield return Frames(5);
            Cast();
            yield return Frames(3);
            FirstPerson("FP 0.05 s: energy gathers in the left palm");
            yield return Frames(4);
            FirstPerson("FP 0.12 s: white-hot core, flames wrap it");
            yield return Frames(3);
            FirstPerson("FP 0.17 s: launched");
            yield return Frames(4);
            FirstPerson("FP 0.23 s: joining the crosshair path");
            yield return UntilLanded();
            yield return Frames(4);
            FirstPerson("FP blast at 15 m (+0.07 s)");
            yield return Frames(12);
            FirstPerson("FP blast +0.27 s");
            Save("Fireball_FirstPerson", 3);
            yield return Frames(200);

            // ---- flight from outside
            AimAt(chest);
            Cast();
            yield return Frames(20);
            Transform shot = Projectile();
            Assert.That(shot, Is.Not.Null);
            Shoot("flight: side", shot.position + new Vector3(3.2f, 0.4f, 0.4f), shot.position, 34f);
            yield return Frames(1);
            shot = Projectile();
            Shoot("flight: 3/4 front", shot.position + new Vector3(1.5f, 0.5f, 3.2f), shot.position, 34f);
            yield return Frames(1);
            shot = Projectile();
            Shoot("flight: behind", shot.position + new Vector3(0.9f, 0.6f, -3.4f), shot.position + Vector3.forward * 1.5f, 34f);
            yield return Frames(1);
            shot = Projectile();
            Shoot("flight: close side", shot.position + new Vector3(1.4f, 0.15f, 0f), shot.position + Vector3.back * 0.3f, 30f);
            yield return UntilLanded();
            Vector3 hit = landed[0].Point;
            Shoot("enemy hit +0.00 s: flash", hit + new Vector3(4.2f, 1.2f, -5f), hit + Vector3.up * 0.4f, 44f);
            yield return Frames(4);
            Shoot("+0.07 s: fireball swells", hit + new Vector3(4.2f, 1.2f, -5f), hit + Vector3.up * 0.5f, 44f);
            yield return Frames(6);
            Shoot("+0.17 s: billows, ring, sparks", hit + new Vector3(4.2f, 1.2f, -5f), hit + Vector3.up * 0.6f, 44f);
            yield return Frames(9);
            Shoot("+0.32 s: rising, burning away", hit + new Vector3(4.2f, 1.2f, -5f), hit + Vector3.up * 0.8f, 44f);
            yield return Frames(18);
            Shoot("+0.62 s: embers, debris, smoke", hit + new Vector3(4.2f, 1.2f, -5f), hit + Vector3.up * 0.8f, 44f);
            yield return Frames(30);
            Shoot("+1.12 s: the enemy keeps burning", dummy.transform.position + new Vector3(2.2f, 1.4f, -2.6f), dummy.transform.position + Vector3.up, 44f);
            Save("Fireball_Flight_EnemyHit", 3);
            yield return Frames(200);

            // ---- ground and wall
            Vector3 floorPoint = new Vector3(3f, 0f, 9f);
            AimAt(floorPoint);
            Cast();
            yield return UntilLanded();
            hit = landed[0].Point;
            yield return Frames(5);
            Shoot("floor +0.08 s", hit + new Vector3(-4.5f, 2f, -4f), hit + Vector3.up * 0.5f, 46f);
            yield return Frames(10);
            Shoot("floor +0.25 s", hit + new Vector3(-4.5f, 2f, -4f), hit + Vector3.up * 0.6f, 46f);
            yield return Frames(36);
            Shoot("floor +0.85 s: scorch, embers, smoke", hit + new Vector3(-3.2f, 2.4f, -3f), hit, 46f);
            yield return Frames(120);
            AimAt(new Vector3(-4f, 2.2f, 23.5f));
            Cast();
            yield return UntilLanded();
            hit = landed[0].Point;
            yield return Frames(6);
            Shoot("wall +0.10 s: along the wall", hit + new Vector3(5f, 0.5f, -4.5f), hit, 46f);
            yield return Frames(14);
            Shoot("wall +0.33 s", hit + new Vector3(5f, 0.5f, -4.5f), hit, 46f);
            yield return Frames(30);
            Shoot("wall +0.83 s", hit + new Vector3(5f, 0.5f, -4.5f), hit, 46f);
            Save("Fireball_GroundWall", 3);
            yield return Frames(200);

            // ---- quality
            foreach (VfxQuality quality in new[] { VfxQuality.Low, VfxQuality.Medium, VfxQuality.High })
            {
                PerformanceSettings.SetVfxQuality(quality);
                AimAt(chest);
                Cast();
                yield return Frames(20);
                shot = Projectile();
                Shoot($"{quality}: flight", shot.position + new Vector3(2.4f, 0.4f, 0.3f), shot.position, 34f);
                yield return UntilLanded();
                hit = landed[0].Point;
                yield return Frames(8);
                Shoot($"{quality}: blast +0.13 s", hit + new Vector3(4.2f, 1.2f, -5f), hit + Vector3.up * 0.5f, 44f);
                yield return Frames(200);
            }

            Save("Fireball_Quality", 2);
        }
    }
}
#endif
