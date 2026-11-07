using System;
using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.Items;
using CoopDeck.Player;
using UnityEngine;

namespace CoopDeck.Skills
{
    /// <summary>
    /// Connects a character's skill-slot / item / selection commands to its <see cref="DeckState"/>,
    /// <see cref="GaugeItemInventory"/> and <see cref="SkillCaster"/>. It reacts to
    /// <see cref="FpsCharacter"/> events only, so humans, NPCs and (later) network proxies use the
    /// same path. The selected slot follows the last card used and is the Charge Potion target.
    /// </summary>
    [DisallowMultipleComponent]
    public class DeckController : MonoBehaviour
    {
        [SerializeField] DeckDefinition startingDeck;
        [Tooltip("Optional: the same cards arranged for trying element combos (F7 debug key). Same deck rules.")]
        [SerializeField] DeckDefinition practiceDeck;
        [SerializeField] List<GaugeItemDefinition> startingItems = new List<GaugeItemDefinition>();
        [Tooltip("0 = a different shuffle every run. Any other value gives a repeatable deck order.")]
        [SerializeField] int randomSeed;
        [SerializeField] FpsCharacter character;
        [SerializeField] SkillCaster caster;

        bool subscribed;

        public DeckState Deck { get; private set; }
        public GaugeItemInventory Items { get; private set; }
        public int SelectedSlot { get; private set; }
        public int Seed { get; private set; }
        public SkillCaster Caster => caster;
        public FpsCharacter Character => character;
        public DeckDefinition StartingDeck => startingDeck;
        public IReadOnlyList<GaugeItemDefinition> StartingItems => startingItems;

        /// <summary>Times the deck was rebuilt (start of run, practice deck, loadout edits).</summary>
        public int RebuildCount { get; private set; }

        /// <summary>A new deck/inventory was built (bind UI again).</summary>
        public event Action DeckRebuilt;

        public event Action<int> SelectionChanged;

        /// <summary>A card was used and its skill cast.</summary>
        public event Action<CardUseResult, SkillCastReport> CardActivated;

        public event Action<ItemUseResult> ItemUsed;

        /// <summary>Short player-facing message (cooldown, full gauge, item used …).</summary>
        public event Action<string> Notice;

        void Awake()
        {
            if (character == null)
            {
                character = GetComponent<FpsCharacter>();
            }

            if (caster == null)
            {
                caster = GetComponent<SkillCaster>();
            }

            if (Deck == null)
            {
                Rebuild(startingDeck != null ? startingDeck.cards : null,
                    startingDeck != null ? startingDeck.handSize : CharacterCommands.SkillSlotCount,
                    startingItems, randomSeed);
            }
        }

        void Start()
        {
            if (caster != null && character != null && !caster.IsInitialized)
            {
                caster.Initialize(character, character.Look, GameLayers.PlayerShotMask, GameLayers.SkillQueryMask);
            }
        }

        void OnEnable() => Subscribe();

        void OnDisable() => Unsubscribe();

        void Update()
        {
            Deck?.Tick(Time.deltaTime);
        }

        /// <summary>Builds a fresh deck and inventory (start of a run, tests).</summary>
        public void Rebuild(IEnumerable<CardDefinition> cards, int handSize, IEnumerable<GaugeItemDefinition> items, int seed)
        {
            Seed = seed != 0 ? seed : Environment.TickCount;
            Deck = new DeckState(cards, Mathf.Max(1, handSize), new SeededRandom(Seed));
            Items = new GaugeItemInventory(items);
            SelectedSlot = 0;
            RebuildCount++;
            DeckRebuilt?.Invoke();
        }

        public bool HasPracticeDeck => practiceDeck != null;
        public bool UsingPracticeDeck { get; private set; }

        /// <summary>
        /// Debug / practice: rebuilds the run with the combo practice deck (or back to the
        /// starting deck). It is a normal deck: gauges, discards, draws and reshuffles are unchanged.
        /// </summary>
        public bool SetPracticeDeck(bool enabled)
        {
            DeckDefinition deck = enabled ? practiceDeck : startingDeck;
            if (deck == null)
            {
                return false;
            }

            UsingPracticeDeck = enabled;
            Rebuild(deck.cards, deck.handSize, startingItems, randomSeed);
            Notice?.Invoke(enabled ? "コンボ練習デッキに切り替えました（F7 で戻す）" : "初期デッキに戻しました");
            return true;
        }

