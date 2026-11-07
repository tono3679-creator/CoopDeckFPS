using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.NPC
{
    /// <summary>
    /// Tuning for one NPC ally: identity, how it follows, how well it aims, how often it thinks,
    /// and how long it saves cards for combos. A new NPC personality is a new asset.
    /// </summary>
    [CreateAssetMenu(menuName = "CoopDeck/NPC Profile", fileName = "NP_NewNpc")]
    public class NpcProfile : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "ヴォルト";
        public Element favoredElement = Element.Lightning;
        public Color color = new Color(1f, 0.86f, 0.3f);

        [Header("Thinking (not every frame)")]
        [Tooltip("Seconds between decisions. Aim, movement and firing still update every frame.")]
        [Range(0.05f, 1f)] public float thinkInterval = 0.2f;
        [Tooltip("Seconds between NavMesh path updates (at most).")]
        [Range(0.1f, 2f)] public float repathInterval = 0.5f;
        [Tooltip("Seconds before a telegraphed attack is noticed (human-like reaction).")]
        [Range(0f, 0.6f)] public float reactionTime = 0.15f;

        [Header("Following")]
        [Min(0.5f)] public float followMinDistance = 3f;
        [Min(1f)] public float followMaxDistance = 7f;
        [Tooltip("Where the NPC stands relative to the leader's view (degrees from the view direction, 180 = behind).")]
        [Range(60f, 180f)] public float followSlotAngle = 130f;
        [Min(1f)] public float followSlotDistance = 4.5f;

        [Header("Distances")]
        [Tooltip("In a fight, stay within this distance of the leader.")]
        [Min(4f)] public float engageLeash = 15f;
        [Tooltip("Further than this from the leader: stop fighting and catch up.")]
        [Min(8f)] public float regroupDistance = 26f;
        [Tooltip("Catching up ends below this distance.")]
        [Min(2f)] public float regroupDoneDistance = 9f;
        [Tooltip("Further than this (or stuck for long): teleport next to the leader.")]
        [Min(10f)] public float teleportDistance = 48f;
        [Min(5f)] public float perceptionRange = 38f;
        [Tooltip("Enemies closer than this are noticed even without line of sight.")]
        [Min(1f)] public float hearingRange = 12f;
        [Min(1f)] public float preferredRangeMin = 7f;
        [Min(2f)] public float preferredRangeMax = 16f;
        [Tooltip("Keep at least this far from melee enemies.")]
        [Min(1f)] public float meleeKeepAway = 4.5f;

        [Header("Aim and fire")]
        [Min(30f)] public float turnSpeed = 320f;
        [Min(1f)] public float aimGain = 11f;
        [Tooltip("Aim wobble in degrees (keeps the NPC from being a perfect shot).")]
        [Range(0f, 4f)] public float aimError = 0.8f;
        [Tooltip("Fires only when the aim is within this many degrees of the target.")]
        [Range(0.5f, 10f)] public float fireTolerance = 3f;
        public Vector2 burstSeconds = new Vector2(0.5f, 1.1f);
        public Vector2 pauseSeconds = new Vector2(0.25f, 0.55f);

        [Header("Cards and combos")]
        [Tooltip("Auto: seconds a combo finisher may be saved for an ally's element.")]
        [Min(0f)] public float holdAuto = 2.5f;
        [Tooltip("Combo-first tactic: seconds a finisher may be saved.")]
        [Min(0f)] public float holdComboFirst = 6f;
        [Tooltip("After a hold runs out, that card is used normally for this long before it can be saved again.")]
        [Min(0f)] public float holdCooldown = 5f;
        [Tooltip("A combo is attempted only if at least this much of its window is left (time to aim and land).")]
        [Range(0f, 2f)] public float comboMargin = 0.35f;
        [Tooltip("Seconds between ordinary skill uses (a human cannot press skills back to back either). Combo finishes and emergency heals ignore it.")]
        [Range(0f, 3f)] public float castInterval = 1f;
        [Tooltip("Gives up aiming a skill after this long (moving target, blocked view).")]
        [Range(0.3f, 3f)] public float skillAimTimeout = 1.2f;

        [Header("Evasion")]
        [Tooltip("Chance to dodge an attack that is not dangerous (dangerous ones are always dodged when possible).")]
        [Range(0f, 1f)] public float dodgeChance = 0.75f;
        [Tooltip("An attack doing at least this share of current HP counts as dangerous.")]
        [Range(0.05f, 1f)] public float lethalShare = 0.45f;
        [Min(0.1f)] public float evadeDuration = 0.4f;

        [Header("Down")]
        [Tooltip("After bleeding out, the NPC rejoins the leader with this much health after a short delay.")]
        [Range(0.1f, 1f)] public float recoverHealthFraction = 0.5f;
        [Min(0f)] public float recoverDelay = 1.5f;

        public float HoldFor(NpcTactic tactic) => tactic == NpcTactic.ComboFirst ? holdComboFirst : holdAuto;

        public static NpcProfile CreateRuntimeDefault()
        {
            var profile = CreateInstance<NpcProfile>();
            profile.name = "NP_RuntimeDefault";
            return profile;
        }
    }
}
