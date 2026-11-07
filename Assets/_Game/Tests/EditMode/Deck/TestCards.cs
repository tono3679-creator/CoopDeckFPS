using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Items;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>Creates throwaway card / item definitions for tests and destroys them afterwards.</summary>
    public sealed class TestCards
    {
        readonly List<Object> created = new List<Object>();

        public CardDefinition Card(string id, float cost, float cooldown = 0f, CardType type = CardType.Active, float maxGauge = 100f)
        {
            var card = ScriptableObject.CreateInstance<CardDefinition>();
            card.name = id;
            card.cardId = id;
            card.displayName = id;
            card.type = type;
            card.maxGauge = maxGauge;
            card.gaugeCost = cost;
            card.cooldown = cooldown;
            card.effects = new List<SkillEffectSpec>();
            created.Add(card);
            return card;
        }

        public List<CardDefinition> Many(int count, float cost, float cooldown = 0f)
        {
            var list = new List<CardDefinition>();
            for (int i = 0; i < count; i++)
            {
                list.Add(Card($"card.test_{i}", cost, cooldown));
            }

            return list;
        }

        public GaugeItemDefinition Item(string id, GaugeItemTarget target, float amount, int maxCarry = 2, int startCount = 2)
        {
            var item = ScriptableObject.CreateInstance<GaugeItemDefinition>();
            item.name = id;
            item.itemId = id;
            item.displayName = id;
            item.target = target;
            item.amount = amount;
            item.maxCarry = maxCarry;
            item.startCount = startCount;
            created.Add(item);
            return item;
        }

        public void DestroyAll()
        {
            foreach (Object o in created)
            {
                if (o != null)
                {
                    Object.DestroyImmediate(o);
                }
            }

            created.Clear();
        }
    }
}
