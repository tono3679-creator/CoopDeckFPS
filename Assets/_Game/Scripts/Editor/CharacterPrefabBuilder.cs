using System.Collections.Generic;
using System.Linq;
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

            // The same Fire Rifle the first-person view shows, on the body's Weapon bone: the clips animate that bone
            // (grip frame) and hand IK keeps both hands on it (Docs/FOX_ANIMATION.md). The body is still in its bind
            // pose here, where the Weapon bone is the rifle frame facing forward.
            Transform weaponBone = FoxGunnerAnimationFactory.WeaponBone(body.transform);
            var heldRifle = (GameObject)PrefabUtility.InstantiatePrefab(rifleVisual.Prefab, weaponBone);
            heldRifle.name = "HeldRifle";
            heldRifle.transform.SetLocalPositionAndRotation(Vector3.zero, FoxGunnerAnimationFactory.RifleLocalRotation(body.transform, weaponBone));

            foreach (Renderer bodyRenderer in body.GetComponentsInChildren<Renderer>(true))
            {
                bodyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            var bodyDriver = root.AddComponent<CharacterAnimatorDriver>();
            bodyDriver.Configure(bodyAnimator, motor, movement != null ? movement.walkSpeed : 6.5f, FoxGunnerAssetFactory.RunClipSpeed);
            // Blend tree in m/s on the walk / run / sprint rings; LocomotionSpeed keeps the planted foot in step.
            FoxGunnerAnimationFactory.ClipTable clipTable = FoxGunnerAnimationFactory.LoadTable();
            LocomotionRing[] rings = FoxGunnerAnimationFactory.Rings(clipTable);
            float reloadClip = clipTable.clips.First(c => c.name == FoxGunnerAnimationFactory.Prefix + "Rifle_Reload").seconds;
            bodyDriver.ConfigureRings(rings, rings[rings.Length - 1].speed, reloadClip);
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
            // Post-processing runs once, on the stack's last camera (the view-model overlay below): URP applies
            // tonemapping, bloom and SMAA inside the camera that post-processes, so on this camera they ran before the
            // rifle and arms were drawn (Docs/WEAPON_VISUAL_QUALITY.md). The base camera's AA setting applies to the stack.
            cameraData.renderPostProcessing = false;
            cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            cameraData.antialiasingQuality = AntialiasingQuality.High;

            // View-model overlay camera: own FOV, never clips into walls.
            var viewModelCameraGo = new GameObject("ViewModelCamera");
            viewModelCameraGo.transform.SetParent(cameraGo.transform, false);
            var viewModelCamera = viewModelCameraGo.AddComponent<Camera>();
            viewModelCamera.nearClipPlane = 0.01f;
            viewModelCamera.farClipPlane = 10f;
            viewModelCamera.fieldOfView = ViewModelFieldOfView;
            viewModelCamera.cullingMask = 1 << GameLayers.ViewModelIndex;
            UniversalAdditionalCameraData viewModelData = viewModelCamera.GetUniversalAdditionalCameraData();
            viewModelData.renderType = CameraRenderType.Overlay;
            viewModelData.renderShadows = false;
            viewModelData.renderPostProcessing = true; // the whole stack: world, rifle, arms, muzzle flash
            cameraData.cameraStack.Add(viewModelCamera);

            Transform muzzle = BuildRifleViewModel(cameraGo.transform, m, rifleVisual, out GameObject viewModelRoot);
            // First-person arms: the fox mesh's forearms and gloves, posed every frame from the body's authored hand goals.
            GameObject fpArms = FoxGunnerAnimationFactory.AddFirstPersonArms(viewModelRoot.transform, body.transform, GameLayers.ViewModelIndex, bodyDriver);

            EditorUtil.SetObject(hitscan, "muzzle", muzzle);
            EditorUtil.SetObject(caster, "castOrigin", muzzle);
            // Projectile skills (fireball) charge in and leave from the first-person left palm.
            Transform leftHand = fpArms != null && fpArms.TryGetComponent(out FirstPersonArms armsIk) ? armsIk.Hand(true) : null;
            EditorUtil.SetObject(caster, "castHand", leftHand);
            EditorUtil.SetObject(caster, "viewModelCamera", viewModelCamera);
            EditorUtil.SetObject(caster, "worldCamera", camera);
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
            // the pivot of bob, sway and kick (ViewModelAnimator), a little above and behind the grip
            viewModelRoot.transform.localPosition = ViewModelRootPosition;

            var rifle = new GameObject("Rifle");
            rifle.transform.SetParent(viewModelRoot.transform, false);
            // The rifle sits where the fox's own arms hold it from its shoulders (FirstPersonArms): the grip within the
            // right arm's reach, the body's left-hand hold within the left arm's; the muzzle turned a little toward
            // the crosshair shows the rifle's side, and tipping it down a little lifts the grip end into view.
            rifle.transform.SetLocalPositionAndRotation(ViewModelGripInCamera - ViewModelRootPosition,
                Quaternion.Euler(ViewModelRiflePitch, ViewModelRifleYaw, 0f));
            Transform r = rifle.transform;

            // The model's origin is its pistol grip: put it in the right hand. Its LeftHandGrip socket lands on the left hand.
            var model = (GameObject)PrefabUtility.InstantiatePrefab(rifleVisual.Prefab, r);
            model.name = "FireRifle";
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = rifleVisual.ViewModelMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            // The fox's own arms are added by FoxGunnerAnimationFactory.AddFirstPersonArms (they follow the body's hand goals).

            return model.transform.Find(FireRifleAssetFactory.MuzzleSocket);
        }

        /// <summary>The first-person rifle's pistol grip (the right hand) in the player camera's (the eyes') space.</summary>
        public static readonly Vector3 ViewModelGripInCamera = new Vector3(0.15f, -0.20f, 0.39f);

        /// <summary>The view-model root (the pivot of bob, sway and kick) in the player camera's space.</summary>
        public static readonly Vector3 ViewModelRootPosition = new Vector3(0.11f, -0.17f, 0.30f);

        /// <summary>Muzzle turned this many degrees toward the screen centre.</summary>
        public const float ViewModelRifleYaw = -6f;

        /// <summary>Muzzle tipped down this many degrees (the grip end rises).</summary>
        public const float ViewModelRiflePitch = 1f;

        /// <summary>The view-model overlay camera's vertical field of view.</summary>
        public const float ViewModelFieldOfView = 70f;

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
