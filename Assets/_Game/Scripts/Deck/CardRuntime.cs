using CoopDeck.Cards;
using UnityEngine;

namespace CoopDeck.Deck
{
    /// <summary>
    /// One physical copy of a card in a run: its own gauge, cooldown and upgrade level, pointing
    /// at a shared <see cref="CardDefinition"/>. Two copies of the same card never share state.
    /// </summary>
    public sealed class CardRuntime
    {
        internal CardRuntime(int instanceId, CardDefinition definition)
        {
            InstanceId = instanceId;
            Definition = definition;
            Gauge = MaxGauge;
        }

        /// <summary>Unique within the run (network sync, UI tracking).</summary>
        public int InstanceId { get; }

        public CardDefinition Definition { get; }
        public float Gauge { get; internal set; }
        public float CooldownRemaining { get; internal set; }
        public int UpgradeLevel { get; internal set; }

        public float MaxGauge => Mathf.Max(1f, Definition.maxGauge);

        /// <summary>Never below 1, so no modifier can make a card free to spam.</summary>
        public float GaugeCost => Mathf.Max(1f, Definition.gaugeCost);

        public float Cooldown => Mathf.Max(0f, Definition.cooldown);
        public bool IsActive => Definition.type == CardType.Active;
        public bool IsOnCooldown => CooldownRemaining > 0f;
        public float GaugeNormalized => Gauge / MaxGauge;

        /// <summary>Uses left with the current gauge, counting the final partial use (rule R6).</summary>
        public int RemainingUses => Gauge <= 0f ? 0 : Mathf.CeilToInt(Gauge / GaugeCost - 0.0001f);

        public override string ToString() => $"{Definition.cardId}#{InstanceId} ({Gauge:0}/{MaxGauge:0})";
    }
}
