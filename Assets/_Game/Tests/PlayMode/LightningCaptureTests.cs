using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Characters;
using CoopDeck.Core;
using CoopDeck.Player;
using CoopDeck.Weapons;
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
    /// Review videos of the Thunderhop's 帯電連射 lightning (Docs/LIGHTNING_VFX.md), run on demand:
    /// Screenshots/Lightning/Lightning_Current.mp4 with the generated materials, and Lightning_GlowBoost.mp4 with a
    /// stronger, wider outer glow for comparison (the materials are restored afterwards; rerunning the generator also
    /// resets them). 1280x720, 30 fps, every frame one game step: first person at full speed (two discharges), a side
    /// view in quarter-speed slow motion, a close look at the hit, and the Ally X default quality (Medium).
    /// <see cref="Capture_Acceptance"/> records the discharge review at 60 fps and real speed from the same first-person
    /// spot as the 7.6 s mark of Lightning_Fixed.mp4 (9 m, Medium): two targets with full-auto fire through two
    /// discharges, one target, and a chain target removed in the discharge frame (before any delayed display), with
    /// every frame saved as a still and the discharge frames logged.
    /// </summary>
    [Explicit("writes review videos; run on demand")]
    [Category("Capture")]
    public class LightningCaptureTests
    {
        const float Dt = 1f / 30f; // the review videos; the acceptance capture runs at 60 fps
        const int Width = 1280;
        const int Height = 720;
        const int Fps = 30;
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const string DummyPath = "Assets/_Game/Prefabs/Enemies/PF_TargetDummy.prefab";
        const string VolumePath = "Assets/_Game/Settings/Rendering/VP_Sandbox.asset";
        const string SkyPath = "Assets/_Game/Art/Environments/Materials/M_Sky_Procedural.mat";
        const string MaterialFolder = "Assets/_Game/Art/VFX/Lightning/Materials/";

        readonly List<Object> spawned = new List<Object>();
        GameObject player;
        FpsLook look;
        HitscanWeapon weapon;
        Camera review;
        RenderTexture videoTarget;
        Texture2D videoFrame;
        UnityEditor.Media.MediaEncoder encoder;
        int frames;
        int discharges;
        string frameFolder;
        int fps = Fps;
        int dumpEvery = 3;
        readonly List<int> dischargeFrames = new List<int>();
        Vector3 aimPoint;
        bool asyncShaders;

        /// <summary>Holds the trigger and keeps the crosshair on the target (pulls the recoil back like a player).</summary>
        sealed class HoldFire : ICharacterCommandSource
        {
            public LightningCaptureTests Owner;
            public bool Firing;

            public CharacterCommands GetCommands(float deltaTime)
            {
                FpsLook l = Owner.look;
                Vector3 d = (Owner.aimPoint - l.PitchPivot.position).normalized;
                float yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                float pitch = Mathf.Asin(d.y) * Mathf.Rad2Deg;
                return new CharacterCommands
                {
                    FireHeld = Firing,
                    LookDelta = new Vector2(Mathf.DeltaAngle(l.Yaw, yaw), pitch - l.Pitch),
                };
            }
        }

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;
            CharacterSelection.Override = "LightningRabbit";
            // Compile shader variants on first use instead of drawing the cyan placeholder (an Editor-only look).
            asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
        }

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            CharacterSelection.Override = null;
            ShaderUtil.allowAsyncCompilation = asyncShaders;
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

        void Block(Vector3 position, Vector3 scale, Color color)
        {
            GameObject go = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = Lit(color);
        }

        IEnumerator Stage()
        {
            // The sandbox's look: pale ground, slate walls, the procedural sky, the same post-processing (Bloom).
            Block(new Vector3(0f, -0.5f, 10f), new Vector3(60f, 1f, 60f), new Color(0.62f, 0.66f, 0.86f));
            Block(new Vector3(0f, 3f, 26f), new Vector3(30f, 6f, 1f), new Color(0.86f, 0.84f, 0.95f));
            Block(new Vector3(-7f, 1.5f, 14f), new Vector3(1.5f, 3f, 1.5f), new Color(0.32f, 0.28f, 0.52f));
            var sun = Track(new GameObject("Sun")).AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.95f);
            RenderSettings.ambientEquatorColor = new Color(0.55f, 0.5f, 0.68f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.27f, 0.36f);
            var volume = Track(new GameObject("Volume")).AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumePath);

            player = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath), new Vector3(0f, 0.05f, 0f), Quaternion.identity));
            look = player.GetComponent<FpsLook>();
            weapon = player.GetComponent<FpsCharacter>().Weapon;
            weapon.ChargeBurstTriggered += _ => discharges++;
            Assert.That(weapon.ChargeBurst, Is.Not.Null, "the rabbit holds the Thunderhop");
            player.GetComponent<FpsCharacter>().CommandSource = new HoldFire { Owner = this };
            foreach (Camera c in player.GetComponentsInChildren<Camera>(true))
            {
                c.enabled = false;
                c.allowHDR = true;
                c.GetUniversalAdditionalCameraData().renderPostProcessing = c.name == "PlayerCamera";
            }

            foreach (AudioListener l in player.GetComponentsInChildren<AudioListener>(true))
            {
                l.enabled = false;
            }

            review = Track(new GameObject("ReviewCamera")).AddComponent<Camera>();
            review.nearClipPlane = 0.05f;
            review.allowHDR = true;
            review.clearFlags = CameraClearFlags.Skybox;
            review.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            review.cullingMask = ~(1 << GameLayers.ViewModelIndex);
            review.enabled = false;
            yield return Frames(20);
        }

        GameObject Dummy(Vector3 position)
        {
            GameObject dummy = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(DummyPath), position, Quaternion.Euler(0f, 180f, 0f)));
            dummy.GetComponent<CoopDeck.Combat.Health>().SetMaxHealth(1000000f, true);
            Physics.SyncTransforms();
            return dummy;
        }

        // ------------------------------------------------------------------ recording

        void RecordFirstPerson()
        {
            Camera world = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "PlayerCamera");
            Camera viewModel = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            viewModel.enabled = true; // an overlay renders only while enabled (never to the screen on its own)
            RenderPipeline.SubmitRenderRequest(world, new RenderPipeline.StandardRequest { destination = videoTarget });
            viewModel.enabled = false;
            Append();
        }

        void RecordView(Vector3 eye, Vector3 at, float fov)
        {
            review.transform.position = eye;
            review.transform.LookAt(at);
            review.fieldOfView = fov;
            review.targetTexture = videoTarget;
            review.Render();
            review.targetTexture = null;
            Append();
        }

        void Append()
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = videoTarget;
            videoFrame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            videoFrame.Apply();
            RenderTexture.active = previous;
            encoder.AddFrame(videoFrame);
            if (frames % dumpEvery == 0)
            {
                File.WriteAllBytes(Path.Combine(frameFolder, $"f{frames:000}.jpg"), videoFrame.EncodeToJPG(85)); // stills for a contact sheet
            }

            frames++;
        }

        void Fire(bool on) => ((HoldFire)player.GetComponent<FpsCharacter>().CommandSource).Firing = on;

        /// <summary>One hit short of a discharge: the next frames are the last shot and the strike.</summary>
        bool AboutToDischarge => weapon.ChargeBurst.Stacks >= weapon.ChargeBurst.RequiredStacks - 1;

        /// <summary>Fires until one hit before the discharge, then records the strike from this view in quarter-speed slow motion.</summary>
        IEnumerator SlowStrike(Vector3 eye, Vector3 at, float fov, int framesAfter)
        {
            Fire(true);
            int start = discharges;
            for (int i = 0; i < 160 && !AboutToDischarge && discharges == start; i++)
            {
                yield return null; // build the charge without recording
            }

            Time.captureDeltaTime = Dt / 4f;
            for (int i = 0; i < 60 && discharges == start; i++)
            {
                RecordView(eye, at, fov);
                yield return null;
            }

            Fire(false);
            for (int i = 0; i < framesAfter; i++)
            {
                RecordView(eye, at, fov);
                yield return null;
            }

            Time.captureDeltaTime = Dt;
        }

        // ------------------------------------------------------------------ glow variants

        static readonly string[] Materials = { "M_FX_Lightning_Bolt", "M_FX_Lightning_Branch", "M_FX_Lightning_Residual", "M_FX_Lightning_Flash", "M_FX_Lightning_Ring", "M_FX_Lightning_Glow" };

        /// <summary>A stronger, wider outer glow (glow colour x1.7, falloff x0.7, soft glow x1.6) for the comparison video.</summary>
        static Dictionary<Material, (Color glow, float falloff, float intensity)> BoostGlow()
        {
            var saved = new Dictionary<Material, (Color, float, float)>();
            foreach (string name in Materials)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + name + ".mat");
                saved[m] = (m.GetColor("_GlowColor"), m.GetFloat("_GlowFalloff"), m.GetFloat("_Intensity"));
                m.SetColor("_GlowColor", m.GetColor("_GlowColor") * 1.7f);
                if (name != "M_FX_Lightning_Flash")
                {
                    m.SetFloat("_GlowFalloff", m.GetFloat("_GlowFalloff") * 0.7f);
                }

                if (name == "M_FX_Lightning_Glow")
                {
                    m.SetFloat("_Intensity", m.GetFloat("_Intensity") * 1.6f);
                }
            }

            return saved;
        }

        static void Restore(Dictionary<Material, (Color glow, float falloff, float intensity)> saved)
        {
            foreach (KeyValuePair<Material, (Color glow, float falloff, float intensity)> pair in saved)
            {
                pair.Key.SetColor("_GlowColor", pair.Value.glow);
                pair.Key.SetFloat("_GlowFalloff", pair.Value.falloff);
                pair.Key.SetFloat("_Intensity", pair.Value.intensity);
            }
        }

        [UnityTest]
        public IEnumerator Capture_Current() => Capture("Lightning_Current.mp4", false);

        [UnityTest]
        public IEnumerator Capture_GlowBoost() => Capture("Lightning_GlowBoost.mp4", true);

        /// <summary>Starts the MP4 (and the stills folder) at the current frame rate; returns the video path.</summary>
        string OpenVideo(string file)
        {
            videoTarget = Track(new RenderTexture(Width, Height, 24) { antiAliasing = 4 });
            videoFrame = Track(new Texture2D(Width, Height, TextureFormat.RGBA32, false));
            string folder = Path.GetFullPath(Path.Combine("Screenshots", "Lightning"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, file);
            frameFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + "_frames");
            if (Directory.Exists(frameFolder))
            {
                Directory.Delete(frameFolder, true);
            }

            Directory.CreateDirectory(frameFolder);
            encoder = new UnityEditor.Media.MediaEncoder(path, new UnityEditor.Media.VideoTrackAttributes
            {
                frameRate = new UnityEditor.Media.MediaRational(fps),
                width = Width,
                height = Height,
                includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.High,
            });
            return path;
        }

        /// <summary>
        /// Records at the end of the frame, after the particle systems updated: a burst spawned this frame is in the
        /// picture, as in the game (recording right after Update shows particles one frame late).
        /// </summary>
        IEnumerator RecordAtEndOfFrame()
        {
            yield return new WaitForEndOfFrame();
            RecordFirstPerson();
        }

        IEnumerator RecordFirstPersonFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return RecordAtEndOfFrame();
                yield return null;
            }
        }

        /// <summary>Holds the trigger, recording, until <paramref name="more"/> discharges happened, then keeps firing <paramref name="after"/> frames.</summary>
        IEnumerator FireThroughDischarges(int more, int after, int guard)
        {
            Fire(true);
            int start = discharges;
            for (int i = 0; i < guard && discharges < start + more; i++)
            {
                yield return RecordAtEndOfFrame();
                yield return null;
            }

            yield return RecordFirstPersonFrames(after);
            Fire(false);
        }

        [UnityTest]
        public IEnumerator Capture_Acceptance()
        {
            fps = 60;
            dumpEvery = 1;
            Time.captureDeltaTime = 1f / fps;
            yield return Stage();
            GameObject left = Dummy(new Vector3(0f, 0f, 9f));
            GameObject right = Dummy(new Vector3(3.2f, 0f, 10f));
            aimPoint = left.transform.position + Vector3.up * 1.2f;
            weapon.ChargeBurstTriggered += _ => dischargeFrames.Add(frames);
            string path = OpenVideo("Lightning_Accept.mp4");
            bool vanish = false;
            weapon.ChargeBurstTriggered += report =>
            {
                if (vanish && report.Chained.Count > 0 && report.Chained[0] != null)
                {
                    Object.Destroy(report.Chained[0].gameObject); // gone before any delayed display
                }
            };

            try
            {
                PerformanceSettings.SetVfxQuality(VfxQuality.Medium); // the 7.6 s mark: Ally X default
                yield return Frames(20);

                // ---- A. two targets, full-auto through two discharges and on (the discharge has to stand out of the fire)
                Debug.Log($"[Lightning] A two targets from frame {frames}");
                yield return FireThroughDischarges(2, 36, 600);
                yield return RecordFirstPersonFrames(30);
                yield return Frames(150); // the charge drains, the gun reloads

                // ---- B. one target
                right.SetActive(false);
                Physics.SyncTransforms();
                Debug.Log($"[Lightning] B one target from frame {frames}");
                yield return FireThroughDischarges(1, 24, 600);
                yield return RecordFirstPersonFrames(24);
                right.SetActive(true);
                Physics.SyncTransforms();
                yield return Frames(150);

                // ---- C. the chain target is removed in the discharge frame
                vanish = true;
                Debug.Log($"[Lightning] C chain target removed from frame {frames}");
                yield return FireThroughDischarges(1, 24, 600);
                yield return RecordFirstPersonFrames(24);
            }
            finally
            {
                encoder.Dispose();
                Time.captureDeltaTime = 1f / fps;
            }

            Debug.Log($"[Lightning] {path}: {frames} frames ({frames / (float)fps:0.0} s), discharges at frames {string.Join(", ", dischargeFrames)}");
            Assert.That(dischargeFrames.Count, Is.GreaterThanOrEqualTo(4));
        }

        [UnityTest]
        public IEnumerator Capture_GunAim_Rabbit() => CaptureGunAim("GunAim_Rabbit.mp4", "LightningRabbit");

        [UnityTest]
        public IEnumerator Capture_GunAim_Fox() => CaptureGunAim("GunAim_Fox.mp4", null);

        /// <summary>First person at 60 fps, full-auto at a target 9 m ahead: does the barrel line run into the crosshair with the tracers?</summary>
        IEnumerator CaptureGunAim(string file, string character)
        {
            CharacterSelection.Override = character; // null = the default character (the fox)
            fps = 60;
            dumpEvery = 1;
            Time.captureDeltaTime = 1f / fps;
            yield return Stage();
            GameObject target = Dummy(new Vector3(0f, 0f, 9f));
            aimPoint = target.transform.position + Vector3.up * 1.2f;
            string path = OpenVideo(file);
            try
            {
                PerformanceSettings.SetVfxQuality(VfxQuality.Medium);
                yield return Frames(20);
                yield return RecordFirstPersonFrames(10);
                Fire(true);
                yield return RecordFirstPersonFrames(40);
                Fire(false);
                yield return RecordFirstPersonFrames(10);
            }
            finally
            {
                encoder.Dispose();
            }

            Debug.Log($"[GunAim] {path}: {frames} frames");
        }

        IEnumerator Capture(string file, bool boost)
        {
            yield return Stage();
            Dictionary<Material, (Color, float, float)> saved = boost ? BoostGlow() : null;
            GameObject a = Dummy(new Vector3(0f, 0f, 9f));
            Dummy(new Vector3(3.2f, 0f, 10f)); // inside the 6 m chain range
            aimPoint = a.transform.position + Vector3.up * 1.2f;
            Vector3 hit = aimPoint;

            string path = OpenVideo(file);

            try
            {
                // ---- 1. first person, full speed, High: hold the trigger through two discharges
                PerformanceSettings.SetVfxQuality(VfxQuality.High);
                yield return Frames(10);
                Fire(true);
                int start = discharges;
                for (int i = 0; i < 110 && discharges < start + 2; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }

                for (int i = 0; i < 15; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }

                Fire(false);
                for (int i = 0; i < 20; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }

                yield return Frames(70); // the charge drains, the gun reloads

                // ---- 2. side view, quarter speed: the strike from above, the chain, the hit and the residual arcs
                Vector3 sideEye = hit + new Vector3(-6.5f, 0.6f, -2.5f);
                Vector3 sideAt = hit + new Vector3(1.4f, 0.8f, 0.4f);
                Debug.Log($"[Lightning] side view from frame {frames}");
                yield return SlowStrike(sideEye, sideAt, 50f, 44);
                for (int i = 0; i < 8; i++)
                {
                    RecordView(sideEye, sideAt, 50f);
                    yield return null;
                }

                yield return Frames(50);

                // ---- 3. close look at the hit, quarter speed
                Vector3 nearEye = hit + new Vector3(-2.2f, 0.4f, -2.6f);
                Debug.Log($"[Lightning] close view from frame {frames}");
                yield return SlowStrike(nearEye, hit + Vector3.up * 0.6f, 55f, 40);
                yield return Frames(50);

                // ---- 4. first person at the Ally X default quality (Medium), full speed
                Debug.Log($"[Lightning] first person (Medium) from frame {frames}");
                PerformanceSettings.SetVfxQuality(VfxQuality.Medium);
                Fire(true);
                start = discharges;
                for (int i = 0; i < 70 && discharges < start + 1; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }

                for (int i = 0; i < 20; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }

                Fire(false);
                for (int i = 0; i < 10; i++)
                {
                    RecordFirstPerson();
                    yield return null;
                }
            }
            finally
            {
                encoder.Dispose();
                Time.captureDeltaTime = Dt;
                if (saved != null)
                {
                    Restore(saved);
                }
            }

            Debug.Log($"[Lightning] {path}: {frames} frames ({frames / (float)fps:0.0} s), {discharges} discharges");
            Assert.That(discharges, Is.GreaterThanOrEqualTo(4));
        }
    }
}
