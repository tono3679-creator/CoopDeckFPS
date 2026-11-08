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

        // ------------------------------------------------------------------ video

        const int VideoWidth = 1280;
        const int VideoHeight = 720;
        const int VideoFps = 30;
        RenderTexture videoTarget;
        Texture2D videoFrame;
        UnityEditor.Media.MediaEncoder encoder;
        int videoFrames;

        /// <summary>Renders the player's own view (world camera + view-model overlay) and appends it to the video.</summary>
        void RecordFirstPerson()
        {
            Camera world = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "PlayerCamera");
            Camera viewModel = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            viewModel.enabled = true;
            RenderPipeline.SubmitRenderRequest(world, new RenderPipeline.StandardRequest { destination = videoTarget });
            viewModel.enabled = false;
            AppendFrame();
        }

        void RecordView(Vector3 eye, Vector3 at, float fov)
        {
            review.transform.position = eye;
            review.transform.LookAt(at);
            review.fieldOfView = fov;
            review.targetTexture = videoTarget;
            review.Render();
            review.targetTexture = null;
            AppendFrame();
        }

        void AppendFrame()
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = videoTarget;
            videoFrame.ReadPixels(new Rect(0, 0, VideoWidth, VideoHeight), 0, 0);
            videoFrame.Apply();
            RenderTexture.active = previous;
            encoder.AddFrame(videoFrame);
            videoFrames++;
        }

        /// <summary>
        /// A short video of the fireball (run on demand): Screenshots/Fireball.mp4, 1280x720 at 30 fps, every frame one
        /// game step. First person (charge, launch, the hit at range), a camera riding beside the shot, the blast on the
        /// enemy in slow motion, a wall and a floor hit, and the enemy left burning.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_FireballVideo()
        {
            yield return Stage();
            PerformanceSettings.SetVfxQuality(VfxQuality.High);
            Health dummy = Dummy(new Vector3(0f, 0f, 16f));
            Vector3 chest = dummy.transform.position + Vector3.up * 1.15f;
            const float Step = 1f / VideoFps;
            Time.captureDeltaTime = Step;

            videoTarget = Track(new RenderTexture(VideoWidth, VideoHeight, 24) { antiAliasing = 4 });
            videoFrame = Track(new Texture2D(VideoWidth, VideoHeight, TextureFormat.RGBA32, false));
            Directory.CreateDirectory("Screenshots");
            string path = Path.GetFullPath(Path.Combine("Screenshots", "Fireball.mp4"));
            var attributes = new UnityEditor.Media.VideoTrackAttributes
            {
                frameRate = new UnityEditor.Media.MediaRational(VideoFps),
                width = VideoWidth,
                height = VideoHeight,
                includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.High,
            };

            encoder = new UnityEditor.Media.MediaEncoder(path, attributes);
            try
            {
                // ---- 1. first person: the charge in the left palm, the launch, the hit at 15 m, the burn
                AimAt(chest);
                yield return Frames(3);
                for (int i = 0; i < 12; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }

                Cast();
                for (int i = 0; i < 150 && landed.Count == 0; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }

                for (int i = 0; i < 48; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }

                yield return Frames(150);

                // ---- 2. riding beside the shot, then holding on the blast
                AimAt(chest);
                Cast();
                Vector3 eye = player.transform.position + new Vector3(2.2f, 1.9f, -1.2f);
                Vector3 look = player.transform.position + new Vector3(0f, 1.4f, 3f);
                for (int i = 0; i < 150 && landed.Count == 0; i++)
                {
                    Transform shot = Projectile();
                    if (shot != null)
                    {
                        eye = Vector3.Lerp(eye, shot.position + new Vector3(2.4f, 0.5f, -2.2f), 0.5f);
                        look = Vector3.Lerp(look, shot.position + shot.forward * 1.2f, 0.6f);
                    }

                    RecordView(eye, look, 44f);
                    yield return null;
                }

                for (int i = 0; i < 40; i++)
                {
                    RecordView(eye, Vector3.Lerp(look, chest, 0.15f), 44f);
                    yield return null;
                }

                yield return Frames(150);

                // ---- 3. the blast on the enemy in slow motion (one third speed)
                AimAt(chest);
                Cast();
                Transform flying = null;
                for (int i = 0; i < 150; i++)
                {
                    flying = Projectile();
                    if (flying != null && Vector3.Distance(flying.position, chest) < 6f)
                    {
                        break;
                    }

                    yield return null;
                }

                Time.captureDeltaTime = Step / 3f;
                Vector3 slowEye = dummy.transform.position + new Vector3(4.6f, 1.6f, -4.2f);
                for (int i = 0; i < 132; i++)
                {
                    RecordView(slowEye, chest + Vector3.up * 0.2f, 42f);
                    yield return null;
                }

                Time.captureDeltaTime = Step;
                for (int i = 0; i < 30; i++)
                {
                    RecordView(slowEye, chest + Vector3.up * 0.2f, 42f);
                    yield return null;
                }

                yield return Frames(150);

                // ---- 4. a wall hit and a floor hit
                Vector3 wallPoint = new Vector3(-4f, 2.2f, 23.5f);
                AimAt(wallPoint);
                Cast();
                for (int i = 0; i < 70; i++)
                {
                    RecordView(wallPoint + new Vector3(5.5f, 0.3f, -6f), wallPoint + Vector3.back * 0.5f, 46f);
                    yield return null;
                }

                yield return Frames(120);
                Vector3 floorPoint = new Vector3(3f, 0f, 9f);
                AimAt(floorPoint);
                Cast();
                for (int i = 0; i < 70; i++)
                {
                    RecordView(floorPoint + new Vector3(-4.8f, 2.2f, -4.2f), floorPoint + Vector3.up * 0.6f, 46f);
                    yield return null;
                }

                // ---- 5. the enemy keeps burning
                AimAt(chest);
                Cast();
                yield return UntilLanded();
                for (int i = 0; i < 60; i++)
                {
                    RecordView(dummy.transform.position + new Vector3(2.4f, 1.5f, -2.8f), dummy.transform.position + Vector3.up * 1.1f, 44f);
                    yield return null;
                }
            }
            finally
            {
                encoder.Dispose();
                Time.captureDeltaTime = Dt;
            }

            Debug.Log($"[Fireball] {path}: {videoFrames} frames ({videoFrames / (float)VideoFps:0.0} s)");
            Assert.That(videoFrames, Is.GreaterThan(200));
        }

        /// <summary>
        /// The quick cast's palm strike frame by frame (run on demand): the player's own view every second game step
        /// from the press until the hand is back on the rifle, then the body from the front left at the chamber, the
        /// strike, the recoil and the hold. Screenshots/Fireball_QuickCast.png; logs ("[QuickCast]") the first-person
        /// left wrist in the eyes' space, how far the shoulder followed the cast and the camera kick.
        /// </summary>
        [UnityTest]
        public IEnumerator Capture_QuickCastStrike()
        {
            yield return Stage();
            PerformanceSettings.SetVfxQuality(VfxQuality.High);
            Health dummy = Dummy(new Vector3(0f, 0f, 16f));
            AimAt(dummy.transform.position + Vector3.up * 1.15f);
            yield return Frames(5);
            var arms = player.GetComponentInChildren<FirstPersonArms>(true);
            var shaker = player.GetComponentsInChildren<CameraShaker>(true).First();
            Transform eyes = look.PitchPivot;
            Camera viewModel = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            Transform elbow = arms.ForeArm(true);
            Transform shoulder = elbow.parent;
            float halfHeight = Mathf.Tan(viewModel.fieldOfView * 0.5f * Mathf.Deg2Rad);

            // screen position in units of the half height (-1 = lower edge; |x| < 16/9 is inside a 16:9 view)
            string Screen(Vector3 world)
            {
                Vector3 p = viewModel.transform.InverseTransformPoint(world);
                return p.z <= 0f ? "behind" : $"({p.x / p.z / halfHeight:0.00}, {p.y / p.z / halfHeight:0.00})";
            }

            Cast();
            for (int f = 1; f <= 30; f++)
            {
                yield return null;
                Vector3 wrist = eyes.InverseTransformPoint(arms.Hand(true).position);
                // the arm mesh starts a quarter of the way down the upper arm (fox_fp_arms.py UPPER_ARM_CUT)
                Vector3 cut = Vector3.Lerp(shoulder.position, elbow.position, 0.25f);
                Debug.Log($"[QuickCast] {f * Dt:0.000} s: wrist {wrist.ToString("0.000")} ({wrist.magnitude:0.000} m), " +
                          $"shoulder reach {arms.CastReach * 100f:0.0} cm, on rifle {driver.LeftHandOnRifle:0.00}, kick {shaker.KickAmount:0.00}, " +
                          $"screen: elbow {Screen(elbow.position)} arm cut {Screen(cut)}");
                if (f % 2 == 0 && f <= 24)
                {
                    FirstPerson($"FP {f * Dt:0.000} s");
                }
            }

            Save("Fireball_QuickCast", 4);
            yield return Frames(90);

            // the body (only a shadow for its own player): the chest turning into the strike
            foreach (Renderer r in player.transform.Find("BodyVisual").GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.On;
            }

            Vector3 side = player.transform.TransformPoint(new Vector3(-2.2f, 1.25f, 1.6f));
            Vector3 chest = player.transform.TransformPoint(new Vector3(-0.1f, 1.0f, 0.25f));
            Cast();
            foreach ((int frames, string label) in new[] { (4, "chamber 0.067 s"), (3, "strike 0.117 s"), (3, "recoil 0.167 s"), (6, "hold 0.267 s") })
            {
                yield return Frames(frames);
                Shoot("body: " + label, side, chest, 30f);
            }

            Save("Fireball_QuickCast_Body", 4);
        }

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
