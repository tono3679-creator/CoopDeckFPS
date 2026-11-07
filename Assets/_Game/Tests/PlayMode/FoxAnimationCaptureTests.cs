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
            var sheet = new Texture2D(w * cols, H * rows, TextureFormat.RGB24, false);
            Color[] fill = Enumerable.Repeat(new Color(0.12f, 0.12f, 0.14f), w * cols * H * rows).ToArray();
            sheet.SetPixels(fill);
            for (int i = 0; i < shots.Count; i++)
            {
                int cx = i % cols;
                int cy = rows - 1 - i / cols;
                sheet.SetPixels(cx * w, cy * H, w, H, shots[i].image.GetPixels());
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
            world.targetTexture = wide;
            world.Render();
            world.targetTexture = null;
            // a manual Render() does not draw the URP stack: draw the view-model camera over it as its own base camera
            var data = viewModel.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            UnityEngine.Rendering.Universal.CameraRenderType type = data.renderType;
            CameraClearFlags flags = viewModel.clearFlags;
            data.renderType = UnityEngine.Rendering.Universal.CameraRenderType.Base;
            viewModel.clearFlags = CameraClearFlags.Depth;
            viewModel.targetTexture = wide;
            viewModel.Render();
            viewModel.targetTexture = null;
            viewModel.clearFlags = flags;
            data.renderType = type;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = wide;
            var image = new Texture2D(W * 2, H, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, W * 2, H), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
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
            Save("FoxAnim_FirstPerson", 2);
        }
    }
}
#endif
