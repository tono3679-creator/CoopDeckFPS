using System;
using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Characters;
using CoopDeck.Items;
using CoopDeck.Player;
using CoopDeck.Weapons;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// One-click (or batch-mode) setup of everything Phase 1 needs. Safe to run repeatedly:
    /// data assets and the input asset are only created when missing, so tuning is preserved.
    /// Prefabs, effects and the sandbox scene are generated placeholders and are rebuilt.
    ///
    /// Batch mode:
    ///   Unity.exe -batchmode -quit -projectPath . -executeMethod CoopDeck.EditorTools.ProjectBootstrap.RunBatch
    /// </summary>
    public static class ProjectBootstrap
    {
        [MenuItem("CoopDeck/Setup/Run Phase 1 Bootstrap", priority = 0)]
        public static void RunFromMenu()
        {
            if (!EditorUtility.DisplayDialog("CoopDeck Bootstrap",
                    "プレイヤー/的/エフェクトのPrefabと Sandbox_Phase1 シーンを再生成します。\n" +
                    "データ資産（武器・移動・入力）は既存なら保持されます。続行しますか？", "実行", "キャンセル"))
            {
                return;
            }

            Run(overwriteData: false);
            EditorUtility.DisplayDialog("CoopDeck Bootstrap", "完了しました。Sandbox_Phase1 を開いて Play してください。", "OK");
        }

        public static void RunBatch()
        {
            try
            {
                bool overwriteData = Array.IndexOf(Environment.GetCommandLineArgs(), "-coopdeckOverwriteData") >= 0;
                Run(overwriteData);
                Debug.Log("[CoopDeck] Bootstrap finished successfully.");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void Run(bool overwriteData)
        {
            LayerSetup.EnsureLayers();
            RenderingSetup.Apply();
            RenderingSetup.ApplyPlayerSettings();

            InputActionAsset input = GameAssetFactory.EnsureInputActions(overwriteData);
            MaterialSet materials = GameAssetFactory.EnsureMaterials();
            FxPrefabSet fx = FxPrefabBuilder.EnsureFxPrefabs(materials, overwrite: true);
            SkillFxSet skillFx = FxPrefabBuilder.EnsureSkillFx(overwrite: true);
            WeaponDefinition rifle = GameAssetFactory.EnsureAssaultRifle(fx);
            MovementProfile movement = GameAssetFactory.EnsureMovementProfile();
            VolumeProfile volume = GameAssetFactory.EnsureSandboxVolumeProfile();

            List<CardDefinition> cards = CardDataFactory.EnsureCards(skillFx, overwriteIcons: false);
            DeckDefinition starterDeck = CardDataFactory.EnsureStarterDeck(cards);
            // Phase 5: the fireball (the fox's basic fire skill), its projectile effects, and the 13th starter card.
            FireballVfxSet fireballVfx = FireballVfxFactory.EnsureAll();
            CardDataFactory.EnsureInStarterDeck(starterDeck, CardDataFactory.EnsureFireball(fireballVfx, overwriteIcons: false));
            DeckDefinition practiceDeck = CardDataFactory.EnsurePracticeDeck(cards);
            List<GaugeItemDefinition> items = CardDataFactory.EnsureGaugeItems();
            ComboAssetSet combos = ComboAssetFactory.EnsureCombos();
            combos.Burning = fireballVfx.Burning; // shown by the scene's ElementAuraPresenter while a fire burn ticks

            // Phase 4: the NPC ally (its own 8-card deck from the same card pool, plus its finisher card).
            var allCards = new List<CardDefinition>(cards);
            allCards.AddRange(CardDataFactory.EnsureNpcCards(skillFx, overwriteIcons: false));
            List<DeckDefinition> npcDecks = CardDataFactory.EnsureNpcDecks(allCards);

            bool overwriteAnimations = Array.IndexOf(Environment.GetCommandLineArgs(), "-coopdeckOverwriteAnimations") >= 0;
            AnimationSet animations = AnimationAssetFactory.EnsureAnimations(overwriteAnimations);
            GameObject windupFx = FxPrefabBuilder.EnsureEnemyWindupFx(overwrite: true);
            EnemySet enemies = EnemyFactory.EnsureEnemies(materials, fx, skillFx, windupFx, animations);

            // The player's full-body model (Meshy → Blender → Unity, Docs/CHARACTER_PIPELINE.md). The NPC keeps the placeholder rig.
            FoxGunnerAssets fox = FoxGunnerAssetFactory.EnsureAll(overwriteAnimations);
            FireRifleAssets fireRifle = FireRifleAssetFactory.EnsureAll();
            // The second playable character: the Lightning Rabbit with its Lightning SMG (Docs/LIGHTNING_RABBIT.md). PF_Player keeps
            // the fox; the rabbit's kit replaces its body and first-person parts at start when selected (-character LightningRabbit).
            FireRifleAssets smg = LightningSMGAssetFactory.EnsureAll();
            LightningRabbitAssets rabbit = LightningRabbitAssetFactory.EnsureAll();
            CharacterKit rabbitKit = LightningRabbitAssetFactory.BuildKit(rabbit, smg);
            CharacterDefinition[] characters = LightningRabbitAssetFactory.EnsureDefinitions(rabbitKit);
            // The rabbit's own gun, the VOLT-9 Thunderhop (Docs/THUNDERHOP_SMG.md): data, electric effects, starting-weapon link.
            ThunderhopAssetFactory.AssignToRabbit(characters, ThunderhopAssetFactory.EnsureAll());
            IgnisAssetFactory.AssignToFox(characters, IgnisAssetFactory.EnsureAll()); // the fox's IGNIS-27 (Docs/IGNIS27_RIFLE.md)
            // Every starter card's own effect at the fireball's quality bar (Docs/WEAPON_SKILL_VFX.md §3); after the cards,
            // the fireball and the lightning, whose shaders, meshes and prefabs it reuses.
            SkillVfxFactory.EnsureAll();
            GameObject player = CharacterPrefabBuilder.BuildPlayer(input, rifle, movement, materials, starterDeck, items, fox.BodyPrefab, fireRifle,
                practiceDeck, characters);
            GameObject dummy = CharacterPrefabBuilder.BuildTargetDummy(materials, fx);
            NpcAssetSet npc = NpcFactory.EnsureNpc(movement, rifle, allCards, npcDecks, animations.Humanoid);
            SandboxSceneBuilder.Build(player, dummy, materials, volume, enemies, combos, npc.Prefab);
            // Phase 2.5: the networked player variant and the two-client smoke scene (second scene in the build).
            NetSmokeSceneBuilder.Build(player, materials, volume);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
