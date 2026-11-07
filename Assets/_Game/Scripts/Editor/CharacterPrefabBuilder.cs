using System.Collections.Generic;
using CoopDeck.Animation;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Enemies;
using CoopDeck.Input;
using CoopDeck.Items;
using CoopDeck.Player;
using CoopDeck.Skills;
using CoopDeck.UI;
using CoopDeck.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Builds the placeholder player and target prefabs. The hierarchy mirrors the final one
    /// (body visual, camera pivot, view model, muzzle), so real models replace children only.
    /// </summary>
    public static class CharacterPrefabBuilder
    {
        public const float EyeHeight = 1.62f;

        public static GameObject BuildPlayer(InputActionAsset input, WeaponDefinition weapon, MovementProfile movement, MaterialSet m,
            DeckDefinition starterDeck, List<GaugeItemDefinition> startingItems, GameObject bodyPrefab, FireRifleAssets rifleVisual,
            DeckDefinition practiceDeck = null)
        {
            EditorUtil.EnsureFolder(GamePaths.CharacterPrefabs);

            var root = new GameObject("Player");
            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.4f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.06f;
            controller.minMoveDistance = 0f;

            var health = root.AddComponent<Health>();
            EditorUtil.SetFloat(health, "maxHealth", 150f);
            EditorUtil.SetInt(health, "team", (int)TeamId.Players);

            var motor = root.AddComponent<FpsMotor>();
            EditorUtil.SetObject(motor, "profile", movement);
            var look = root.AddComponent<FpsLook>();
            var reader = root.AddComponent<PlayerInputReader>();
            EditorUtil.SetObject(reader, "actions", input);
            var hitscan = root.AddComponent<HitscanWeapon>();
            EditorUtil.SetObject(hitscan, "definition", weapon);
            var weaponFx = root.AddComponent<WeaponFx>();
            EditorUtil.SetObject(weaponFx, "weapon", hitscan);
            EditorUtil.SetInt(weaponFx, "muzzleFlashLayer", GameLayers.ViewModelIndex);
            var character = root.AddComponent<FpsCharacter>();
            root.AddComponent<CombatStatus>();
            // Downed → an ally (NPC) can revive; with no ally around, PlayerRespawn respawns as before.
            var revivable = root.AddComponent<Revivable>();
            EditorUtil.SetFloat(revivable, "reviveTime", 2.5f);
            EditorUtil.SetFloat(revivable, "bleedOutTime", 20f);
            root.AddComponent<AllyCollisionFilter>();
            root.AddComponent<PlayerRespawn>();
            var caster = root.AddComponent<SkillCaster>();
            var deckController = root.AddComponent<DeckController>();
            EditorUtil.SetObject(deckController, "startingDeck", starterDeck);
            EditorUtil.SetObject(deckController, "practiceDeck", practiceDeck);
            EditorUtil.SetObjectArray(deckController, "startingItems", startingItems.ToArray());
            EditorUtil.SetObject(deckController, "character", character);
            EditorUtil.SetObject(deckController, "caster", caster);

            // Full-body model (Fox Gunner, Humanoid, PF_FoxGunner_Body) kept as a nested prefab so model updates
            // flow in. Others will see it; the local player only sees its shadow. Its controller matches the
            // run cycle to the ground speed itself, so the driver's playback-rate parameter goes unused.
            var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, root.transform);
            body.name = "BodyVisual";
            body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            var bodyAnimator = body.GetComponent<Animator>();
            bodyAnimator.applyRootMotion = false;
            bodyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // bones may drive sockets/IK even when off screen

            // The same Fire Rifle the first-person view shows, carried in the body's right hand (its shadow for now).
            FireRifleAssetFactory.RightHandHold(bodyPrefab, out Vector3 holdPosition, out Quaternion holdRotation);
            var heldRifle = (GameObject)PrefabUtility.InstantiatePrefab(rifleVisual.Prefab, bodyAnimator.GetBoneTransform(HumanBodyBones.RightHand));
            heldRifle.name = "HeldRifle";
            heldRifle.transform.SetLocalPositionAndRotation(holdPosition, holdRotation);

            foreach (Renderer bodyRenderer in body.GetComponentsInChildren<Renderer>(true))
            {
                bodyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            var bodyDriver = root.AddComponent<CharacterAnimatorDriver>();
            bodyDriver.Configure(bodyAnimator, motor, movement != null ? movement.walkSpeed : 6.5f, FoxGunnerAssetFactory.RunClipSpeed);
            EditorUtil.SetObject(character, "bodyAnimator", bodyDriver);

            var pivot = new GameObject("CameraPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, EyeHeight, 0f);

            // World camera.
            var cameraGo = new GameObject("PlayerCamera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.SetParent(pivot.transform, false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 400f;
            camera.fieldOfView = 68f;
            camera.cullingMask = ~(1 << GameLayers.ViewModelIndex);
            cameraGo.AddComponent<AudioListener>();
            cameraGo.AddComponent<CameraShaker>(); // small, capped shake for combos (aim ray comes from the pivot)
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = true;
            cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;

            // View-model overlay camera: own FOV, never clips into walls.
            var viewModelCameraGo = new GameObject("ViewModelCamera");
            viewModelCameraGo.transform.SetParent(cameraGo.transform, false);
            var viewModelCamera = viewModelCameraGo.AddComponent<Camera>();
            viewModelCamera.nearClipPlane = 0.01f;
            viewModelCamera.farClipPlane = 10f;
            viewModelCamera.fieldOfView = 55f;
            viewModelCamera.cullingMask = 1 << GameLayers.ViewModelIndex;
            UniversalAdditionalCameraData viewModelData = viewModelCamera.GetUniversalAdditionalCameraData();
            viewModelData.renderType = CameraRenderType.Overlay;
            viewModelData.renderShadows = false;
            cameraData.cameraStack.Add(viewModelCamera);

            Transform muzzle = BuildRifleViewModel(cameraGo.transform, m, rifleVisual, out GameObject viewModelRoot);

            EditorUtil.SetObject(hitscan, "muzzle", muzzle);
            EditorUtil.SetObject(caster, "castOrigin", muzzle);
            EditorUtil.SetObject(look, "yawRoot", root.transform);
            EditorUtil.SetObject(look, "pitchPivot", pivot.transform);
            EditorUtil.SetObject(look, "viewCamera", camera);
            EditorUtil.SetObject(character, "motor", motor);
            EditorUtil.SetObject(character, "look", look);
            EditorUtil.SetObject(character, "health", health);
            EditorUtil.SetObject(character, "weapon", hitscan);
            EditorUtil.SetObject(character, "commandSourceComponent", reader);

            var animator = viewModelRoot.AddComponent<ViewModelAnimator>();
            EditorUtil.SetObject(animator, "character", character);
            EditorUtil.SetObject(animator, "viewModelRoot", viewModelRoot.transform);

            root.layer = GameLayers.PlayerIndex;
            EditorUtil.SetLayerRecursively(body, GameLayers.PlayerIndex);
            EditorUtil.SetLayerRecursively(viewModelRoot, GameLayers.ViewModelIndex);

            string path = GamePaths.CharacterPrefabs + "/PF_Player.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Fire Rifle model (nested PF_Weapon_FireRifle, thin-outline material) and placeholder gloves.
        /// Returns the rifle's muzzle socket.
        /// </summary>
        static Transform BuildRifleViewModel(Transform cameraTransform, MaterialSet m, FireRifleAssets rifleVisual, out GameObject viewModelRoot)
        {
            viewModelRoot = new GameObject("ViewModel");
            viewModelRoot.transform.SetParent(cameraTransform, false);
            // At the overlay camera's 55° FOV this sits in the lower-right quarter of the screen.
            viewModelRoot.transform.localPosition = new Vector3(0.15f, -0.135f, 0.42f);

            var rifle = new GameObject("Rifle");
            rifle.transform.SetParent(viewModelRoot.transform, false);
            // The Fire Rifle is bulkier than the old placeholder: further out and lower keeps the stock off the lens
            // and the scope under the crosshair line; turning the muzzle a little toward the crosshair shows the
            // rifle's side (flame emblem, energy cell). The gloves move with it.
            rifle.transform.SetLocalPositionAndRotation(ViewModelRifleOffset, Quaternion.Euler(0f, ViewModelRifleYaw, 0f));
            Transform r = rifle.transform;

            // The model's origin is its pistol grip: put it in the right hand. Its LeftHandGrip socket lands on the left hand.
            var model = (GameObject)PrefabUtility.InstantiatePrefab(rifleVisual.Prefab, r);
            model.name = "FireRifle";
            model.transform.SetLocalPositionAndRotation(ViewModelGripPosition, Quaternion.identity);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = rifleVisual.ViewModelMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            // Placeholder hands, to be replaced by the first-person arm rig (see ANIMATION_GUIDELINES.md).
            EditorUtil.VisualPrimitive(PrimitiveType.Capsule, "RightHand", r, new Vector3(0.01f, -0.1f, -0.1f), new Vector3(0.065f, 0.08f, 0.065f), m.Glove, new Vector3(-20f, 0f, 0f), false);
            EditorUtil.VisualPrimitive(PrimitiveType.Capsule, "RightForearm", r, new Vector3(0.05f, -0.16f, -0.24f), new Vector3(0.07f, 0.12f, 0.07f), m.Glove, new Vector3(-60f, 0f, 0f), false);
            EditorUtil.VisualPrimitive(PrimitiveType.Capsule, "LeftHand", r, new Vector3(-0.03f, -0.05f, 0.27f), new Vector3(0.065f, 0.07f, 0.065f), m.Glove, new Vector3(0f, 0f, 80f), false);
            EditorUtil.VisualPrimitive(PrimitiveType.Capsule, "LeftForearm", r, new Vector3(-0.12f, -0.13f, 0.18f), new Vector3(0.07f, 0.13f, 0.07f), m.Glove, new Vector3(-50f, 40f, 0f), false);

            return model.transform.Find(FireRifleAssetFactory.MuzzleSocket);
        }

        /// <summary>Where the first-person rifle's grip sits inside ViewModel/Rifle (the right-hand glove).</summary>
        public static readonly Vector3 ViewModelGripPosition = new Vector3(0f, -0.11f, -0.12f);

        /// <summary>ViewModel/Rifle (rifle and gloves together) relative to the view-model root.</summary>
        public static readonly Vector3 ViewModelRifleOffset = new Vector3(0.01f, -0.035f, 0.11f);

        /// <summary>Muzzle turned this many degrees toward the screen centre.</summary>
        public const float ViewModelRifleYaw = -6f;

        public static GameObject BuildTargetDummy(MaterialSet m, FxPrefabSet fx)
        {
            EditorUtil.EnsureFolder(GamePaths.EnemyPrefabs);

            var root = new GameObject("TargetDummy");
            var health = root.AddComponent<Health>();
            EditorUtil.SetFloat(health, "maxHealth", 120f);
            EditorUtil.SetInt(health, "team", (int)TeamId.Enemies);
            root.AddComponent<CombatStatus>();   // burn, marking
            root.AddComponent<ForceReceiver>();  // gravity pulls

            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(visual.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            body.transform.localScale = new Vector3(0.9f, 0.75f, 0.9f);
            body.GetComponent<MeshRenderer>().sharedMaterial = m.EnemyBody;
            body.AddComponent<HitZone>().Configure(health, HitZoneType.Body, 1f);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(visual.transform, false);
            head.transform.localPosition = new Vector3(0f, 1.95f, 0f);
            head.transform.localScale = Vector3.one * 0.52f;
            head.GetComponent<MeshRenderer>().sharedMaterial = m.EnemyHead;
            head.AddComponent<HitZone>().Configure(health, HitZoneType.Head, 1f);

            EditorUtil.VisualPrimitive(PrimitiveType.Cube, "Eye", head.transform, new Vector3(0f, 0.05f, -0.45f), new Vector3(0.55f, 0.16f, 0.12f), m.EnemyEye, castShadows: false);
            EditorUtil.VisualPrimitive(PrimitiveType.Cylinder, "Base", visual.transform, new Vector3(0f, 0.05f, 0f), new Vector3(1.1f, 0.05f, 1.1f), m.EnemyBase);

            var bar = new GameObject("HealthBar");
            bar.transform.SetParent(root.transform, false);
            bar.transform.localPosition = new Vector3(0f, 2.5f, 0f);
            GameObject background = EditorUtil.VisualPrimitive(PrimitiveType.Quad, "Background", bar.transform, Vector3.zero, new Vector3(1.04f, 0.14f, 1f), m.BarBackground, castShadows: false);
            GameObject fill = EditorUtil.VisualPrimitive(PrimitiveType.Quad, "Fill", bar.transform, new Vector3(0f, 0f, -0.001f), new Vector3(1f, 0.1f, 1f), m.BarFill, castShadows: false);
            background.GetComponent<MeshRenderer>().receiveShadows = false;
            fill.GetComponent<MeshRenderer>().receiveShadows = false;
            bar.AddComponent<WorldHealthBar>().Configure(health, fill.transform, background.transform);

            var dummy = root.AddComponent<TargetDummy>();
            dummy.Configure(visual, fx.EnemyDeath, 3f);

            var flash = root.AddComponent<HitFlash>();
            EditorUtil.SetObjectArray(flash, "renderers", new Object[]
            {
                body.GetComponent<Renderer>(),
                head.GetComponent<Renderer>(),
            });

            EditorUtil.SetLayerRecursively(root, GameLayers.EnemyIndex);

            string path = GamePaths.EnemyPrefabs + "/PF_TargetDummy.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
