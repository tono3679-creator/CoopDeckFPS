using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Characters;
using CoopDeck.Core;
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
    /// Review videos of the shooting and skill effects, run on demand (Screenshots/Effects/&lt;tag&gt;_*.mp4, tag from the
    /// EditorPrefs key <see cref="TagKey"/>, default "Current"). Same stage, distances and quality (Medium, the Ally X
    /// default) every time so before/after compare 1:1. 1920x1080, 30 fps, one game step per frame (real speed).
    /// <list type="bullet">
    /// <item>Weapons (fox IGNIS-27, rabbit VOLT-9): first person at a wall 14 m away, then at a dummy 9 m away, then a
    /// side view of the same burst, then the impact up close at quarter speed.</item>
    /// <item>Skills: each starter card cast at a group of dummies 10 m ahead, first person then a raised side view.</item>
    /// </list>
    /// </summary>
    [Explicit("writes review videos; run on demand")]
    [Category("Capture")]
    public class EffectsCaptureTests
    {
        public const string TagKey = "CoopDeck.EffectsCaptureTag";
        const float Dt = 1f / 30f;
        const int Width = 1920; // the Ally X screen (1280x720 until 2026-11-09)
        const int Height = 1080;
        const int Fps = 30;
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const string DummyPath = "Assets/_Game/Prefabs/Enemies/PF_TargetDummy.prefab";
        const string VolumePath = "Assets/_Game/Settings/Rendering/VP_Sandbox.asset";
        const string SkyPath = "Assets/_Game/Art/Environments/Materials/M_Sky_Procedural.mat";
        const string CardFolder = "Assets/_Game/ScriptableObjects/Cards/";

        /// <summary>The starter deck's cards in a fixed order (fire, ice, lightning, gravity, support).</summary>
        public static readonly string[] Cards =
        {
            "CD_Fireball", "CD_FlameVortex", "CD_Incendiary", "CD_MeteorStrike",
            "CD_IceLance", "CD_FrostNova",
            "CD_ChainLightning", "CD_ThunderCharge",
            "CD_GravitySpike", "CD_GravityCollapse",
            "CD_Shield", "CD_AreaHeal", "CD_MarkTarget",
        };

        readonly List<Object> spawned = new List<Object>();
        GameObject player;
        FpsLook look;
        Camera review;
        RenderTexture videoTarget;
        Texture2D videoFrame;
        UnityEditor.Media.MediaEncoder encoder;
        int frames;
        string frameFolder;
        Vector3 aimPoint;
        bool asyncShaders;

        sealed class Aim : ICharacterCommandSource
        {
            public EffectsCaptureTests Owner;
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

        static string Tag => EditorPrefs.GetString(TagKey, "Current");

        [SetUp]
        public void SetUp()
        {
            Time.captureDeltaTime = Dt;
            asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            PerformanceSettings.SetVfxQuality(VfxQuality.Medium);
        }

        [TearDown]
        public void TearDown()
        {
            encoder?.Dispose();
            encoder = null;
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

        IEnumerator Stage(string character)
        {
            CharacterSelection.Override = character;
            Block(new Vector3(0f, -0.5f, 10f), new Vector3(60f, 1f, 60f), new Color(0.62f, 0.66f, 0.86f));
            Block(new Vector3(0f, 3f, 20f), new Vector3(30f, 6f, 1f), new Color(0.86f, 0.84f, 0.95f));
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
            player.GetComponent<FpsCharacter>().CommandSource = new Aim { Owner = this };
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

        void OpenVideo(string file)
        {
            videoTarget = Track(new RenderTexture(Width, Height, 24) { antiAliasing = 4 });
            videoFrame = Track(new Texture2D(Width, Height, TextureFormat.RGBA32, false));
            string folder = Path.GetFullPath(Path.Combine("Screenshots", "Effects"));
            Directory.CreateDirectory(folder);
            frameFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(file) + "_frames");
            if (Directory.Exists(frameFolder))
            {
                Directory.Delete(frameFolder, true);
            }

            Directory.CreateDirectory(frameFolder);
            frames = 0;
            encoder = new UnityEditor.Media.MediaEncoder(Path.Combine(folder, file), new UnityEditor.Media.VideoTrackAttributes
            {
                frameRate = new UnityEditor.Media.MediaRational(Fps),
                width = Width,
                height = Height,
                includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.High,
            });
        }

        void CloseVideo()
        {
            encoder.Dispose();
            encoder = null;
        }

        void Append(string label)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = videoTarget;
            videoFrame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            videoFrame.Apply();
            RenderTexture.active = previous;
            encoder.AddFrame(videoFrame);
            if (frames % 2 == 0)
            {
                File.WriteAllBytes(Path.Combine(frameFolder, $"f{frames:0000}_{label}.jpg"), videoFrame.EncodeToJPG(85));
            }

            frames++;
        }

        void RenderFirstPerson()
        {
            Camera world = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "PlayerCamera");
            Camera viewModel = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            viewModel.enabled = true;
            RenderPipeline.SubmitRenderRequest(world, new RenderPipeline.StandardRequest { destination = videoTarget });
            viewModel.enabled = false;
        }

        void RenderView(Vector3 eye, Vector3 at, float fov)
        {
            review.transform.position = eye;
            review.transform.LookAt(at);
            review.fieldOfView = fov;
            review.targetTexture = videoTarget;
            review.Render();
            review.targetTexture = null;
        }

        /// <summary>Records <paramref name="count"/> frames at the end of each frame (after particles updated).</summary>
        IEnumerator Record(int count, string label, System.Func<bool> view = null, Vector3 eye = default, Vector3 at = default, float fov = 50f)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForEndOfFrame();
                if (eye == default)
                {
                    RenderFirstPerson();
                }
                else
                {
                    RenderView(eye, at, fov);
                }

                Append(label);
                yield return null;
            }
        }

        void Fire(bool on) => ((Aim)player.GetComponent<FpsCharacter>().CommandSource).Firing = on;

        IEnumerator SettleAim(Vector3 point)
        {
            aimPoint = point;
            yield return Frames(6);
        }

        // ------------------------------------------------------------------ weapons

        [UnityTest]
        public IEnumerator Capture_Weapon_Fox() => CaptureWeapon(null, "Fox");

        [UnityTest]
        public IEnumerator Capture_Weapon_Rabbit() => CaptureWeapon("LightningRabbit", "Rabbit");

        IEnumerator CaptureWeapon(string character, string name)
        {
            yield return Stage(character);
            Vector3 dummyAt = new Vector3(0.6f, 0.05f, 9f);
            Dummy(dummyAt);
            Vector3 chest = dummyAt + Vector3.up * 1.15f;
            Vector3 wall = new Vector3(-2.2f, 1.4f, 19.5f);
            OpenVideo($"{Tag}_Weapon_{name}.mp4");

            yield return SettleAim(wall);
            Fire(true);
            yield return Record(30, "wall");
            Fire(false);
            yield return Record(8, "wall");

            yield return SettleAim(chest);
            Fire(true);
            yield return Record(36, "enemy");
            Fire(false);
            yield return Record(10, "enemy");

            Vector3 side = new Vector3(3.6f, 1.7f, 2.2f);
            yield return Frames(20);
            Fire(true);
            yield return Record(30, "side", null, side, new Vector3(0f, 1.3f, 5f), 55f);
            Fire(false);
            yield return Record(10, "side", null, side, new Vector3(0f, 1.3f, 5f), 55f);

            // The hit up close, quarter speed.
            yield return Frames(20);
            Time.captureDeltaTime = Dt / 4f;
            Fire(true);
            yield return Record(48, "closeup", null, chest + new Vector3(1.6f, 0.4f, -1.9f), chest, 45f);
            Fire(false);
            yield return Record(24, "closeup", null, chest + new Vector3(1.6f, 0.4f, -1.9f), chest, 45f);
            Time.captureDeltaTime = Dt;
            CloseVideo();
        }

        // ------------------------------------------------------------------ skills

        [UnityTest]
        public IEnumerator Capture_Skills()
        {
            yield return Stage(null);
            var caster = player.GetComponent<SkillCaster>();
            Vector3[] group = { new Vector3(0f, 0.05f, 10f), new Vector3(-1.8f, 0.05f, 11.2f), new Vector3(1.9f, 0.05f, 11f) };
            var dummies = group.Select(Dummy).ToArray();

            Vector3 target = group[0] + Vector3.up * 1.1f;
            OpenVideo($"{Tag}_Skills.mp4");
            foreach (string id in Cards)
            {
                var card = AssetDatabase.LoadAssetAtPath<CardDefinition>(CardFolder + id + ".asset");
                Assert.That(card, Is.Not.Null, id);
                bool self = card.targeting == CardTargeting.Self || card.targeting == CardTargeting.AreaAroundSelf;
                for (int d = 0; d < dummies.Length; d++)
                {
                    dummies[d].transform.position = group[d]; // pulls (gravity cards) moved them: same stage for every card
                }

                Physics.SyncTransforms();
                yield return SettleAim(target);

                caster.CastCard(card);
                yield return Record(id == "CD_MeteorStrike" ? 60 : 45, id);
                yield return Frames(30);

                // The same cast from a raised side view (self-centred cards: around the player).
                Vector3 focus = self ? player.transform.position + Vector3.up * 1f : (player.transform.position + target) * 0.5f;
                Vector3 eye = self ? new Vector3(4.5f, 2.6f, 3.5f) : new Vector3(7.5f, 4f, 3.5f);
                caster.CastCard(card);
                yield return Record(id == "CD_MeteorStrike" ? 60 : 45, id + "_side", null, eye, focus, self ? 50f : 58f);
                yield return Frames(40);
            }

            CloseVideo();
        }
    }
}
