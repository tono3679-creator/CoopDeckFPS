using System;
using System.Collections.Generic;
using CoopDeck.Deck;
using UnityEngine;

namespace CoopDeck.Items
{
    public enum ItemUseFailure
    {
        None = 0,
        NoItems = 1,
        OutOfStock = 2,
        NoCardInSlot = 3,
        AlreadyFull = 4,
    }

    public readonly struct ItemUseResult
    {
        public ItemUseResult(GaugeItemDefinition item, float totalRestored, int cardsAffected, int targetSlot)
        {
            Item = item;
            TotalRestored = totalRestored;
            CardsAffected = cardsAffected;
            TargetSlot = targetSlot;
        }

        public GaugeItemDefinition Item { get; }
        public float TotalRestored { get; }
        public int CardsAffected { get; }

        /// <summary>Slot for single-target items, -1 for whole-hand items.</summary>
        public int TargetSlot { get; }
    }

    /// <summary>
    /// Gauge items carried by one combatant, with a selected item type. Plain C#.
    /// Rules (CARD_SYSTEM.md §5): no overflow above the card maximum, hand cards only, an item that
    /// would restore nothing is refused and not consumed, carry limit per item type.
    /// </summary>
    public sealed class GaugeItemInventory
    {
        sealed class Entry
        {
            public GaugeItemDefinition Definition;
            public int Count;
        }

        readonly List<Entry> entries = new List<Entry>();

        public event Action Changed;

        public GaugeItemInventory(IEnumerable<GaugeItemDefinition> items)
        {
            if (items == null)
            {
                return;
            }

            foreach (GaugeItemDefinition item in items)
            {
                if (item == null || IndexOf(item) >= 0)
                {
                    continue;
                }

                entries.Add(new Entry { Definition = item, Count = Mathf.Clamp(item.startCount, 0, item.maxCarry) });
            }
        }

        public int ItemTypeCount => entries.Count;
        public int SelectedIndex { get; private set; }
        public GaugeItemDefinition Selected => entries.Count > 0 ? entries[SelectedIndex].Definition : null;

        public GaugeItemDefinition GetDefinition(int index) => index >= 0 && index < entries.Count ? entries[index].Definition : null;
        public int GetCount(int index) => index >= 0 && index < entries.Count ? entries[index].Count : 0;
        public int GetCount(GaugeItemDefinition item) => GetCount(IndexOf(item));

        public int IndexOf(GaugeItemDefinition item)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].Definition == item)
                {
                    return i;
                }
            }

            return -1;
        }

        public void CycleSelection()
        {
            if (entries.Count == 0)
            {
                return;
            }

            SelectedIndex = (SelectedIndex + 1) % entries.Count;
            Changed?.Invoke();
        }

        public void Select(int index)
        {
            if (index < 0 || index >= entries.Count || index == SelectedIndex)
            {
                return;
            }

            SelectedIndex = index;
            Changed?.Invoke();
        }

        /// <summary>Adds items up to the carry limit. Returns how many were actually added.</summary>
        public int Add(GaugeItemDefinition item, int amount)
        {
            if (item == null || amount <= 0)
            {
                return 0;
            }

            int index = IndexOf(item);
            if (index < 0)
            {
                entries.Add(new Entry { Definition = item, Count = 0 });
                index = entries.Count - 1;
            }

            Entry entry = entries[index];
            int added = Mathf.Min(amount, entry.Definition.maxCarry - entry.Count);
            if (added <= 0)
            {
                return 0;
            }

            entry.Count += added;
            Changed?.Invoke();
            return added;
        }

        public bool TryUseSelected(DeckState deck, int selectedSlot, out ItemUseResult result, out ItemUseFailure failure)
        {
            return TryUse(SelectedIndex, deck, selectedSlot, out result, out failure);
        }

        public bool TryUse(int index, DeckState deck, int selectedSlot, out ItemUseResult result, out ItemUseFailure failure)
        {
            result = default;
            if (deck == null || index < 0 || index >= entries.Count)
            {
                failure = ItemUseFailure.NoItems;
                return false;
            }

            Entry entry = entries[index];
            if (entry.Count <= 0)
            {
                failure = ItemUseFailure.OutOfStock;
                return false;
            }

            GaugeItemDefinition item = entry.Definition;
            float restored = 0f;
            int affected = 0;
            int targetSlot = -1;

            if (item.target == GaugeItemTarget.SelectedCard)
            {
                targetSlot = selectedSlot;
                if (deck.GetHandCard(selectedSlot) == null)
                {
                    failure = ItemUseFailure.NoCardInSlot;
                    return false;
                }

                restored = deck.RestoreCharge(selectedSlot, item.amount);
                affected = restored > 0f ? 1 : 0;
            }
            else
            {
                if (deck.HandCount == 0)
                {
                    failure = ItemUseFailure.NoCardInSlot;
                    return false;
                }

                for (int slot = 0; slot < deck.HandSize; slot++)
                {
                    float amount = deck.RestoreCharge(slot, item.amount);
                    if (amount > 0f)
                    {
                        restored += amount;
                        affected++;
                    }
                }
            }

            if (restored <= 0f)
            {
                failure = ItemUseFailure.AlreadyFull; // nothing to restore: keep the item
                return false;
            }

            entry.Count--;
            failure = ItemUseFailure.None;
            result = new ItemUseResult(item, restored, affected, targetSlot);
            Changed?.Invoke();
            return true;
        }
    }
}
