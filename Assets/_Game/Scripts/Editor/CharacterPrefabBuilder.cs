using System.Collections.Generic;
using System.Linq;
using CoopDeck.Animation;
using CoopDeck.Cards;
using CoopDeck.Characters;
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

        /// <param name="characters">
        /// The playable characters (CoopDeck.Characters): the first is the body built into this prefab (the fox), the
        /// others bring a kit that <see cref="PlayerCharacterSwap"/> puts in at start when selected. Null: fox only.
        /// </param>
        public static GameObject BuildPlayer(InputActionAsset input, WeaponDefinition weapon, MovementProfile movement, MaterialSet m,
            DeckDefinition starterDeck, List<GaugeItemDefinition> startingItems, GameObject bodyPrefab, FireRifleAssets rifleVisual,
            DeckDefinition practiceDeck = null, CharacterDefinition[] characters = null)
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
            RelinkController(bodyAnimator, FoxGunnerAnimationFactory.Fox.ControllerPath);
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

            if (characters != null && characters.Length > 0)
            {
                // Another character (launch argument -character <id>, CoopDeck > Play As) replaces the fox's body and
                // first-person parts with its kit before anything wakes up; gameplay components stay as they are.
                var swap = root.AddComponent<PlayerCharacterSwap>();
                GameObject viewRifle = viewModelRoot.transform.Find("Rifle").gameObject;
                swap.Configure(characters, body, new[] { viewRifle, fpArms }, viewModelRoot.transform, cameraGo.transform, bodyDriver,
                    motor, movement != null ? movement.walkSpeed : 6.5f, hitscan, caster, viewModelCamera, camera);
            }

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

            // The rifle sits where the fox's own arms hold it from its shoulders (FirstPersonArms): the grip within the
            // right arm's reach, the body's left-hand hold within the left arm's. Carried low at the hip, the muzzle just
            // right of and below the crosshair, the barrel parallel to the view (its line runs into the crosshair, so the
            // shots fly straight out of it) and the receiver lying toward the lower right.
            // The fox's own arms are added by FoxGunnerAnimationFactory.AddFirstPersonArms (they follow the body's hand goals).
            return AddViewModelWeapon(viewModelRoot.transform, rifleVisual, "FireRifle", ViewModelGripInCamera, ViewModelHipPitch, ViewModelHipYaw);
        }

        /// <summary>
        /// The view-model weapon: a "Rifle" node under the view-model root holding the weapon prefab (thin-outline
        /// material, no shadows), its grip at <paramref name="gripInCamera"/> in the player camera's space. Returns the muzzle.
        /// </summary>
        static Transform AddViewModelWeapon(Transform viewModelRoot, FireRifleAssets weaponVisual, string modelName, Vector3 gripInCamera,
            float pitch, float yaw)
        {
            var rifle = new GameObject("Rifle");
            rifle.transform.SetParent(viewModelRoot, false);
            rifle.transform.SetLocalPositionAndRotation(gripInCamera - ViewModelRootPosition, Quaternion.Euler(pitch, yaw, 0f));

            // The model's origin is its pistol grip: put it in the right hand. Its LeftHandGrip socket lands on the left hand.
            var model = (GameObject)PrefabUtility.InstantiatePrefab(weaponVisual.Prefab, rifle.transform);
            model.name = modelName;
            model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                renderer.sharedMaterial = weaponVisual.ViewModelMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return model.transform.Find(FireRifleAssetFactory.MuzzleSocket);
        }

        /// <summary>Where a character's first-person weapon sits in the player camera's space.</summary>
        public struct ViewModelLayout
        {
            public Vector3 GripInCamera;
            public float Pitch;
            public float Yaw;
        }

        /// <summary>
        /// A character kit (CoopDeck.Characters.CharacterKit) for <see cref="PlayerCharacterSwap"/>: the body with its
        /// held weapon on the Weapon bone (shadow only for the local player, like the fox's in PF_Player) and the
        /// first-person weapon and arms under a "ViewModel" node laid out as the player's view-model root's children.
        /// The arms' eyes are set to the player camera when the kit is put into the player.
        /// </summary>
        public static CharacterKit BuildCharacterKit(string name, GameObject bodyPrefab, FireRifleAssets weaponVisual, string weaponName,
            string heldName, FoxGunnerAnimationFactory.CharacterAnimSpec animSpec, FoxGunnerAnimationFactory.FirstPersonSpec firstPerson,
            ViewModelLayout layout)
        {
            EditorUtil.EnsureFolder(GamePaths.CharacterPrefabs);
            var root = new GameObject("PF_CharacterKit_" + name);
            var kit = root.AddComponent<CharacterKit>();

            var body = (GameObject)PrefabUtility.InstantiatePrefab(bodyPrefab, root.transform);
            body.name = "BodyVisual";
            body.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            var bodyAnimator = body.GetComponent<Animator>();
            RelinkController(bodyAnimator, animSpec.ControllerPath);
            bodyAnimator.applyRootMotion = false;
            bodyAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // bones drive sockets and IK even off screen

            Transform weaponBone = FoxGunnerAnimationFactory.WeaponBone(body.transform);
            var held = (GameObject)PrefabUtility.InstantiatePrefab(weaponVisual.Prefab, weaponBone);
            held.name = heldName;
            held.transform.SetLocalPositionAndRotation(Vector3.zero, FoxGunnerAnimationFactory.RifleLocalRotation(body.transform, weaponBone));
            foreach (Renderer bodyRenderer in body.GetComponentsInChildren<Renderer>(true))
            {
                bodyRenderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            }

            var viewModel = new GameObject("ViewModel");
            viewModel.transform.SetParent(root.transform, false);
            Transform muzzle = AddViewModelWeapon(viewModel.transform, weaponVisual, weaponName, layout.GripInCamera, layout.Pitch, layout.Yaw);
            GameObject arms = FoxGunnerAnimationFactory.AddFirstPersonArms(viewModel.transform, body.transform, GameLayers.ViewModelIndex, null,
                firstPerson, out Vector3 leftShoulder, out Vector3 rightShoulder);

            FoxGunnerAnimationFactory.ClipTable table = FoxGunnerAnimationFactory.LoadTable(animSpec);
            LocomotionRing[] rings = FoxGunnerAnimationFactory.Rings(table, animSpec.Prefix);
            float reload = table.clips.First(c => c.name == animSpec.Prefix + "Rifle_Reload").seconds;
            kit.Configure(body, bodyAnimator, viewModel.transform, muzzle, arms.GetComponent<FirstPersonArms>());
            kit.ConfigureDriver(FoxGunnerAssetFactory.RunClipSpeed, rings, rings[rings.Length - 1].speed, reload);
            kit.ConfigureShoulders(leftShoulder, rightShoulder, firstPerson.TorsoYaw, firstPerson.TorsoLean);

            EditorUtil.SetLayerRecursively(body, GameLayers.PlayerIndex);
            EditorUtil.SetLayerRecursively(viewModel, GameLayers.ViewModelIndex);

            string path = GamePaths.CharacterPrefabs + "/" + root.name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<CharacterKit>();
        }

        /// <summary>
        /// The body's controller, loaded again when the body prefab in memory lost it. The animation factories delete and
        /// re-create the controller every run; in a live Editor that already had the body prefab loaded, the loaded prefab
        /// keeps pointing at the deleted one (null), and a player or kit built from it would save "no controller" as an
        /// override (2026-11-08: the fox and the rabbit stood still after a bootstrap run from a live Editor).
        /// </summary>
        static void RelinkController(Animator animator, string controllerPath)
        {
            if (animator != null && animator.runtimeAnimatorController == null)
            {
                animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(controllerPath);
            }
        }

        /// <summary>
        /// The first-person rifle's pistol grip (the right hand) in the player camera's (the eyes') space: a low hip
        /// carry after the reference the user picked (2026-11-09). The barrel runs parallel to the view
        /// (<see cref="ViewModelHipPitch"/>, <see cref="ViewModelHipYaw"/>), so its line on screen runs into the crosshair and
        /// the shots fly straight out of it to the hit. The muzzle sits right of and below the crosshair (viewport about
        /// 0.63, 0.41) and the rifle lies toward the lower right at 27 deg on screen; the left hand holds the handguard at
        /// the lower edge. With the barrel parallel the slope on screen is set by where the barrel sits: higher is flatter.
        /// This is the closest to the composition the user kept (grip 0.234, -0.275 with the muzzle tipped 6 deg down,
        /// 22 deg, but the tracers left the barrel 20 deg off): 2 cm higher, its silhouette overlaps that one 83%
        /// (same height 85% at 31 deg, 4.4 cm higher 70% at 22 deg). (0.215, -0.29) ran the rifle diagonally into the
        /// corner (35 deg). Further out (z 0.40) the fox's left arm no longer reaches the handguard.
        /// </summary>
        public static readonly Vector3 ViewModelGripInCamera = new Vector3(0.234f, -0.255f, 0.38f);

        public static readonly Vector3 ViewModelRootPosition = new Vector3(0.194f, -0.245f, 0.29f);

        /// <summary>
        /// The muzzle's turn from the camera's forward (degrees, negative = left). Near 0 the barrel's line runs into the
        /// crosshair on screen, so tracers (muzzle → hit) leave along the barrel; -6 pointed it 6 deg left of where the
        /// bullets go (2026-11-08, user: the barrel and the tracers did not line up). -1 keeps a sliver of the side.
        /// </summary>
        public const float ViewModelRifleYaw = -1f;

        /// <summary>The muzzle's tip down from the camera's forward (degrees); 0 keeps the barrel on the crosshair (the rabbit's SMG).</summary>
        public const float ViewModelRiflePitch = 0f;

        /// <summary>
        /// The fox's rifle's tip down from the camera's forward (degrees). 0: the barrel runs parallel to the view, so its
        /// line on screen runs into the crosshair and the shots fly straight out of it to the hit under the crosshair.
        /// At 6 the barrel pointed 6 deg under the crosshair and the tracers left it 20 deg off on screen; bending the
        /// tracers out of the barrel was rejected (2026-11-09, user: the shots fly straight from the barrel).
        /// </summary>
        public const float ViewModelHipPitch = 0f;

        /// <summary>The fox's rifle's turn from the camera's forward (degrees). 0 for the same reason; -1 (the rabbit's
        /// <see cref="ViewModelRifleYaw"/>) turns the barrel's line slightly off the tracers'.</summary>
        public const float ViewModelHipYaw = 0f;

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
