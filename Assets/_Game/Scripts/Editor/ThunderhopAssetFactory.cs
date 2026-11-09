using CoopDeck.Characters;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// The Lightning Rabbit's VOLT-9 Thunderhop SMG (Docs/THUNDERHOP_SMG.md): its weapon data
    /// converted from the design JSON, its short cyan electric effects (muzzle flash, tracer,
    /// impacts, muzzle crackle while charging; the discharge uses the shared stylized lightning from
    /// LightningVfxFactory, Docs/LIGHTNING_VFX.md), and the assignment as the
    /// rabbit's starting weapon. Effects are rebuilt every run; the weapon's numbers are written
    /// only when the asset is created, so Inspector tuning is kept (like the other weapon data).
    /// </summary>
    public static class ThunderhopAssetFactory
    {
        public const string WeaponPath = GamePaths.WeaponData + "/WD_Volt9Thunderhop.asset";
        public const string FxFolder = GamePaths.VfxPrefabs + "/Thunderhop";
        public const string RabbitId = "LightningRabbit";

        // Design JSON visuals: emissive #5ADCF4, light body #F4EFE7.
        static readonly Color Cyan = Palette.Hex(0x5ADCF4);
        static readonly Color CoreWhite = Palette.Hex(0xE8FBFF);

        public static WeaponDefinition EnsureAll()
        {
            EditorUtil.EnsureFolder(FxFolder);
            Material spark = GameAssetFactory.Fx("M_FX_Volt_Spark", CoreWhite, 2.6f, 1f, 1.2f);

            // Muzzle, tracer and impacts on the lightning graphs in the rabbit's cyan (Docs/WEAPON_SKILL_VFX.md §2).
            LightningVfxSet lightning = LightningVfxFactory.EnsureAll(); // its graphs colour the shot effects too
            WeaponVfxSet shots = WeaponVfxFactory.EnsureThunderhop();
            GameObject muzzle = shots.Muzzle;
            GameObject tracer = shots.Tracer;
            GameObject impact = shots.Impact;
            GameObject impactHit = shots.ImpactHit;
            GameObject charge = FxPrefabBuilder.EnsureIn(FxFolder, "FX_Thunderhop_Charge", true, () => BuildCharge(spark));

            EditorUtil.EnsureFolder(GamePaths.WeaponData);
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(WeaponPath);
            if (weapon == null)
            {
                weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
                ApplyDesignValues(weapon);
                AssetDatabase.CreateAsset(weapon, WeaponPath);
            }

            weapon.muzzleFlashPrefab = muzzle;
            weapon.tracerPrefab = tracer;
            weapon.impactPrefab = impact;
            weapon.damageableImpactPrefab = impactHit;
            weapon.chargeBurst.chargeFx = charge;
            // The discharge uses the shared stylized lightning (Docs/LIGHTNING_VFX.md): thick bolt + branches, big hit, smaller chained hits.
            weapon.chargeBurst.burstFx = lightning.Hit;
            weapon.chargeBurst.chainBurstFx = lightning.HitChain;
            weapon.chargeBurst.arcFx = lightning.Bolt;
            DeleteIfPresent(FxFolder + "/FX_Thunderhop_Burst.prefab"); // the first, thin version
            DeleteIfPresent(FxFolder + "/FX_Thunderhop_Arc.prefab");
            EditorUtility.SetDirty(weapon);
            return weapon;
        }

        /// <summary>
        /// The design JSON (ThunderHop_SMG_WeaponData.json, schema 1) in this project's weapon format.
        /// Fields the game has no system for (reserve ammo, ADS, equip time, movement multipliers) are
        /// converted or left out as documented in Docs/THUNDERHOP_SMG.md §2. Damage and vertical recoil carry the
        /// user-approved tuning (§5), not the JSON values.
        /// </summary>
        public static void ApplyDesignValues(WeaponDefinition w)
        {
            w.weaponId = "weapon.volt9_thunderhop";
            w.displayName = "VOLT-9 サンダーホップ";
            w.categoryLabel = "サブマシンガン";
            w.description = "雷ウサギ専用の高連射SMG。連続命中で帯電を蓄積し、電撃を放つ。";

            w.damage = 11f;                   // JSON 8; raised on 2026-11-08 (user) to match the fox rifle up close
            w.element = Element.Lightning;
            w.headshotMultiplier = 1.5f;     // weakPointDamageMultiplier
            w.range = 55f;
            w.falloffStart = 10f;
            w.falloffEnd = 24f;
            w.minDamageFactor = 0.6f;

            w.roundsPerMinute = 900f;
            w.automatic = true;               // fireMode full_auto
            w.magazineSize = 40;
            w.reloadTime = 1.65f;
            w.pelletsPerShot = 1;             // hitModel hitscan

            // User decision 2026-11-08: no ADS, hip fire has no spread, fights are hip fire (JSON ADS 0.65° / hip 2.4° /
            // moving x1.25 are not used). Bullets go straight to the crosshair; only recoil moves the aim. Jumping keeps
            // its own spread (a separate rule, not in the JSON), as on IGNIS-27.
            w.baseSpread = 0f;
            w.spreadPerShot = 0f;
            w.maxBloom = 0f;
            w.bloomRecovery = 2f;
            w.movingSpread = 0f;
            w.airborneSpread = 1f;

            w.recoilPitch = 0.5f;              // JSON 0.8; lowered on 2026-11-08 (user): 0.8 climbed off a 9 m target
            w.recoilYaw = 0.3f;
            w.firstShotRecoilMultiplier = 1f;
            w.recoilRecoverySeconds = 0.16f;

            w.chargeBurst = new ChargeBurstSpec
            {
                enabled = true,
                displayName = "帯電連射",
                requiredStacks = 12,
                maxSecondsBetweenHits = 2f,
                stacksPerHit = 1,
                primaryDamage = 18f,
                chainTargets = 1,
                chainRange = 6f,
                chainDamage = 12f,
                element = Element.Lightning,  // aura 4 s and combo cooldown 3 s come from the combo system
                perTargetCooldown = 1.25f,
            };
        }

        /// <summary>Makes the Thunderhop the rabbit's starting weapon (called after the character definitions are generated).</summary>
        public static void AssignToRabbit(CharacterDefinition[] characters, WeaponDefinition weapon)
        {
            if (characters == null)
            {
                return;
            }

            foreach (CharacterDefinition c in characters)
            {
                if (c != null && c.Id == RabbitId)
                {
                    c.SetStartingWeapon(weapon);
                    EditorUtility.SetDirty(c);
                }
            }
        }

        /// <summary>Without the full bootstrap: the weapon, its effects, and the assignment on the existing character definitions.</summary>
        public static string RunStandalone()
        {
            WeaponDefinition weapon = EnsureAll();
            string[] guids = AssetDatabase.FindAssets("t:CharacterDefinition", new[] { GamePaths.CharacterData });
            var characters = new CharacterDefinition[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                characters[i] = AssetDatabase.LoadAssetAtPath<CharacterDefinition>(AssetDatabase.GUIDToAssetPath(guids[i]));
            }

            AssignToRabbit(characters, weapon);
            AssetDatabase.SaveAssets();
            return $"{weapon.displayName}: {characters.Length} character definitions checked";
        }

        // ------------------------------------------------------------ effects

        static GameObject Root(string name, float lifetime)
        {
            var root = new GameObject(name);
            root.AddComponent<PooledFx>().Lifetime = lifetime;
            return root;
        }

        /// <summary>The charge feedback on the gun: a couple of tiny jittering sparks at the muzzle.</summary>
        static GameObject BuildCharge(Material spark)
        {
            GameObject root = Root("FX_Thunderhop_Charge", 0.14f);
            ParticleSystem crackle = FxPrefabBuilder.AddSystem(root, "Crackle", spark);
            FxPrefabBuilder.Configure(crackle, (0.05f, 0.11f), (0.6f, 2.2f), (0.012f, 0.026f), CoreWhite, Cyan, 3, 8);
            FxPrefabBuilder.SetShape(crackle, ParticleSystemShapeType.Sphere, 0.035f, 0f);
            FxPrefabBuilder.Stretch(crackle, 0.03f);
            ParticleSystem.NoiseModule noise = crackle.noise;
            noise.enabled = true;
            noise.strength = 2.5f;
            noise.frequency = 3f;
            return root;
        }

        static void DeleteIfPresent(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