        public void SetCharacter(FpsCharacter newCharacter, SkillCaster newCaster)
        {
            Unsubscribe();
            character = newCharacter;
            caster = newCaster;
            Subscribe();
        }

        void Subscribe()
        {
            if (subscribed || character == null)
            {
                return;
            }

            character.SkillSlotRequested += OnSkillSlot;
            character.ItemUseRequested += OnItemUse;
            character.ItemCycleRequested += OnItemCycle;
            character.CardSelectRequested += SetSelectedSlot;
            character.CardSelectStepRequested += StepSelection;
            subscribed = true;
        }

        void Unsubscribe()
        {
            if (!subscribed || character == null)
            {
                return;
            }

            character.SkillSlotRequested -= OnSkillSlot;
            character.ItemUseRequested -= OnItemUse;
            character.ItemCycleRequested -= OnItemCycle;
            character.CardSelectRequested -= SetSelectedSlot;
            character.CardSelectStepRequested -= StepSelection;
            subscribed = false;
        }

        void OnSkillSlot(int slot) => UseSlot(slot);

        void OnItemUse() => UseSelectedItem();

        void OnItemCycle() => CycleItem();

        /// <summary>Uses the card in a hand slot and casts its skill. Normal shooting is never involved.</summary>
        public bool UseSlot(int slot)
        {
            if (Deck == null)
            {
                return false;
            }

            if (!Deck.TryUseCard(slot, out CardUseResult result, out CardUseFailure failure))
            {
                if (failure == CardUseFailure.OnCooldown)
                {
                    Notice?.Invoke("クールダウン中");
                }
                else if (failure == CardUseFailure.EmptySlot)
                {
                    Notice?.Invoke("この枠にカードがありません");
                }

                return false;
            }

            SetSelectedSlot(slot);
            SkillCastReport report = caster != null && caster.IsInitialized
                ? caster.CastCard(result.Card.Definition, result.Card.UpgradeLevel)
                : new SkillCastReport { Card = result.Card.Definition };
            CoopDeck.Animation.CharacterAnimatorDriver body = character != null ? character.BodyAnimator : null;
            if (body != null)
            {
                body.Skill(SkillMotionRules.For(result.Card.Definition)); // the cast motion never delays the effect
            }

            CardActivated?.Invoke(result, report);
            return true;
        }

        public bool UseSelectedItem()
        {
            if (Deck == null || Items == null)
            {
                return false;
            }

            if (!Items.TryUseSelected(Deck, SelectedSlot, out ItemUseResult result, out ItemUseFailure failure))
            {
                GaugeItemDefinition item = Items.Selected;
                string name = item != null ? item.displayName : "アイテム";
                switch (failure)
                {
                    case ItemUseFailure.OutOfStock:
                        Notice?.Invoke($"{name}がありません");
                        break;
                    case ItemUseFailure.AlreadyFull:
                        Notice?.Invoke("ゲージが満タンです");
                        break;
                    case ItemUseFailure.NoCardInSlot:
                        Notice?.Invoke("対象のカードがありません");
                        break;
                }

                return false;
            }

            if (result.TargetSlot >= 0)
            {
                CardRuntime card = Deck.GetHandCard(result.TargetSlot);
                Notice?.Invoke($"{result.Item.displayName}：{card?.Definition.displayName} +{result.TotalRestored:0}");
            }
            else
            {
                Notice?.Invoke($"{result.Item.displayName}：手札 {result.CardsAffected} 枚 +{result.Item.amount:0}");
            }

            CoopDeck.Animation.CharacterAnimatorDriver itemBody = character != null ? character.BodyAnimator : null;
            if (itemBody != null)
            {
                itemBody.Interact(CoopDeck.Animation.InteractionMotion.UseItem);
            }

            ItemUsed?.Invoke(result);
            return true;
        }

        public void CycleItem()
        {
            Items?.CycleSelection();
        }

        public void SetSelectedSlot(int slot)
        {
            if (Deck == null || slot < 0 || slot >= Deck.HandSize || slot == SelectedSlot)
            {
                return;
            }

            SelectedSlot = slot;
            SelectionChanged?.Invoke(slot);
        }

        public void StepSelection(int delta)
        {
            if (Deck == null || delta == 0)
            {
                return;
            }

            int count = Deck.HandSize;
            SetSelectedSlot(((SelectedSlot + delta) % count + count) % count);
        }
    }
}
