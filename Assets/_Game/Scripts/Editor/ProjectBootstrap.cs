using System;
using System.Collections.Generic;
using CoopDeck.Cards;
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
            List<GaugeItemDefinition> items = CardDataFactory.EnsureGaugeItems();

            GameObject player = CharacterPrefabBuilder.BuildPlayer(input, rifle, movement, materials, starterDeck, items);
            GameObject dummy = CharacterPrefabBuilder.BuildTargetDummy(materials, fx);
            SandboxSceneBuilder.Build(player, dummy, materials, volume);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}
