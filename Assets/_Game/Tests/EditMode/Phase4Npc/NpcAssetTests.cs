using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Input;
using CoopDeck.NPC;
using CoopDeck.Player;
using CoopDeck.Skills;
using CoopDeck.UI;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>Checks the generated NPC data, prefab and scene (run CoopDeck > Setup > Run Phase 1 Bootstrap first).</summary>
    public class NpcAssetTests
    {
        const string NpcPrefabPath = "Assets/_Game/Prefabs/Characters/PF_Npc_Volt.prefab";
        const string PlayerPrefabPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const string VoltDeckPath = "Assets/_Game/ScriptableObjects/Cards/Decks/DD_Npc_Volt.asset";
        const string StarterDeckPath = "Assets/_Game/ScriptableObjects/Cards/Decks/DD_Starter.asset";

        static GameObject NpcPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NpcPrefabPath);
            Assert.That(prefab, Is.Not.Null, "NPC prefab missing: run the bootstrap");
            return prefab;
        }

        [Test]
        public void NpcDeck_HasEightCards_TwoSlots_AndUsesTheSharedCardPool()
        {
            var deck = AssetDatabase.LoadAssetAtPath<DeckDefinition>(VoltDeckPath);
            Assert.That(deck, Is.Not.Null);
            Assert.That(deck.cards.Count, Is.EqualTo(8));
            Assert.That(deck.handSize, Is.EqualTo(2));
            Assert.That(deck.cards.All(c => c != null && c.type == CardType.Active && Mathf.Approximately(c.maxGauge, 100f)), Is.True);
            Assert.That(deck.cards.Count(c => c.element == Element.Lightning), Is.GreaterThanOrEqualTo(4), "a lightning specialist");
            Assert.That(deck.cards.Any(c => c.element == Element.Fire), Is.True, "can start overload for the player");

            // Same CardDefinition assets as the player's deck (no separate NPC card system).
            var starter = AssetDatabase.LoadAssetAtPath<DeckDefinition>(StarterDeckPath);
            Assert.That(deck.cards.Intersect(starter.cards).Any(), Is.True);
            Assert.That(starter.cards.Count, Is.EqualTo(13), "the player's starter deck (12 cards and the fireball) is unchanged by the NPC");
            Assert.That(deck.cards.Any(c => c.cardId == "card.fireball"), Is.False, "the fireball is the fox's skill, not the NPC's");
        }

        [Test]
        public void NpcPrefab_IsTheSameCombatantAsThePlayer_DrivenByTheAiController()
        {
            GameObject npc = NpcPrefab();
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            System.Type[] shared =
            {
                typeof(CharacterController), typeof(FpsMotor), typeof(FpsLook), typeof(FpsCharacter), typeof(Health), typeof(CombatStatus),
                typeof(HitscanWeapon), typeof(SkillCaster), typeof(DeckController), typeof(Revivable), typeof(AllyCollisionFilter),
            };
            foreach (System.Type type in shared)
            {
                Assert.That(npc.GetComponent(type), Is.Not.Null, "NPC " + type.Name);
                Assert.That(player.GetComponent(type), Is.Not.Null, "player " + type.Name);
            }

            Assert.That(npc.GetComponent<NpcController>(), Is.Not.Null, "AI controller");
            Assert.That(npc.GetComponent<PlayerInputReader>(), Is.Null, "never reads devices");
            Assert.That(npc.GetComponentInChildren<Camera>(), Is.Null);
            Assert.That(npc.GetComponent<DeckLoadoutController>(), Is.Not.Null);
            Assert.That(npc.GetComponent<Health>().Team, Is.EqualTo(TeamId.Players));
            Assert.That(npc.layer, Is.EqualTo(GameLayers.PlayerIndex));

            var so = new SerializedObject(npc.GetComponent<FpsCharacter>());
            Assert.That(so.FindProperty("commandSourceComponent").objectReferenceValue, Is.SameAs(npc.GetComponent<NpcController>()));
            var deck = new SerializedObject(npc.GetComponent<DeckController>());
            Assert.That(deck.FindProperty("startingDeck").objectReferenceValue, Is.SameAs(AssetDatabase.LoadAssetAtPath<DeckDefinition>(VoltDeckPath)));
        }

        [Test]
        public void NpcLooksDifferentFromPlayerAndEnemies()
        {
            GameObject npc = NpcPrefab();
            var npcMaterials = npc.GetComponentsInChildren<Renderer>().Select(r => r.sharedMaterial).Where(m => m != null).ToList();
            Assert.That(npcMaterials.Any(m => m.name == "M_Npc_Body"), Is.True);
            Assert.That(npcMaterials.Any(m => m.name == "M_Player_Body" || m.name == "M_Enemy_Body"), Is.False);
            Assert.That(npc.GetComponentsInChildren<Renderer>().Any(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly), Is.False,
                "the NPC body is visible (only the local player's body is shadow-only)");
        }

        [Test]
        public void SandboxScene_HasTheNpcWiredToThePlayerAndCombos()
        {
            var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/Sandbox_Phase1.unity", OpenSceneMode.Additive);
            try
            {
                NpcController npc = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NpcController>()).FirstOrDefault();
                Assert.That(npc, Is.Not.Null, "NPC in the scene");
                var so = new SerializedObject(npc);
                Assert.That(so.FindProperty("leader").objectReferenceValue, Is.Not.Null);
                Assert.That(so.FindProperty("comboSystem").objectReferenceValue, Is.Not.Null);
                Assert.That(new SerializedObject(npc.GetComponent<DeckLoadoutController>()).FindProperty("engagement").objectReferenceValue, Is.Not.Null);

                GameObject[] roots = scene.GetRootGameObjects();
                Assert.That(roots.Any(g => g.GetComponentInChildren<NpcHud>() != null), Is.True);
                Assert.That(roots.Any(g => g.GetComponentInChildren<NpcDeckEditorHud>() != null), Is.True);
                Assert.That(roots.Any(g => g.GetComponentInChildren<ComboAudio>() != null), Is.True);
                Assert.That(roots.Any(g => g.GetComponentInChildren<TeamEngagementMonitor>() != null), Is.True);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void Combos_HaveCoopPresentation_WithQualityGatedLayers()
        {
            string[] guids = AssetDatabase.FindAssets("t:ComboDefinition", new[] { "Assets/_Game/ScriptableObjects/Combos" });
            Assert.That(guids.Length, Is.EqualTo(3));
            foreach (string guid in guids)
            {
                var combo = AssetDatabase.LoadAssetAtPath<ComboDefinition>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(combo.coopMultiplier, Is.EqualTo(1.3f).Within(1e-4f), combo.name);
                Assert.That(combo.coopFx, Is.Not.Null, combo.name + " co-op effect");
                Assert.That(combo.sound, Is.Not.Null, combo.name + " sound");
                Assert.That(combo.coopSound, Is.Not.Null, combo.name + " co-op sound");
                Assert.That(combo.coopSound, Is.Not.SameAs(combo.sound));
                Assert.That(combo.window, Is.EqualTo(4f));
                Assert.That(combo.cooldown, Is.EqualTo(3f));
                var gates = combo.coopFx.GetComponentsInChildren<CoopDeck.Effects.VfxQualityGate>(true);
                Assert.That(gates.Any(g => g.MinimumQuality == VfxQuality.High), Is.True, "extra layers only at high quality");
                Assert.That(gates.Any(g => g.MinimumQuality == VfxQuality.Medium), Is.True);
            }
        }

        [Test]
        public void OrderControls_AreBound_ForKeyboardAndGamepad_WithoutTakingExistingKeys()
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.InputSystem.InputActionAsset>("Assets/_Game/Settings/Input/GameControls.inputactions");
            Assert.That(asset, Is.Not.Null);
            var tactic = asset.FindAction($"{GameInputActions.PlayerMap}/{GameInputActions.OrderTactic}");
            var mark = asset.FindAction($"{GameInputActions.PlayerMap}/{GameInputActions.OrderMark}");
            Assert.That(tactic, Is.Not.Null);
            Assert.That(mark, Is.Not.Null);
            Assert.That(tactic.bindings.Select(b => b.path), Is.SupersetOf(new[] { "<Keyboard>/v", "<Gamepad>/rightStickPress" }));
            Assert.That(mark.bindings.Select(b => b.path), Is.SupersetOf(new[] { "<Mouse>/middleButton", "<Keyboard>/x", "<Gamepad>/leftTrigger" }));

            var used = asset.FindActionMap(GameInputActions.PlayerMap).actions
                .Where(a => a.name != GameInputActions.OrderTactic && a.name != GameInputActions.OrderMark)
                .SelectMany(a => a.bindings).Select(b => b.path).ToList();
            foreach (string path in tactic.bindings.Concat(mark.bindings).Select(b => b.path))
            {
                Assert.That(used, Does.Not.Contain(path), path + " is already used by another action");
            }
        }

        [Test]
        public void ThunderboltCard_IsAnExtraCard_NotInThePlayersStarterDeck()
        {
            var bolt = AssetDatabase.LoadAssetAtPath<CardDefinition>("Assets/_Game/ScriptableObjects/Cards/CD_Thunderbolt.asset");
            Assert.That(bolt, Is.Not.Null);
            Assert.That(bolt.element, Is.EqualTo(Element.Lightning));
            Assert.That(bolt.targeting, Is.EqualTo(CardTargeting.Aim));
            Assert.That(bolt.icon, Is.Not.Null);
            Assert.That(bolt.impactFx, Is.Not.Null);
            var starter = AssetDatabase.LoadAssetAtPath<DeckDefinition>(StarterDeckPath);
            Assert.That(starter.cards.Contains(bolt), Is.False);
        }
    }
}
