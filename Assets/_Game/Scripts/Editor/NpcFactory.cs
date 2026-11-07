using System.Collections.Generic;
using CoopDeck.Animation;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.NPC;
using CoopDeck.Player;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    public sealed class NpcAssetSet
    {
        public GameObject Prefab;
        public NpcProfile Profile;
        public WeaponDefinition Weapon;
        public List<DeckDefinition> Decks;
        public List<CardDefinition> OwnedCards;
    }

    /// <summary>
    /// The NPC ally "Volt" (lightning): profile, carbine, and the prefab. The body is the same
    /// combatant as the player (CharacterController + FpsMotor + FpsLook + HitscanWeapon +
    /// SkillCaster + DeckController + Health) with the AI controller as its command source and a
    /// visible third-person placeholder rig in gold / navy with a cyan visor, so player (blue),
    /// enemies (red / violet) and the ally read apart at a glance. Data assets keep their tuning.
    /// </summary>
    public static class NpcFactory
    {
        public const string PrefabPath = GamePaths.CharacterPrefabs + "/PF_Npc_Volt.prefab";
        public const string ProfilePath = GamePaths.CharacterData + "/NP_Volt.asset";
        public const string WeaponPath = GamePaths.WeaponData + "/WD_NpcCarbine.asset";
        const float EyeHeight = 1.55f;

        public static NpcAssetSet EnsureNpc(MovementProfile movement, WeaponDefinition rifle, List<CardDefinition> allCards,
            List<DeckDefinition> npcDecks, RuntimeAnimatorController bodyController)
        {
            var set = new NpcAssetSet
            {
                Profile = EnsureProfile(),
                Weapon = EnsureWeapon(rifle),
                Decks = npcDecks,
                OwnedCards = allCards,
            };

            Material body = Lit("M_Npc_Body", new Color(1f, 0.78f, 0.2f), 0.35f, null);
            Material limbs = Lit("M_Npc_Limbs", new Color(0.13f, 0.15f, 0.26f), 0.3f, null);
            Material head = Lit("M_Npc_Head", new Color(0.95f, 0.95f, 1f), 0.4f, null);
            Material visor = Lit("M_Npc_Visor", new Color(0.3f, 1f, 1f), 0.6f, new Color(0.5f, 2.6f, 3f));
            Material glow = Lit("M_Npc_Glow", new Color(1f, 0.9f, 0.3f), 0.5f, new Color(3f, 2.4f, 0.5f));
            Material gun = AssetDatabase.LoadAssetAtPath<Material>(GamePaths.WeaponMaterials + "/M_Rifle_Body.mat");

            set.Prefab = BuildPrefab(set, movement, body, limbs, head, visor, glow, gun, bodyController);
            return set;
        }

        static NpcProfile EnsureProfile()
        {
            EditorUtil.EnsureFolder(GamePaths.CharacterData);
            var profile = AssetDatabase.LoadAssetAtPath<NpcProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<NpcProfile>();
                profile.displayName = "ヴォルト";
                profile.favoredElement = Element.Lightning;
                profile.color = new Color(1f, 0.86f, 0.3f);
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }

            return profile;
        }

        static WeaponDefinition EnsureWeapon(WeaponDefinition rifle)
        {
            EditorUtil.EnsureFolder(GamePaths.WeaponData);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            if (weapon == null)
            {
                weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
                weapon.weaponId = "weapon.npc_carbine";
                weapon.displayName = "ボルトカービン";
                // A bit weaker than the player's rifle: the NPC supports, the player leads.
                weapon.damage = 11f;
                weapon.roundsPerMinute = 540f;
                weapon.magazineSize = 30;
                weapon.reloadTime = 1.9f;
                weapon.baseSpread = 0.35f;
                weapon.spreadPerShot = 0.15f;
                weapon.maxBloom = 1.6f;
                weapon.recoilPitch = 0.35f;
                weapon.recoilYaw = 0.15f;
                AssetDatabase.CreateAsset(weapon, WeaponPath);
            }

            if (rifle != null)
            {
                if (weapon.muzzleFlashPrefab == null) weapon.muzzleFlashPrefab = rifle.muzzleFlashPrefab;
                if (weapon.tracerPrefab == null) weapon.tracerPrefab = rifle.tracerPrefab;
                if (weapon.impactPrefab == null) weapon.impactPrefab = rifle.impactPrefab;
                if (weapon.damageableImpactPrefab == null) weapon.damageableImpactPrefab = rifle.damageableImpactPrefab;
            }

            EditorUtility.SetDirty(weapon);
            return weapon;
        }

        static Material Lit(string name, Color color, float smoothness, Color? emission)
        {
            EditorUtil.EnsureFolder(GamePaths.CharacterMaterials);
            string path = $"{GamePaths.CharacterMaterials}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            material.SetFloat("_Metallic", 0f);
            if (emission.HasValue)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", emission.Value);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }

            EditorUtility.SetDirty(material);
            return material;
        }

        static GameObject BuildPrefab(NpcAssetSet set, MovementProfile movement, Material body, Material limbs, Material head,
            Material visor, Material glow, Material gunMaterial, RuntimeAnimatorController bodyController)
        {
            EditorUtil.EnsureFolder(GamePaths.CharacterPrefabs);
            var root = new GameObject("Npc_Volt");

            var controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.38f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.4f;
            controller.slopeLimit = 50f;
            controller.skinWidth = 0.06f;
            controller.minMoveDistance = 0f;

            var health = root.AddComponent<Health>();
            EditorUtil.SetFloat(health, "maxHealth", 130f);
            EditorUtil.SetInt(health, "team", (int)TeamId.Players);

            var motor = root.AddComponent<FpsMotor>();
            EditorUtil.SetObject(motor, "profile", movement);
            var look = root.AddComponent<FpsLook>();
            var hitscan = root.AddComponent<HitscanWeapon>();
            EditorUtil.SetObject(hitscan, "definition", set.Weapon);
            var weaponFx = root.AddComponent<WeaponFx>();
            EditorUtil.SetObject(weaponFx, "weapon", hitscan);
            EditorUtil.SetInt(weaponFx, "muzzleFlashLayer", -1);
            EditorUtil.SetInt(weaponFx, "prewarmCount", 3);
            root.AddComponent<CombatStatus>();
            var revivable = root.AddComponent<Revivable>();
            EditorUtil.SetFloat(revivable, "reviveTime", 2.5f);
            EditorUtil.SetFloat(revivable, "bleedOutTime", 25f);
            root.AddComponent<AllyCollisionFilter>();
            var caster = root.AddComponent<SkillCaster>();
            var npc = root.AddComponent<NpcController>();
            EditorUtil.SetObject(npc, "profile", set.Profile);
            var character = root.AddComponent<FpsCharacter>();
            EditorUtil.SetString(character, "displayName", set.Profile.displayName);
            EditorUtil.SetObject(character, "commandSourceComponent", npc);
            EditorUtil.SetObject(npc, "character", character);

            DeckDefinition startDeck = set.Decks != null && set.Decks.Count > 0 ? set.Decks[0] : null;
            var deck = root.AddComponent<DeckController>();
            EditorUtil.SetObject(deck, "startingDeck", startDeck);
            EditorUtil.SetObject(deck, "character", character);
            EditorUtil.SetObject(deck, "caster", caster);

            var loadout = root.AddComponent<DeckLoadoutController>();
            EditorUtil.SetObject(loadout, "deckController", deck);
            EditorUtil.SetObject(loadout, "initialDeck", startDeck);
            var so = new SerializedObject(loadout);
            SerializedProperty owned = so.FindProperty("ownedCards");
            owned.arraySize = set.OwnedCards.Count;
            for (int i = 0; i < set.OwnedCards.Count; i++)
            {
                SerializedProperty entry = owned.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("card").objectReferenceValue = set.OwnedCards[i];
                entry.FindPropertyRelative("copies").intValue = set.OwnedCards[i].cardId == "card.thunderbolt" ? 3 : 2;
            }

            SerializedProperty presets = so.FindProperty("presets");
            presets.arraySize = set.Decks.Count;
            for (int i = 0; i < set.Decks.Count; i++)
            {
                presets.GetArrayElementAtIndex(i).objectReferenceValue = set.Decks[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            // Visible third-person body (the NPC is seen by the player, unlike the player's own shadow body).
            RigParts rig = PlaceholderRigBuilder.Build(root.transform, body, limbs, head, visor, shadowsOnly: false);
            rig.Visual.name = "BodyVisual";
            EditorUtil.VisualPrimitive(PrimitiveType.Cube, "PowerCell", rig.Spine, new Vector3(0f, 0.34f, -0.2f), new Vector3(0.24f, 0.32f, 0.12f), glow);
            EditorUtil.VisualPrimitive(PrimitiveType.Cube, "ShoulderPad_L", rig.ShoulderL, new Vector3(-0.03f, 0.04f, 0f), new Vector3(0.18f, 0.08f, 0.2f), body);
            EditorUtil.VisualPrimitive(PrimitiveType.Cube, "ShoulderPad_R", rig.ShoulderR, new Vector3(0.03f, 0.04f, 0f), new Vector3(0.18f, 0.08f, 0.2f), body);
            EditorUtil.VisualPrimitive(PrimitiveType.Cylinder, "HaloRing", rig.Head, new Vector3(0f, 0.5f, 0f), new Vector3(0.42f, 0.008f, 0.42f), glow, castShadows: false);
            var animator = rig.Visual.gameObject.AddComponent<Animator>();
            animator.runtimeAnimatorController = bodyController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var driver = root.AddComponent<CharacterAnimatorDriver>();
            driver.Configure(animator, motor, movement != null ? movement.walkSpeed : 6.5f, 4f);
            EditorUtil.SetObject(character, "bodyAnimator", driver);

            // Aim pivot at eye height: the look pitches it; the carbine hangs from it so it points where the NPC aims.
            var pivot = new GameObject("AimPivot");
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, EyeHeight, 0f);
            var gun = new GameObject("Carbine");
            gun.transform.SetParent(pivot.transform, false);
            gun.transform.localPosition = new Vector3(0.24f, -0.32f, 0.32f);
            Material gunBody = gunMaterial != null ? gunMaterial : limbs;
            EditorUtil.VisualPrimitive(PrimitiveType.Cube, "Receiver", gun.transform, Vector3.zero, new Vector3(0.07f, 0.1f, 0.46f), gunBody);
            EditorUtil.VisualPrimitive(PrimitiveType.Cube, "Glow", gun.transform, new Vector3(0.04f, 0.01f, 0.06f), new Vector3(0.008f, 0.02f, 0.3f), glow, castShadows: false);
            EditorUtil.VisualPrimitive(PrimitiveType.Cylinder, "Barrel", gun.transform, new Vector3(0f, 0.01f, 0.32f), new Vector3(0.03f, 0.09f, 0.03f), limbs, new Vector3(90f, 0f, 0f));
            var muzzle = new GameObject("Muzzle");
            muzzle.transform.SetParent(gun.transform, false);
            muzzle.transform.localPosition = new Vector3(0f, 0.01f, 0.42f);

            EditorUtil.SetObject(look, "yawRoot", root.transform);
            EditorUtil.SetObject(look, "pitchPivot", pivot.transform);
            EditorUtil.SetObject(hitscan, "muzzle", muzzle.transform);
            EditorUtil.SetObject(caster, "castOrigin", muzzle.transform);
            EditorUtil.SetObject(character, "motor", motor);
            EditorUtil.SetObject(character, "look", look);
            EditorUtil.SetObject(character, "health", health);
            EditorUtil.SetObject(character, "weapon", hitscan);

            EditorUtil.SetLayerRecursively(root, GameLayers.PlayerIndex);
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
            {
                renderer.shadowCastingMode = renderer.name == "HaloRing" || renderer.name == "Glow" ? ShadowCastingMode.Off : ShadowCastingMode.On;
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }
    }
}
