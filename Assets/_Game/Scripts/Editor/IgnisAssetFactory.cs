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
    /// The Fox Gunner's IGNIS-27 Blazetail assault rifle (Docs/IGNIS27_RIFLE.md): its weapon data converted
    /// from the design JSON (Docs/Design/IGNIS27_Blazetail_WeaponData.json), its short warm fire effects (muzzle
    /// flame, tracer, ember impacts, embers at the muzzle while the brand builds, the compact ignition burst)
    /// and the assignment as the fox's starting weapon. The fox keeps its Fire Rifle model, prefab and
    /// animations; only the weapon data changes. Effects are rebuilt every run; the weapon's numbers are written
    /// only when the asset is created, so Inspector tuning is kept (like the other weapon data). The fox's
    /// previous rifle (WD_AssaultRifle, プロトAR) is not touched.
    /// </summary>
    public static class IgnisAssetFactory
    {
        public const string WeaponPath = GamePaths.WeaponData + "/WD_Ignis27Blazetail.asset";
        public const string FxFolder = GamePaths.VfxPrefabs + "/Ignis";
        public const string FoxId = "FoxGunner";

        /// <summary>
        /// Burn put on every target of the ignition. The design JSON asks for the existing burn ("use existing burn
        /// status behaviour", no second damage over time) but gives no damage figure: this is the fireball card's
        /// burn rate (6/s, the mildest fire card), for the JSON's 4 s.
        /// </summary>
        public const float BurnDamagePerSecond = 6f;

        // Design JSON visuals: accents #F5953D (orange) / #FFD578 (warm yellow), red panels #BA4A37.
        static readonly Color Orange = Palette.Hex(0xF5953D);
        static readonly Color Gold = Palette.Hex(0xFFD578);
        static readonly Color Ember = Palette.Hex(0xBA4A37);

        public static WeaponDefinition EnsureAll()
        {
            EditorUtil.EnsureFolder(FxFolder);
            Material core = GameAssetFactory.Fx("M_FX_Ignis_Core", Gold, 3f, 1f, 1.8f);
            Material ember = GameAssetFactory.Fx("M_FX_Ignis_Ember", Gold, 3.4f, 1f, 1.2f);
            Material puff = GameAssetFactory.Fx("M_FX_Ignis_Puff", Orange, 1.8f, 1f, 1.1f);

            // Muzzle, tracer and impacts at the fireball's quality bar (Docs/WEAPON_SKILL_VFX.md §2).
            WeaponVfxSet shots = WeaponVfxFactory.EnsureIgnis();
            GameObject muzzle = shots.Muzzle;
            GameObject tracer = shots.Tracer;
            GameObject impact = shots.Impact;
            GameObject impactHit = shots.ImpactHit;
            GameObject charge = FxPrefabBuilder.EnsureIn(FxFolder, "FX_Ignis_Charge", true, () => BuildCharge(ember));
            GameObject burst = FxPrefabBuilder.EnsureIn(FxFolder, "FX_Ignis_Ignition", true, () => BuildIgnition("FX_Ignis_Ignition", core, puff, ember, 1f));
            GameObject splash = FxPrefabBuilder.EnsureIn(FxFolder, "FX_Ignis_Ignition_Splash", true, () => BuildIgnition("FX_Ignis_Ignition_Splash", core, puff, ember, 0.55f));

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
            weapon.chargeBurst.burstFx = burst;
            weapon.chargeBurst.chainBurstFx = splash;
            weapon.chargeBurst.arcFx = null; // no bolt: the burst sits on the target
            EditorUtility.SetDirty(weapon);
            return weapon;
        }

        /// <summary>
        /// The design JSON (schema 1, status proposal_requires_playtest) in this project's weapon format, with the
        /// same conversions as the VOLT-9 (Docs/THUNDERHOP_SMG.md §2). Fields the game has no system for
        /// (reserve ammo, equip time, ADS, movement multipliers, audio, reload glow) are left out
        /// (Docs/IGNIS27_RIFLE.md §2). Values the JSON does not give keep the fox's previous rifle's (プロトAR).
        /// </summary>
        public static void ApplyDesignValues(WeaponDefinition w)
        {
            w.weaponId = "weapon.ignis27_blazetail";
            w.displayName = "IGNIS-27 ブレイズテイル";
            w.categoryLabel = "アサルトライフル";
            w.description = "炎属性キツネ専用の中距離アサルトライフル。精密射撃で火種を蓄積し、連続命中で起爆する。";

            w.damage = 18f;                     // baseDamagePerBullet
            w.element = Element.Fire;           // HUD and damage colour; bullets give no element (the ignition does)
            w.headshotMultiplier = 1.65f;      // weakPointDamageMultiplier
            w.range = 85f;
            w.falloffStart = 24f;
            w.falloffEnd = 60f;
            w.minDamageFactor = 0.7f;

            w.roundsPerMinute = 480f;
            w.automatic = true;                 // fireMode full_auto
            w.magazineSize = 28;
            w.reloadTime = 2.1f;
            w.pelletsPerShot = 1;               // hitModel hitscan

            // No ADS, and the fight is from the hip with no spread (user, 2026-11-08): the JSON's hip-fire cone
            // (1.75°) is 0, so is its moving multiple (x1.40 of 0); the ADS cone (0.40°) is not used. Bullets go
            // where the crosshair is; recoil is what has to be controlled.
            w.baseSpread = 0f;
            w.spreadPerShot = 0f;
            w.maxBloom = 0f;
            w.bloomRecovery = 0f;
            w.movingSpread = 0f;
            w.airborneSpread = 1.4f;            // not in the JSON: プロトAR's (jumping is a separate rule from hip fire)

            w.recoilPitch = 1.05f;              // verticalDegreesPerShot
            w.recoilYaw = 0.35f;                // horizontalMaxDegreesPerShot
            w.firstShotRecoilMultiplier = 1.3f; // not in the JSON: プロトAR's
            w.recoilRecoverySeconds = 0.2f;

            w.chargeBurst = new ChargeBurstSpec
            {
                enabled = true,
                displayName = "火種刻印",
                requiredStacks = 6,
                maxSecondsBetweenHits = 2.25f,
                stacksPerHit = 1,                // stacksPerDirectHit; the ignition's own hits never add stacks
                primaryDamage = 22f,             // bonusDamageToPrimaryTarget
                shape = ChargeBurstShape.Splash,
                splashRadius = 2.5f,
                splashTargets = 4,
                splashDamage = 12f,              // damagePerSplashTarget
                element = Element.Fire,          // fire aura 4 s and the 3 s combo cooldown come from the combo system
                statusDamagePerSecond = BurnDamagePerSecond, // the existing burn (CombatStatus), see BurnDamagePerSecond
                statusDuration = 4f,             // statusDurationSeconds
                perTargetCooldown = 4f,          // cooldownPerTargetSeconds
            };
        }

        /// <summary>Makes the IGNIS-27 the fox's starting weapon (called after the character definitions are generated).</summary>
        public static void AssignToFox(CharacterDefinition[] characters, WeaponDefinition weapon)
        {
            if (characters == null)
            {
                return;
            }

            foreach (CharacterDefinition c in characters)
            {
                if (c != null && c.Id == FoxId)
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

            AssignToFox(characters, weapon);
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

        /// <summary>The brand building up on the gun: a couple of embers drifting off the muzzle.</summary>
        static GameObject BuildCharge(Material ember)
        {
            GameObject root = Root("FX_Ignis_Charge", 0.4f);
            ParticleSystem glow = FxPrefabBuilder.AddSystem(root, "Embers", ember);
            FxPrefabBuilder.Configure(glow, (0.2f, 0.35f), (0.2f, 0.6f), (0.01f, 0.02f), Gold, Ember, 2, 8);
            FxPrefabBuilder.SetShape(glow, ParticleSystemShapeType.Sphere, 0.03f, 0f);
            ParticleSystem.MainModule main = glow.main;
            main.gravityModifier = -0.15f; // drift up like heat
            ParticleSystem.NoiseModule noise = glow.noise;
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 2f;
            return root;
        }

        /// <summary>
        /// Compact 3D fire burst with glowing fragments for the ignition (about the 2.5 m splash): a white-hot
        /// flash, a ball of flame puffs, fragments that fly out and fall, and a warm light blink (high quality only).
        /// The splash version is smaller and dimmer.
        /// </summary>
        static GameObject BuildIgnition(string name, Material core, Material puff, Material ember, float scale)
        {
            GameObject root = Root(name, 0.9f);
            ParticleSystem flash = FxPrefabBuilder.AddSystem(root, "Flash", core);
            FxPrefabBuilder.Configure(flash, (0.06f, 0.09f), (0f, 0f), (0.9f * scale, 1.3f * scale), Color.white, Gold, 1, 2);
            FxPrefabBuilder.SetShape(flash, ParticleSystemShapeType.Sphere, 0.001f, 0f);

            ParticleSystem fire = FxPrefabBuilder.AddSystem(root, "Fireball", puff);
            FxPrefabBuilder.Configure(fire, (0.22f, 0.4f), (1.2f * scale, 3.2f * scale), (0.45f * scale, 0.9f * scale), Gold, Ember,
                (short)Mathf.RoundToInt(10 * scale + 4), 24);
            FxPrefabBuilder.SetShape(fire, ParticleSystemShapeType.Sphere, 0.25f * scale, 0f);
            ParticleSystem.MainModule fireMain = fire.main;
            fireMain.gravityModifier = -0.2f;

            ParticleSystem fragments = FxPrefabBuilder.AddSystem(root, "Fragments", ember);
            FxPrefabBuilder.Configure(fragments, (0.35f, 0.7f), (3f * scale, 7f * scale), (0.025f, 0.05f), Gold, Orange,
                (short)Mathf.RoundToInt(14 * scale + 2), 32);
            FxPrefabBuilder.SetShape(fragments, ParticleSystemShapeType.Sphere, 0.15f, 0f);
            FxPrefabBuilder.Stretch(fragments, 0.03f);
            ParticleSystem.MainModule fragMain = fragments.main;
            fragMain.gravityModifier = 1.2f;

            if (scale >= 1f)
            {
                var lightGo = new GameObject("Light");
                lightGo.transform.SetParent(root.transform, false);
                lightGo.AddComponent<VfxQualityGate>().MinimumQuality = VfxQuality.High;
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 5f;
                light.color = Orange;
                light.shadows = LightShadows.None;
                light.intensity = 0f;
                lightGo.AddComponent<LightFlash>();
            }

            return root;
        }
    }
}
