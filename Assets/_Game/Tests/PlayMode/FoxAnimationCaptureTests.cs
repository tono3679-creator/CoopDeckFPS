#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Animation;
using CoopDeck.Core;
using CoopDeck.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Review renders of the Fox Gunner body in play (not part of the normal run): the generated PF_Player is
    /// driven through its real systems (move / sprint / jump / dash / fire / reload / cards) and the driver API for
    /// the rest, and a third-person camera writes contact sheets to Screenshots/FoxAnim_*.png.
    ///   Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode -testFilter FoxAnimationCaptureTests
    /// </summary>
    [Explicit("writes review images; run on demand")]
    [Category("Capture")]
    public class FoxAnimationCaptureTests
    {
        const float Dt = 1f / 60f;
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const int W = 400;
        const int H = 460;
        readonly List<Object> spawned = new List<Object>();

        sealed class ScriptedCommands : ICharacterCommandSource
        {
            public CharacterCommands Next;
            public CharacterCommands GetCommands(float deltaTime)
            {
                CharacterCommands c = Next;
                // one-shot presses last a single frame
                Next.JumpPressed = false;
                Next.DashPressed = false;
                Next.FirePressed = false;
                Next.ReloadPressed = false;
                Next.SkillPressedMask = 0;
                return c;
            }
        }

        [SetUp]
        public void SetUp() => Time.captureDeltaTime = Dt;

        [TearDown]
        public void TearDown()
        {
            Time.captureDeltaTime = 0f;
            foreach (Object o in spawned.Where(o => o != null))
            {
                Object.Destroy(o);
            }

            spawned.Clear();
        }

        Camera camera;
        RenderTexture target;
        GameObject player;
        readonly List<(string label, Texture2D image)> shots = new List<(string, Texture2D)>();

        void Shoot(string label, Vector3 localEye, Vector3 localLook)
        {
            Transform t = player.transform;
            camera.transform.position = t.TransformPoint(localEye);
            camera.transform.LookAt(t.TransformPoint(localLook));
            camera.targetTexture = target;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = target;
            var image = new Texture2D(W, H, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            shots.Add((label, image));
        }

        void Front(string label) => Shoot(label + " (front 3/4)", new Vector3(-1.7f, 1.35f, 2.6f), new Vector3(0f, 0.85f, 0f));

        void Side(string label) => Shoot(label + " (right side)", new Vector3(-3.0f, 1.0f, 0.1f), new Vector3(0f, 0.8f, 0.1f));

        void Back(string label) => Shoot(label + " (back 3/4)", new Vector3(1.6f, 1.5f, -2.4f), new Vector3(0f, 0.85f, 0f));

        void LogState(string label)
        {
            Animator a = player.GetComponent<FpsCharacter>().BodyAnimator.Animator;
            var motor = player.GetComponent<FpsMotor>();
            string states = string.Join(", ", Enumerable.Range(0, a.layerCount).Select(l => a.GetLayerName(l) + "=" + a.GetCurrentAnimatorStateInfo(l).shortNameHash + (a.IsInTransition(l) ? "(T)" : "")));
            Debug.Log($"[FoxAnim] {label}: speed {a.GetFloat("Speed"):0.00} moveX {a.GetFloat("MoveX"):0.00} moveY {a.GetFloat("MoveY"):0.00} sprint {motor.IsSprinting} dashing {motor.IsDashing} vel {motor.Velocity} base idle={a.GetCurrentAnimatorStateInfo(0).IsName("Idle")} loco={a.GetCurrentAnimatorStateInfo(0).IsName("Locomotion")} dash={a.GetCurrentAnimatorStateInfo(0).IsName("Dash")} next={(a.IsInTransition(0) ? (a.GetNextAnimatorStateInfo(0).IsName("Dash") ? "Dash" : a.GetNextAnimatorStateInfo(0).IsName("Locomotion") ? "Locomotion" : a.GetNextAnimatorStateInfo(0).IsName("Idle") ? "Idle" : "other") : "-")} | {states}");
        }

        IEnumerator Frames(int n)
        {
            for (int i = 0; i < n; i++)
            {
                yield return null;
            }
        }

        void Save(string name, int cols)
        {
            int rows = (shots.Count + cols - 1) / cols;
            int w = shots[0].image.width;
            int h = shots[0].image.height;
            var sheet = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
            Color[] fill = Enumerable.Repeat(new Color(0.12f, 0.12f, 0.14f), w * cols * h * rows).ToArray();
            sheet.SetPixels(fill);
            for (int i = 0; i < shots.Count; i++)
            {
                int cx = i % cols;
                int cy = rows - 1 - i / cols;
                sheet.SetPixels(cx * w, cy * h, w, h, shots[i].image.GetPixels());
            }

            sheet.Apply();
            Directory.CreateDirectory("Screenshots");
            string path = Path.Combine("Screenshots", name + ".png");
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Debug.Log($"[FoxAnim] {path}: " + string.Join(" | ", shots.Select((s, i) => $"{i + 1}:{s.label}")));
            foreach (var s in shots)
            {
                Object.Destroy(s.image);
            }

            shots.Clear();
            Object.Destroy(sheet);
        }

        IEnumerator Spawn(ScriptedCommands commands)
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.transform.localScale = new Vector3(40f, 1f, 40f);
            var floorMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            floorMat.SetColor("_BaseColor", new Color(0.33f, 0.34f, 0.38f));
            floor.GetComponent<Renderer>().sharedMaterial = floorMat;
            spawned.Add(floor);
            spawned.Add(floorMat);
            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, 150f, 0f);
            spawned.Add(sun.gameObject);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.55f);
            Physics.SyncTransforms();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            Assert.That(prefab, Is.Not.Null, PlayerPath + " missing: run the bootstrap");
            player = Object.Instantiate(prefab, new Vector3(0f, 0.05f, 0f), Quaternion.identity);
            spawned.Add(player);
            player.GetComponent<FpsCharacter>().CommandSource = commands;
            foreach (Camera c in player.GetComponentsInChildren<Camera>(true))
            {
                c.enabled = false;
            }

            foreach (AudioListener l in player.GetComponentsInChildren<AudioListener>(true))
            {
                l.enabled = false;
            }

            // the local player's body only casts shadows; show it for the review camera
            foreach (Renderer r in player.transform.Find("BodyVisual").GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.On;
            }

            camera = new GameObject("CaptureCamera").AddComponent<Camera>();
            camera.fieldOfView = 34f;
            camera.nearClipPlane = 0.05f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.2f, 0.21f, 0.25f);
            camera.cullingMask = ~(1 << GameLayers.ViewModelIndex);
            spawned.Add(camera.gameObject);
            target = new RenderTexture(W, H, 24) { antiAliasing = 4 };
            spawned.Add(target);
            yield return Frames(40);
        }

        [UnityTest]
        public IEnumerator Capture_FoxGunner_Motions()
        {
            var commands = new ScriptedCommands();
            yield return Spawn(commands);
            var character = player.GetComponent<FpsCharacter>();
            CharacterAnimatorDriver driver = character.BodyAnimator;
            Assert.That(driver, Is.Not.Null);

            // ---- locomotion
            Front("Combat_Idle");
            Side("Combat_Idle");
            commands.Next.Move = new Vector2(0f, 0.25f);
            yield return Frames(40);
            Side("Walk_Forward ~1.6 m/s");
            commands.Next.Move = new Vector2(0f, 1f);
            yield return Frames(31);
            Side("Run_Forward 6.5 m/s");
            Front("Run_Forward 6.5 m/s");
            commands.Next.Move = new Vector2(1f, 0f);
            yield return Frames(33);
            Front("Run_Right");
            commands.Next.Move = new Vector2(-1f, 0f);
            yield return Frames(33);
            Front("Run_Left");
            commands.Next.Move = new Vector2(0f, -1f);
            yield return Frames(33);
            Front("Run_Backward");
            commands.Next.Move = new Vector2(0.7f, 0.7f);
            yield return Frames(33);
            Front("Run diagonal F-R");
            commands.Next.Move = new Vector2(0f, 1f);
            commands.Next.SprintHeld = true;
            yield return Frames(35);
            LogState("sprint");
            Side("Sprint 9.5 m/s");
            commands.Next.SprintHeld = false;
            commands.Next.Move = Vector2.zero;
            yield return Frames(40);
            commands.Next.JumpPressed = true;
            yield return Frames(6);
            Side("Jump_Start/Loop (rising)");
            yield return Frames(26);
            Side("Fall");
            yield return Frames(30);
            commands.Next.Move = new Vector2(1f, 0f);
            commands.Next.DashPressed = true;
            for (int k = 0; k < 6; k++)
            {
                yield return null;
                LogState("dash+" + (k + 1));
            }

            Front("Dash_Right");
            commands.Next.Move = Vector2.zero;
            yield return Frames(40);
            Save("FoxAnim_Locomotion", 4);

            // ---- rifle and cards through the real systems
            commands.Next.FireHeld = true;
            commands.Next.FirePressed = true;
            yield return Frames(7);
            Front("Rifle_Fire (holding the trigger)");
            commands.Next.FireHeld = false;
            yield return Frames(20);
            commands.Next.ReloadPressed = true;
            yield return Frames(30);
            Front("Rifle_Reload 0.5 s");
            yield return Frames(30);
            Side("Rifle_Reload 1.0 s");
            yield return Frames(60);
            for (int slot = 0; slot < 3; slot++)
            {
                commands.Next.SkillPressedMask = 1 << slot;
                yield return Frames(9);
                Front($"card slot {slot + 1} cast");
                yield return Frames(50);
            }

            // ---- the rest through the driver
            var motions = new[] { SkillMotion.QuickCast, SkillMotion.Charged, SkillMotion.AreaCast, SkillMotion.SelfBuff, SkillMotion.Ultimate };
            var at = new[] { 9, 24, 18, 26, 40 };
            for (int i = 0; i < motions.Length; i++)
            {
                driver.Skill(motions[i]);
                yield return Frames(at[i]);
                Front("Skill " + motions[i]);
                yield return Frames(70);
            }

            Save("FoxAnim_Weapon_Skills", 4);

            driver.Hit(false);
            yield return Frames(3);
            Front("Hit_Light");
            yield return Frames(30);
            driver.Hit(true);
            yield return Frames(6);
            Front("Hit_Heavy");
            yield return Frames(40);
            driver.Knockback();
            yield return Frames(14);
            Side("Knockback airborne");
            yield return Frames(80);
            foreach (var g in new[] { (InteractionMotion.Pickup, 17), (InteractionMotion.Interact, 22), (InteractionMotion.UseItem, 26), (InteractionMotion.Emote, 50) })
            {
                driver.Interact(g.Item1);
                yield return Frames(g.Item2);
                Front(g.Item1.ToString());
                yield return Frames(90);
            }

            driver.SetReviving(true);
            yield return Frames(40);
            Front("Revive_Ally");
            driver.SetReviving(false);
            yield return Frames(30);
            driver.SetDowned(true);
            yield return Frames(30);
            Side("Downed_Start");
            yield return Frames(90);
            Front("Downed_Loop");
            Back("Downed_Loop");
            driver.Revive();
            yield return Frames(30);
            Side("Revived");
            yield return Frames(70);
            driver.SetDowned(true);
            yield return Frames(120);
            driver.SetDead(true);
            yield return Frames(110);
            Front("Death");
            Side("Death");
            driver.Respawned();
            yield return Frames(20);
            Front("Rifle_Equip (respawn)");
            Save("FoxAnim_Reactions", 4);
        }

        void LogFirstPersonHands(string label)
        {
            FirstPersonArms arms = player.GetComponentInChildren<FirstPersonArms>(true);
            if (arms == null)
            {
                return;
            }

            float left = Vector3.Distance(arms.Hand(true).position, arms.GoalPosition(true));
            float right = Vector3.Distance(arms.Hand(false).position, arms.GoalPosition(false));
            Camera vm = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            SkinnedMeshRenderer skin = arms.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Debug.Log($"[FoxAnim] {label}: FP hand error left {left * 100:0.0} cm, right {right * 100:0.0} cm; " +
                      $"hands in view-model camera L {vm.transform.InverseTransformPoint(arms.Hand(true).position)} " +
                      $"R {vm.transform.InverseTransformPoint(arms.Hand(false).position)} viewport L {vm.WorldToViewportPoint(arms.Hand(true).position)}; " +
                      $"renderer enabled {skin.enabled} active {skin.gameObject.activeInHierarchy} layer {skin.gameObject.layer} mask {vm.cullingMask} " +
                      $"bounds {skin.bounds.center}/{skin.bounds.size} mat {(skin.sharedMaterial != null ? skin.sharedMaterial.shader.name : "none")} " +
                      $"verts {(skin.sharedMesh != null ? skin.sharedMesh.vertexCount : 0)} bones {skin.bones.Length} root {skin.rootBone?.name}");
        }
        [UnityTest]
        public IEnumerator Diagnose_TailShape()
        {
            var commands = new ScriptedCommands();
            yield return Spawn(commands);
            Animator a = player.GetComponent<FpsCharacter>().BodyAnimator.Animator;
            SkinnedMeshRenderer skin = a.GetComponentInChildren<SkinnedMeshRenderer>();
            Transform[] bones = skin.bones;
            int tail1 = System.Array.FindIndex(bones, b => b.name == "Tail01");
            BoneWeight[] weights = skin.sharedMesh.boneWeights;
            var tailVerts = new List<int>();
            for (int i = 0; i < weights.Length; i++)
            {
                BoneWeight w = weights[i];
                float t = 0f;
                foreach (var (idx, wt) in new[] { (w.boneIndex0, w.weight0), (w.boneIndex1, w.weight1), (w.boneIndex2, w.weight2), (w.boneIndex3, w.weight3) })
                {
                    if (bones[idx].name.StartsWith("Tail")) t += wt;
                }

                if (t > 0.5f) tailVerts.Add(i);
            }

            Transform hips = a.GetBoneTransform(HumanBodyBones.Hips);
            Transform[] tail = Enumerable.Range(1, 4).Select(k => bones.First(b => b.name == $"Tail{k:00}")).ToArray();
            void Measure(string label)
            {
                var baked = new Mesh();
                skin.BakeMesh(baked, true);
                Vector3[] v = baked.vertices;
                Vector3 root = skin.transform.InverseTransformPoint(tail[0].position);
                float reach = tailVerts.Max(i => Vector3.Distance(v[i], root));
                Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
                foreach (int i in tailVerts)
                {
                    min = Vector3.Min(min, v[i]);
                    max = Vector3.Max(max, v[i]);
                }

                string angles = string.Join(", ", tail.Select(t => Quaternion.Angle(Quaternion.Inverse(hips.rotation) * t.rotation,
                    Quaternion.identity).ToString("0")));
                string local = string.Join(", ", tail.Select(t => t.localRotation.eulerAngles.ToString("0")));
                Debug.Log($"[TailDiag] {label}: reach {reach:0.000} m, bounds {(max - min).ToString("0.000")}, tail-vs-hips angles {angles}; local {local}; " +
                          $"bone lengths {Vector3.Distance(tail[0].position, tail[1].position):0.000} {Vector3.Distance(tail[1].position, tail[2].position):0.000}; " +
                          $"tail lossyScale {tail[0].lossyScale.ToString("0.000")}; skinWeights {QualitySettings.skinWeights} quality {QualitySettings.names[QualitySettings.GetQualityLevel()]}");
                Object.Destroy(baked);
            }

            Measure("idle");
            commands.Next.Move = Vector2.up;
            commands.Next.SprintHeld = true;
            yield return Frames(40);
            Measure("sprint");
            commands.Next.SprintHeld = false;
            yield return Frames(30);
            Measure("run");
            var secondary = a.GetComponent<FoxSecondaryMotion>();
            secondary.enabled = false;
            yield return Frames(3);
            Measure("run, no procedural tail");
        }

        /// <summary>The player's own first-person view (world camera + view-model overlay) in a few moments.</summary>
        RenderTexture wide;

        void FirstPerson(string label)
        {
            Camera world = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "PlayerCamera");
            Camera viewModel = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            // A render request draws one camera, not the stack: the world, then the view-model camera on a clear
            // background into its own texture, laid over the world by its alpha (what the overlay does in the game).
            RenderPipeline.SubmitRenderRequest(world, new RenderPipeline.StandardRequest { destination = wide });
            var overlay = new RenderTexture(wide.width, wide.height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var data = viewModel.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            UnityEngine.Rendering.Universal.CameraRenderType type = data.renderType;
            CameraClearFlags flags = viewModel.clearFlags;
            Color background = viewModel.backgroundColor;
            data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
            viewModel.clearFlags = CameraClearFlags.SolidColor;
            viewModel.backgroundColor = new Color(0f, 0f, 0f, 0f);
            RenderPipeline.SubmitRenderRequest(viewModel, new RenderPipeline.StandardRequest { destination = overlay });
            viewModel.backgroundColor = background;
            viewModel.clearFlags = flags;
            data.renderType = type;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = wide;
            var image = new Texture2D(wide.width, wide.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, wide.width, wide.height), 0, 0);
            RenderTexture.active = overlay;
            var top = new Texture2D(wide.width, wide.height, TextureFormat.RGBA32, false);
            top.ReadPixels(new Rect(0, 0, wide.width, wide.height), 0, 0);
            RenderTexture.active = previous;
            Color[] under = image.GetPixels(), over = top.GetPixels();
            for (int i = 0; i < under.Length; i++)
            {
                under[i] = Color.Lerp(under[i], over[i], over[i].a);
            }

            image.SetPixels(under);
            image.Apply();
            Object.Destroy(top);
            overlay.Release();
            Object.Destroy(overlay);
            shots.Add((label, image));
            LogFirstPersonHands(label);
        }

        [UnityTest]
        public IEnumerator Capture_FirstPerson()
        {
            var commands = new ScriptedCommands();
            yield return Spawn(commands);
            wide = new RenderTexture(W * 2, H, 24) { antiAliasing = 4 };
            spawned.Add(wide);
            foreach (Renderer r in player.transform.Find("BodyVisual").GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly; // as in the game
            }

            CharacterAnimatorDriver driver = player.GetComponent<FpsCharacter>().BodyAnimator;
            FirstPerson("FP idle");
            commands.Next.Move = Vector2.up;
            yield return Frames(20);
            FirstPerson("FP run");
            commands.Next.SprintHeld = true;
            yield return Frames(40);
            FirstPerson("FP sprint");
            commands.Next.SprintHeld = false;
            commands.Next.Move = Vector2.zero;
            yield return Frames(30);
            commands.Next.FireHeld = true; // empty a few rounds so the reload happens
            commands.Next.FirePressed = true;
            yield return Frames(20);
            commands.Next.FireHeld = false;
            yield return Frames(10);
            commands.Next.ReloadPressed = true;
            yield return Frames(40);
            FirstPerson("FP reload 0.66 s");
            yield return Frames(80);
            driver.Skill(SkillMotion.QuickCast);
            commands.Next.SkillPressedMask = 1;
            yield return Frames(9);
            FirstPerson("FP cast");
            yield return Frames(60);
            Save("FoxAnim_FirstPerson", 3);
        }

        // ------------------------------------------------------------------ first-person fox layout (fox_fp_arms.py arms)

        [System.Serializable]
        public class FoxFpVariant
        {
            public string label = "default";
            public Vector3 grip = new Vector3(0.15f, -0.20f, 0.39f); // pistol grip in the player camera's space
            public float yaw = -6f;
            public float pitch = 1f;
            public float roll;
            public float torsoYaw = 40f;
            public float lean = 33f;
            public float fov = 70f;
            public Vector3 leftElbow;   // zero: keep the built hints
            public Vector3 rightElbow;
        }

        [System.Serializable]
        public class FoxFpVariantList
        {
            public FoxFpVariant[] variants;
        }

        /// <summary>
        /// Explicit: the in-game first-person view (world + view-model overlay, 1280x720, 4x MSAA) for rifle placements,
        /// chest turns and view-model FOVs read from FP_FOX or Logs/fp_fox.json, with the hand-goal error, wrist bend and
        /// elbow screen positions logged ("[FoxFP]"). Writes Screenshots/FoxFP_Tune.png.
        /// </summary>
        [UnityTest, Explicit]
        public IEnumerator Tune_FirstPersonFox()
        {
            string path = System.Environment.GetEnvironmentVariable("FP_FOX");
            if (string.IsNullOrEmpty(path))
            {
                path = "Logs/fp_fox.json";
            }

            FoxFpVariantList list = File.Exists(path)
                ? JsonUtility.FromJson<FoxFpVariantList>(File.ReadAllText(path))
                : new FoxFpVariantList { variants = new[] { new FoxFpVariant() } };
            var commands = new ScriptedCommands();
            yield return Spawn(commands);
            wide = new RenderTexture(1280, 720, 24) { antiAliasing = 4 };
            spawned.Add(wide);
            foreach (Renderer r in player.transform.Find("BodyVisual").GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            Camera worldCam = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "PlayerCamera");
            Camera viewCam = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            var arms = player.GetComponentInChildren<FirstPersonArms>(true);
            Transform rifle = arms.transform.parent.Find("Rifle");
            yield return Frames(30);
            foreach (FoxFpVariant v in list.variants)
            {
                rifle.SetPositionAndRotation(worldCam.transform.TransformPoint(v.grip),
                    worldCam.transform.rotation * Quaternion.Euler(v.pitch, v.yaw, v.roll));
                arms.TorsoYaw = v.torsoYaw;
                arms.TorsoLean = v.lean;
                if (v.leftElbow != Vector3.zero || v.rightElbow != Vector3.zero)
                {
                    arms.SetElbowHints(v.leftElbow, v.rightElbow);
                }
                viewCam.fieldOfView = v.fov;
                yield return Frames(3);
                FirstPerson(v.label);

                // Where the muzzle is on screen, and how far the barrel's line on screen turns off the tracer's
                // (muzzle → the crosshair's hit 20 m out).
                Transform muzzle = rifle.GetComponentsInChildren<Transform>(true).First(t => t.name == "Muzzle");
                Vector3 vp = viewCam.WorldToViewportPoint(muzzle.position);
                Vector2 m0 = viewCam.WorldToScreenPoint(muzzle.position);
                Vector2 barrel = (Vector2)viewCam.WorldToScreenPoint(muzzle.position + rifle.forward * 2f) - m0;
                Vector2 tracer = (Vector2)worldCam.WorldToScreenPoint(worldCam.transform.position + worldCam.transform.forward * 20f)
                                 - (Vector2)worldCam.WorldToScreenPoint(muzzle.position);
                Debug.Log($"[FoxFP] {v.label}: muzzle viewport ({vp.x:0.000}, {vp.y:0.000}), barrel on screen {Mathf.Atan2(-barrel.y, -barrel.x) * Mathf.Rad2Deg:0.0} deg, " +
                          $"tracer {Mathf.Atan2(-tracer.y, -tracer.x) * Mathf.Rad2Deg:0.0} deg, off by {Vector2.Angle(barrel, tracer):0.0} deg");
                foreach (bool left in new[] { true, false })
                {
                    Transform hand = arms.Hand(left), fore = arms.ForeArm(left);
                    Vector3 axis = (hand.position - fore.position).normalized;
                    Quaternion off = hand.rotation * Quaternion.Inverse(fore.rotation * arms.RestHandInForearm(left));
                    Transform elbow = fore;
                    Debug.Log($"[FoxFP] {v.label} {(left ? "L" : "R")}: goal error {Vector3.Distance(hand.position, arms.GoalPosition(left)) * 100f:0.0} cm, " +
                              $"wrist bend {Vector3.Angle(axis, off * axis):0} deg, elbow viewport {viewCam.WorldToViewportPoint(elbow.position)}, " +
                              $"hand viewport {viewCam.WorldToViewportPoint(hand.position)}");
                }
            }

            Save("FoxFP_Tune", 2);
        }

        // ------------------------------------------------------------------ first-person layout tuning

        [System.Serializable]
        public class FpVariant
        {
            public string label;
            public Vector3 vmRoot;          // ViewModel in camera space
            public Vector3 rifleOffset;     // ViewModel/Rifle
            public Vector3 rifleEuler;
            public Vector3 shoulders;       // shoulder midpoint in ViewModel space
            public float armsYaw;
            public Vector3 leftElbow;       // FirstPersonArms fallback elbow directions (arms space)
            public Vector3 rightElbow;
            public bool gripOverride;       // FP_Grip_L in HeldRifle space
            public Vector3 gripPos;
            public Vector3 gripEuler;
            public bool perArm;             // each shoulder placed on its own (ViewModel space)
            public Vector3 leftShoulder;
            public Vector3 rightShoulder;
            public float gripRoll;          // authored FP_Grip_L rolled about the rifle's forward axis through gripAxis
            public float gripSlide;         // and slid along it (HeldRifle space, metres)
            public Vector3 gripAxis;
            public float foreArmScale;      // bone length scales (0 = unchanged)
            public float upperArmScale;
            public bool auto;               // shoulders and elbow hints solved from where the elbows should be on screen
            public Vector2 leftElbowVp;
            public Vector3 leftUpperDir;    // camera space, elbow -> shoulder
            public Vector2 rightElbowVp;
            public Vector3 rightUpperDir;
            public bool gripFrame;          // left grip from palm point, finger direction and palm normal (HeldRifle space)
            public Vector3 palm;
            public Vector3 along;
            public Vector3 normal;
            public float leftNatural;       // 0..1: elbow pulled toward the straight-wrist line of the hand
            public float rightNatural;
        }

        /// <summary>Test-only: holds FP_Grip_L at a tuned pose after the Animator has written the body (before FirstPersonArms).</summary>
        [DefaultExecutionOrder(150)]
        public class GripOverride : MonoBehaviour
        {
            public Transform grip;
            public Transform rifle;
            public Vector3 position;
            public Quaternion rotation = Quaternion.identity;
            public bool active;

            void LateUpdate()
            {
                if (active)
                {
                    grip.SetPositionAndRotation(rifle.TransformPoint(position), rifle.rotation * rotation);
                }
            }
        }

        [System.Serializable]
        public class FpVariantList
        {
            public FpVariant[] variants;
        }

        /// <summary>
        /// Renders first-person layout variants (view-model placement, shoulders, elbows, left grip, arm lengths) from
        /// a JSON file (env FP_TUNE, default Logs/fp_tune.json; without one, the generated layout as it is): the
        /// player's view plus a close view of the left wrist, and logs where hands and elbows land on screen and how
        /// far each wrist bends. With "auto", shoulders and elbow hints are solved from where the elbows should be
        /// (the numbers to copy into FoxGunnerAnimationFactory.FirstPerson*). Screenshots/FoxAnim_FPTune.png.
        /// </summary>
        [UnityTest]
        public IEnumerator Tune_FirstPersonLayout()
        {
            string path = System.Environment.GetEnvironmentVariable("FP_TUNE");
            if (string.IsNullOrEmpty(path))
            {
                path = "Logs/fp_tune.json";
            }

            FpVariantList list = File.Exists(path) ? JsonUtility.FromJson<FpVariantList>(File.ReadAllText(path)) : null;
            var commands = new ScriptedCommands();
            yield return Spawn(commands);
            wide = new RenderTexture(W * 4, H * 2, 24) { antiAliasing = 4 };
            spawned.Add(wide);
            var sideTarget = new RenderTexture(H * 2, H * 2, 24) { antiAliasing = 4 };
            spawned.Add(sideTarget);
            foreach (Renderer r in player.transform.Find("BodyVisual").GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            Camera viewCam = player.GetComponentsInChildren<Camera>(true).First(c => c.name == "ViewModelCamera");
            var arms = player.GetComponentInChildren<FirstPersonArms>(true);
            Transform armsT = arms.transform;
            Transform vm = armsT.parent;
            Transform rifle = vm.Find("Rifle");
            var vmAnimator = vm.GetComponent<CoopDeck.Player.ViewModelAnimator>();
            Transform bodyRifle = player.GetComponentsInChildren<Transform>(true).First(x => x.name == "HeldRifle");
            Transform grip = player.GetComponentsInChildren<Transform>(true).First(x => x.name == "FP_Grip_L");
            Transform B(string n) => armsT.GetComponentsInChildren<Transform>(true).First(x => x.name == n);
            Vector3 shoulderMid = (armsT.InverseTransformPoint(B("LeftArm").position) + armsT.InverseTransformPoint(B("RightArm").position)) * 0.5f;
            Vector3 leftArmLocal = B("LeftArm").localPosition, rightArmLocal = B("RightArm").localPosition;
            Vector3 gripLocalPos = grip.localPosition;
            Quaternion gripLocalRot = grip.localRotation;
            Vector3 gripRifle0 = bodyRifle.InverseTransformPoint(grip.position);
            Quaternion gripRifleRot0 = Quaternion.Inverse(bodyRifle.rotation) * grip.rotation;
            var gripHold = player.AddComponent<GripOverride>();
            gripHold.grip = grip;
            gripHold.rifle = bodyRifle;
            var boneLocal = new Dictionary<string, Vector3>();
            foreach (string s in new[] { "Left", "Right" })
            {
                boneLocal[s + "ForeArm"] = B(s + "ForeArm").localPosition;
                boneLocal[s + "Hand"] = B(s + "Hand").localPosition;
            }
            Debug.Log($"[FPTune] arms: upper {Vector3.Distance(B("LeftArm").position, B("LeftForeArm").position):0.000} fore {Vector3.Distance(B("LeftForeArm").position, B("LeftHand").position):0.000} " +
                      $"shoulder width {Vector3.Distance(B("LeftArm").position, B("RightArm").position):0.000}; grip in HeldRifle pos {F(bodyRifle.InverseTransformPoint(grip.position))} " +
                      $"euler {F((Quaternion.Inverse(bodyRifle.rotation) * grip.rotation).eulerAngles)}; socket {F(bodyRifle.InverseTransformPoint(bodyRifle.GetComponentsInChildren<Transform>(true).First(x => x.name == "LeftHandGrip").position))}; " +
                      $"rifle bounds {bodyRifle.GetComponentInChildren<MeshFilter>(true).sharedMesh.bounds}; vm {F(vm.localPosition)} rifle {F(rifle.localPosition)} {F(rifle.localEulerAngles)} arms {F(armsT.localPosition)} {F(armsT.localEulerAngles)}");
            System.Reflection.FieldInfo Field(string n) => typeof(FirstPersonArms).GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            // how far the hand is bent off the forearm line (twist excluded)
            float Bend(bool left)
            {
                Transform lower = arms.ForeArm(left), hand = arms.Hand(left);
                Vector3 axis = (hand.position - lower.position).normalized;
                Quaternion off = hand.rotation * Quaternion.Inverse(lower.rotation * arms.RestHandInForearm(left));
                return Vector3.Angle(axis, off * axis);
            }

            if (list == null)
            {
                // no file: one variant that reproduces the generated layout (applying it changes nothing)
                list = new FpVariantList
                {
                    variants = new[]
                    {
                        new FpVariant
                        {
                            label = "generated layout", vmRoot = vm.localPosition, rifleOffset = rifle.localPosition,
                            rifleEuler = rifle.localEulerAngles, shoulders = armsT.localPosition + shoulderMid,
                            armsYaw = armsT.localEulerAngles.y,
                            leftElbow = (Vector3)Field("leftElbow").GetValue(arms), rightElbow = (Vector3)Field("rightElbow").GetValue(arms),
                        },
                    },
                };
            }

            var side = new GameObject("SideCam").AddComponent<Camera>();
            side.cullingMask = 1 << GameLayers.ViewModelIndex;
            side.clearFlags = CameraClearFlags.SolidColor;
            side.backgroundColor = new Color(0.25f, 0.27f, 0.32f);
            side.nearClipPlane = 0.01f;
            side.fieldOfView = 42f;
            spawned.Add(side.gameObject);

            foreach (FpVariant v in list.variants)
            {
                vm.localPosition = v.vmRoot;
                vm.localRotation = Quaternion.identity;
                vmAnimator.Configure(player.GetComponent<FpsCharacter>(), vm);
                rifle.SetLocalPositionAndRotation(v.rifleOffset, Quaternion.Euler(v.rifleEuler));
                Quaternion yaw = Quaternion.Euler(0f, v.armsYaw, 0f);
                armsT.SetLocalPositionAndRotation(v.shoulders - yaw * shoulderMid, yaw);
                Field("leftElbow").SetValue(arms, v.leftElbow);
                Field("rightElbow").SetValue(arms, v.rightElbow);
                // the Animator rewrites FP_Grip_L every frame: hold the tuned pose from LateUpdate instead
                gripHold.active = false;
                grip.SetLocalPositionAndRotation(gripLocalPos, gripLocalRot);
                if (v.gripOverride)
                {
                    gripHold.position = v.gripPos;
                    gripHold.rotation = Quaternion.Euler(v.gripEuler);
                    gripHold.active = true;
                }
                else if (v.gripFrame)
                {
                    // the authored grip's frame (fox_rig FP_LEFT_*: Blender rifle space right/forward/up -> x/z/y here)
                    var palm0 = new Vector3(-0.040f, 0.055f, 0.310f);
                    Quaternion basis0 = Quaternion.LookRotation(new Vector3(0.45f, 0.40f, 0.80f), new Vector3(0.55f, 0.80f, 0f));
                    Quaternion basis1 = Quaternion.LookRotation(v.along, v.normal);
                    Quaternion r1 = basis1 * Quaternion.Inverse(basis0) * gripRifleRot0;
                    Vector3 wristInHand = Quaternion.Inverse(gripRifleRot0) * (gripRifle0 - palm0);
                    gripHold.position = v.palm + r1 * wristInHand;
                    gripHold.rotation = r1;
                    gripHold.active = true;
                }
                else if (v.gripRoll != 0f || v.gripSlide != 0f)
                {
                    Quaternion roll = Quaternion.AngleAxis(v.gripRoll, Vector3.forward);
                    gripHold.position = v.gripAxis + roll * (gripRifle0 - v.gripAxis) + Vector3.forward * v.gripSlide;
                    gripHold.rotation = roll * gripRifleRot0;
                    gripHold.active = true;
                }

                B("LeftArm").localPosition = leftArmLocal;
                B("RightArm").localPosition = rightArmLocal;
                foreach (string s in new[] { "Left", "Right" })
                {
                    B(s + "ForeArm").localPosition = boneLocal[s + "ForeArm"] * (v.upperArmScale > 0f ? v.upperArmScale : 1f);
                    B(s + "Hand").localPosition = boneLocal[s + "Hand"] * (v.foreArmScale > 0f ? v.foreArmScale : 1f);
                }

                if (v.perArm)
                {
                    B("LeftArm").position = vm.TransformPoint(v.leftShoulder);
                    B("RightArm").position = vm.TransformPoint(v.rightShoulder);
                }

                yield return Frames(4);
                if (v.auto)
                {
                    Solve("Left", v.leftElbowVp, v.leftUpperDir, "leftElbow");
                    Solve("Right", v.rightElbowVp, v.rightUpperDir, "rightElbow");
                }

                // shoulder at the end of the upper arm from the wanted elbow; the hint points the IK elbow there
                void Solve(string s, Vector2 elbowVp, Vector3 upperDir, string field)
                {
                    float l1 = Vector3.Distance(B(s + "Arm").position, B(s + "ForeArm").position);
                    float l2 = Vector3.Distance(B(s + "ForeArm").position, B(s + "Hand").position);
                    Vector3 wrist = arms.GoalPosition(s == "Left");
                    Ray ray = viewCam.ViewportPointToRay(new Vector3(elbowVp.x, elbowVp.y, 0f));
                    Vector3 w = wrist - ray.origin;
                    float dw = Vector3.Dot(ray.direction, w);
                    float disc = dw * dw - w.sqrMagnitude + l2 * l2;
                    float t = disc >= 0f ? dw - Mathf.Sqrt(disc) : dw;
                    if (disc >= 0f && Vector3.Dot(ray.direction * t, viewCam.transform.forward) < 0.15f)
                    {
                        t = dw + Mathf.Sqrt(disc); // the near solution would put the elbow (and shoulder) at the lens
                    }
                    Vector3 elbow = ray.origin + ray.direction * t;
                    float natural = s == "Left" ? v.leftNatural : v.rightNatural;
                    if (natural > 0f)
                    {
                        // the hand already holds its goal rotation: the forearm line that leaves the wrist straight
                        bool left = s == "Left";
                        Transform lower = arms.ForeArm(left), hand = arms.Hand(left);
                        Vector3 localAxis = Quaternion.Inverse(lower.rotation) * (hand.position - lower.position).normalized;
                        Vector3 straight = hand.rotation * Quaternion.Inverse(arms.RestHandInForearm(left)) * localAxis;
                        Vector3 toElbow = Vector3.Lerp((elbow - wrist).normalized, -straight, natural).normalized;
                        elbow = wrist + toElbow * l2;
                    }

                    Vector3 shoulder = elbow + viewCam.transform.TransformDirection(upperDir.normalized) * l1;
                    B(s + "Arm").position = shoulder;
                    Vector3 axis = (wrist - shoulder).normalized;
                    Vector3 pole = elbow - (shoulder + axis * Vector3.Dot(elbow - shoulder, axis));
                    Vector3 hint = armsT.InverseTransformDirection(pole.normalized);
                    Field(field).SetValue(arms, hint);
                    Debug.Log($"[FPTune] {v.label} {s}: l1 {l1:0.000} l2 {l2:0.000} elbow vp {F(viewCam.WorldToViewportPoint(elbow))} " +
                              $"{(disc >= 0f ? "" : $"UNREACHABLE by {(Mathf.Sqrt(-disc)) * 100f:0.0} cm ")}shoulder (vm) {F(vm.InverseTransformPoint(shoulder))} hint (arms) {F(hint)} " +
                              $"wrist cam {F(viewCam.transform.InverseTransformPoint(wrist))}");
                }

                yield return Frames(12);
                FirstPerson("FPT " + v.label);
                Texture2D fpImage = shots[shots.Count - 1].image;
                shots.RemoveAt(shots.Count - 1);

                Transform cam = viewCam.transform;
                side.transform.position = cam.TransformPoint(new Vector3(-0.42f, -0.32f, 0.48f));
                side.transform.LookAt(cam.TransformPoint(new Vector3(0.03f, -0.16f, 0.68f)));
                side.targetTexture = sideTarget;
                side.Render();
                side.targetTexture = null;
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = sideTarget;
                var sideImage = new Texture2D(H * 2, H * 2, TextureFormat.RGB24, false);
                sideImage.ReadPixels(new Rect(0, 0, H * 2, H * 2), 0, 0);
                sideImage.Apply();
                RenderTexture.active = previous;

                var row = new Texture2D(W * 4 + H * 2, H * 2, TextureFormat.RGB24, false);
                row.SetPixels(0, 0, W * 4, H * 2, fpImage.GetPixels());
                row.SetPixels(W * 4, 0, H * 2, H * 2, sideImage.GetPixels());
                row.Apply();
                Object.Destroy(fpImage);
                Object.Destroy(sideImage);
                shots.Add(("FPT " + v.label, row));

                Vector3 Vp(Transform x) => viewCam.WorldToViewportPoint(x.position);
                Debug.Log($"[FPTune] {v.label}: viewport L hand {F(Vp(B("LeftHand")))} L elbow {F(Vp(B("LeftForeArm")))} L shoulder {F(Vp(B("LeftArm")))} | " +
                          $"R hand {F(Vp(B("RightHand")))} R elbow {F(Vp(B("RightForeArm")))} R shoulder {F(Vp(B("RightArm")))} | " +
                          $"goal error L {Vector3.Distance(B("LeftHand").position, arms.GoalPosition(true)) * 100f:0.0} cm R {Vector3.Distance(B("RightHand").position, arms.GoalPosition(false)) * 100f:0.0} cm; wrist bend L {Bend(true):0} R {Bend(false):0} deg");
            }

            Save("FoxAnim_FPTune", 1);
        }

        static string F(Vector3 v) => $"({v.x:0.000}, {v.y:0.000}, {v.z:0.000})";
    }
}
#endif
