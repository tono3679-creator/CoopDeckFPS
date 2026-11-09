#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Core;
using CoopDeck.NPC;
using CoopDeck.Player;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Review captures of the fox's Fire Rifle (not part of the normal run). The sandbox scene is loaded, the player
    /// stands still and time is frozen, then the player's real camera stack (world camera + view-model overlay) is
    /// rendered at 1920x1080 under several render setups (quality tier, render scale, MSAA, FXAA / SMAA, which
    /// camera runs post-processing), with the same frame each time. Third-person shots show the fox holding it.
    /// Writes Screenshots/WeaponLook/&lt;tag&gt;_&lt;setup&gt;_{fp,muzzle,tp_near,tp_mid}.png and Logs/weapon_look_&lt;tag&gt;.json
    /// (GPU-synced render time per setup). WEAPON_LOOK_TAG (default "now") and WEAPON_LOOK_SETUPS (comma-separated setup
    /// labels, default all) and WEAPON_LOOK_CHARACTER (default FoxGunner; LightningRabbit) come from the environment or,
    /// for a live Editor, Logs/weapon_look_tag.txt / _setups.txt / _character.txt.
    ///   Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter WeaponLookCaptureTests
    /// </summary>
    [Explicit("writes review images; run on demand")]
    [Category("Capture")]
    public class WeaponLookCaptureTests
    {
        const string ScenePath = "Assets/_Game/Scenes/Sandbox_Phase1.unity";
        const int W = 1920;
        const int H = 1080;
        const int TimedFrames = 40;

        sealed class Setup
        {
            public string Label;
            public string Quality;
            public float RenderScale;
            public int Msaa;
            public AntialiasingMode Aa;
            public bool PostOnViewModel; // post-processing (and so SMAA, tonemapping, bloom) on the last camera
            public UpscalingFilterSelection Upscaling;
            public bool ThirdPerson;
            public bool Muzzle; // also a still of the muzzle flash (drawn by the view-model camera)
        }

        static readonly Setup[] Setups =
        {
            new Setup { Label = "HH_now", Quality = "Handheld", RenderScale = 0.833f, Msaa = 1, Aa = AntialiasingMode.FastApproximateAntialiasing, Upscaling = UpscalingFilterSelection.FSR, ThirdPerson = true, Muzzle = true },
            new Setup { Label = "HH_FSR_SMAA_vmPost", Quality = "Handheld", RenderScale = 0.833f, Msaa = 1, Aa = AntialiasingMode.SubpixelMorphologicalAntiAliasing, PostOnViewModel = true, Upscaling = UpscalingFilterSelection.FSR },
            new Setup { Label = "HH_RS1_SMAA_vmPost", Quality = "Handheld", RenderScale = 1f, Msaa = 1, Aa = AntialiasingMode.SubpixelMorphologicalAntiAliasing, PostOnViewModel = true, Upscaling = UpscalingFilterSelection.Auto, ThirdPerson = true, Muzzle = true },
            new Setup { Label = "HH_RS1_MSAA2_SMAA_vmPost", Quality = "Handheld", RenderScale = 1f, Msaa = 2, Aa = AntialiasingMode.SubpixelMorphologicalAntiAliasing, PostOnViewModel = true, Upscaling = UpscalingFilterSelection.Auto },
            new Setup { Label = "High_now", Quality = "High", RenderScale = 1f, Msaa = 4, Aa = AntialiasingMode.FastApproximateAntialiasing, Upscaling = UpscalingFilterSelection.Auto, ThirdPerson = true, Muzzle = true },
            new Setup { Label = "High_SMAA_basePost", Quality = "High", RenderScale = 1f, Msaa = 4, Aa = AntialiasingMode.SubpixelMorphologicalAntiAliasing, Upscaling = UpscalingFilterSelection.Auto },
            new Setup { Label = "High_SMAA_vmPost", Quality = "High", RenderScale = 1f, Msaa = 4, Aa = AntialiasingMode.SubpixelMorphologicalAntiAliasing, PostOnViewModel = true, Upscaling = UpscalingFilterSelection.Auto, ThirdPerson = true, Muzzle = true },
            new Setup { Label = "High_RS125_SMAA_vmPost", Quality = "High", RenderScale = 1.25f, Msaa = 4, Aa = AntialiasingMode.SubpixelMorphologicalAntiAliasing, PostOnViewModel = true, Upscaling = UpscalingFilterSelection.Auto, ThirdPerson = true },
            new Setup { Label = "High_noMSAA_SMAA_vmPost", Quality = "High", RenderScale = 1f, Msaa = 1, Aa = AntialiasingMode.SubpixelMorphologicalAntiAliasing, PostOnViewModel = true, Upscaling = UpscalingFilterSelection.Auto },
        };

        [Serializable]
        class Result
        {
            public string setup;
            public string quality;
            public float renderScale;
            public int msaa;
            public string antialiasing;
            public bool postOnViewModel;
            public string upscaling;
            public float medianMs;
            public float p90Ms;
            public string fp;
        }

        [Serializable]
        class Report
        {
            public string tag;
            public string gpu;
            public string unityVersion;
            public List<Result> results = new List<Result>();
        }

        sealed class Still : ICharacterCommandSource
        {
            public CharacterCommands GetCommands(float deltaTime) => default;
        }

        readonly List<Object> spawned = new List<Object>();
        readonly Dictionary<int, RenderPipelineAsset> originalPipelines = new Dictionary<int, RenderPipelineAsset>();
        int originalLevel;
        float originalShake;

        [SetUp]
        public void SetUp()
        {
            originalLevel = QualitySettings.GetQualityLevel();
            originalShake = PerformanceSettings.ScreenShakeScale;
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                originalPipelines[i] = QualitySettings.GetRenderPipelineAssetAt(i);
            }

            Time.captureDeltaTime = 1f / 60f;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0f;
            PerformanceSettings.ScreenShakeScale = originalShake;
            // the clones below are assigned to the current level only; put every level's own asset back
            foreach (var pair in originalPipelines)
            {
                QualitySettings.SetQualityLevel(pair.Key, false);
                QualitySettings.renderPipeline = pair.Value;
            }

            QualitySettings.SetQualityLevel(originalLevel, true);
            foreach (Object o in spawned.Where(o => o != null))
            {
                Object.Destroy(o);
            }

            spawned.Clear();
        }

        static IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
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

        static void Save(Texture2D image, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, image.EncodeToPNG());
        }

        /// <summary>Renders `camera` (a base camera: its overlay stack renders with it) into a new target.</summary>
        static Texture2D Shoot(Camera camera, int msaa, out RenderTexture target)
        {
            target = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = msaa };
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            Texture2D image = Read(target);
            return image;
        }

        static (float median, float p90) Measure(Camera camera, RenderTexture target)
        {
            var probe = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            var times = new List<double>();
            for (int i = 0; i < TimedFrames + 5; i++)
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                camera.Render();
                RenderTexture.active = target;
                probe.ReadPixels(new Rect(0, 0, 1, 1), 0, 0); // waits for the GPU
                RenderTexture.active = null;
                watch.Stop();
                if (i >= 5)
                {
                    times.Add(watch.Elapsed.TotalMilliseconds);
                }
            }

            Object.Destroy(probe);
            times.Sort();
            return ((float)times[times.Count / 2], (float)times[times.Count * 9 / 10]);
        }

        /// <summary>
        /// CharacterSelection.Override, looked up by name: the capture also builds where the character module
        /// (CoopDeck.Characters) is absent, and then the player keeps its built-in fox.
        /// </summary>
        static void SelectCharacter(string id)
        {
            Type selection = Type.GetType("CoopDeck.Characters.CharacterSelection, CoopDeck.Characters");
            selection?.GetProperty("Override")?.SetValue(null, id);
        }

        /// <summary>The environment variable, else the first line of the file (a live Editor has no new variables).</summary>
        static string Setting(string variable, string file)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(value) && File.Exists(file))
            {
                value = File.ReadAllLines(file).FirstOrDefault()?.Trim();
            }

            return value;
        }

        [UnityTest]
        public IEnumerator Capture_FireRifle_Look()
        {
            string tag = Setting("WEAPON_LOOK_TAG", "Logs/weapon_look_tag.txt");
            tag = string.IsNullOrEmpty(tag) ? "now" : tag;
            string filter = Setting("WEAPON_LOOK_SETUPS", "Logs/weapon_look_setups.txt");
            HashSet<string> only = string.IsNullOrEmpty(filter) ? null : new HashSet<string>(filter.Split(','));

            string character = Setting("WEAPON_LOOK_CHARACTER", "Logs/weapon_look_character.txt");
            SelectCharacter(string.IsNullOrEmpty(character) ? "FoxGunner" : character);
            PerformanceSettings.ScreenShakeScale = 0f;
            yield return EditorSceneManager.LoadSceneAsyncInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(5);

            FpsCharacter player = FpsCharacter.FindLocalHuman();
            Assert.That(player, Is.Not.Null, "no local player in the sandbox scene");
            player.CommandSource = new Still();
            foreach (NpcController npc in Object.FindObjectsByType<NpcController>(FindObjectsSortMode.None))
            {
                npc.gameObject.SetActive(false); // keeps the frame identical between setups
            }

            player.Look.SetRotation(player.Look.Yaw, 4f);
            yield return Frames(150); // idle pose, view-model sway and the arms settle (2.5 s of game time)
            Time.timeScale = 0f;
            yield return Frames(3);

            Camera world = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "PlayerCamera");
            Camera viewModel = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            UniversalAdditionalCameraData worldData = world.GetUniversalAdditionalCameraData();
            UniversalAdditionalCameraData viewModelData = viewModel.GetUniversalAdditionalCameraData();
            world.enabled = false; // rendered by hand below; the overlay stays enabled so the stack draws it
            var weapon = player.GetComponent<HitscanWeapon>();

            var tpCamera = new GameObject("WeaponLookThirdPerson").AddComponent<Camera>();
            spawned.Add(tpCamera.gameObject);
            tpCamera.enabled = false;
            tpCamera.nearClipPlane = 0.05f;
            tpCamera.cullingMask = ~(1 << GameLayers.ViewModelIndex);
            UniversalAdditionalCameraData tpData = tpCamera.GetUniversalAdditionalCameraData();
            tpData.renderPostProcessing = true;
            Transform body = player.transform.Find("BodyVisual");
            Renderer[] bodyRenderers = body.GetComponentsInChildren<Renderer>(true);
            // the body's weapon: HeldRifle (fox), HeldSMG (rabbit)
            Transform heldRifle = body.GetComponentsInChildren<Transform>(true).First(t => t.name.StartsWith("Held"));

            var report = new Report { tag = tag, gpu = SystemInfo.graphicsDeviceName, unityVersion = Application.unityVersion };
            string folder = Path.Combine("Screenshots", "WeaponLook");
            foreach (Setup setup in Setups)
            {
                if (only != null && !only.Contains(setup.Label))
                {
                    continue;
                }

                int level = Array.IndexOf(QualitySettings.names, setup.Quality);
                Assert.That(level, Is.GreaterThanOrEqualTo(0), setup.Quality);
                QualitySettings.SetQualityLevel(level, true);
                var clone = Object.Instantiate((UniversalRenderPipelineAsset)originalPipelines[level]);
                spawned.Add(clone);
                clone.renderScale = setup.RenderScale;
                clone.msaaSampleCount = setup.Msaa;
                clone.upscalingFilter = setup.Upscaling;
                QualitySettings.renderPipeline = clone;
                worldData.antialiasing = setup.Aa;
                worldData.antialiasingQuality = AntialiasingQuality.High;
                worldData.renderPostProcessing = !setup.PostOnViewModel;
                viewModelData.renderPostProcessing = setup.PostOnViewModel;
                yield return Frames(2);

                Texture2D fp = Shoot(world, setup.Msaa, out RenderTexture target);
                string fpPath = Path.Combine(folder, $"{tag}_{setup.Label}_fp.png");
                Save(fp, fpPath);
                Object.Destroy(fp);
                (float median, float p90) = Measure(world, target);
                world.targetTexture = null;
                target.Release();
                Object.Destroy(target);

                if (setup.Muzzle && weapon.Definition != null && weapon.Definition.muzzleFlashPrefab != null && weapon.Muzzle != null)
                {
                    // the weapon's own muzzle flash on the view-model layer, its particles advanced 30 ms (time is frozen)
                    GameObject flash = Object.Instantiate(weapon.Definition.muzzleFlashPrefab, weapon.Muzzle.position, weapon.Muzzle.rotation, weapon.Muzzle);
                    foreach (Transform t in flash.GetComponentsInChildren<Transform>(true))
                    {
                        t.gameObject.layer = GameLayers.ViewModelIndex;
                    }

                    foreach (ParticleSystem ps in flash.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        ps.Simulate(0.03f, false, true);
                    }

                    Texture2D shot = Shoot(world, setup.Msaa, out RenderTexture muzzleTarget);
                    Save(shot, Path.Combine(folder, $"{tag}_{setup.Label}_muzzle.png"));
                    Object.Destroy(shot);
                    world.targetTexture = null;
                    muzzleTarget.Release();
                    Object.Destroy(muzzleTarget);
                    Object.DestroyImmediate(flash);
                }

                if (setup.ThirdPerson)
                {
                    foreach (Renderer r in bodyRenderers)
                    {
                        r.shadowCastingMode = ShadowCastingMode.On;
                    }

                    tpData.antialiasing = setup.Aa;
                    tpData.antialiasingQuality = AntialiasingQuality.High;
                    var shots = new (string name, Vector3 eye, float fov)[]
                    {
                        ("tp_near", new Vector3(0.75f, 1.45f, 1.05f), 34f),
                        ("tp_mid", new Vector3(1.4f, 1.6f, 3.4f), 50f),
                    };
                    foreach (var shot in shots)
                    {
                        tpCamera.fieldOfView = shot.fov;
                        tpCamera.transform.position = player.transform.TransformPoint(shot.eye);
                        tpCamera.transform.LookAt(shot.name == "tp_near" ? heldRifle.position + heldRifle.forward * 0.15f
                            : player.transform.position + Vector3.up * 1.0f);
                        Texture2D tp = Shoot(tpCamera, setup.Msaa, out RenderTexture tpTarget);
                        Save(tp, Path.Combine(folder, $"{tag}_{setup.Label}_{shot.name}.png"));
                        Object.Destroy(tp);
                        tpCamera.targetTexture = null;
                        tpTarget.Release();
                        Object.Destroy(tpTarget);
                    }

                    foreach (Renderer r in bodyRenderers)
                    {
                        r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
                    }
                }

                var asset = (UniversalRenderPipelineAsset)QualitySettings.renderPipeline;
                report.results.Add(new Result
                {
                    setup = setup.Label, quality = QualitySettings.names[QualitySettings.GetQualityLevel()],
                    renderScale = asset.renderScale, msaa = asset.msaaSampleCount, antialiasing = setup.Aa.ToString(),
                    postOnViewModel = setup.PostOnViewModel, upscaling = asset.upscalingFilter.ToString(),
                    medianMs = median, p90Ms = p90, fp = fpPath,
                });
                Debug.Log($"[WeaponLook] {tag} {setup.Label}: render {median:0.00} ms (p90 {p90:0.00}) -> {fpPath}");
            }

            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", $"weapon_look_{tag}.json"), JsonUtility.ToJson(report, true));
            Assert.That(report.results, Is.Not.Empty, "no setup matched WEAPON_LOOK_SETUPS");
        }
    }
}
#endif
