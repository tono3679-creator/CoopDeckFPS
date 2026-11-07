using UnityEngine;

namespace CoopDeck.Combat
{
    public enum ReviveTick
    {
        None = 0,

        /// <summary>The revive finished this tick.</summary>
        Revived = 1,

        /// <summary>Nobody revived in time this tick (the owner decides: respawn, retreat …).</summary>
        BledOut = 2,
    }

    /// <summary>
    /// Down → revive rules as plain data: while downed, an ally standing close fills the revive
    /// bar (it drains back when they leave) and pauses the bleed-out timer. The host will own
    /// this state online; <see cref="Revivable"/> feeds it from the scene.
    /// </summary>
    public sealed class ReviveModel
    {
        public ReviveModel(float reviveTime, float bleedOutTime)
        {
            ReviveTime = Mathf.Max(0.1f, reviveTime);
            BleedOutTime = Mathf.Max(0.1f, bleedOutTime);
        }

        public float ReviveTime { get; }
        public float BleedOutTime { get; }
        public bool IsDowned { get; private set; }
        public bool HasBledOut { get; private set; }
        public float Progress { get; private set; }
        public float BleedOutRemaining { get; private set; }
        public float Progress01 => Progress / ReviveTime;

        /// <summary>Id of the ally currently reviving (0 = nobody).</summary>
        public int ReviverId { get; private set; }

        public void Down()
        {
            IsDowned = true;
            HasBledOut = false;
            Progress = 0f;
            ReviverId = 0;
            BleedOutRemaining = BleedOutTime;
        }

        /// <summary>Back up (revived, respawned or reset).</summary>
        public void Clear()
        {
            IsDowned = false;
            HasBledOut = false;
            Progress = 0f;
            ReviverId = 0;
            BleedOutRemaining = 0f;
        }

        /// <param name="reviverId">Ally in revive range this tick, 0 = none.</param>
        public ReviveTick Tick(float deltaTime, int reviverId)
        {
            if (!IsDowned || HasBledOut || deltaTime <= 0f)
            {
                return ReviveTick.None;
            }

            ReviverId = reviverId;
            if (reviverId != 0)
            {
                Progress += deltaTime;
                if (Progress >= ReviveTime)
                {
                    Clear();
                    return ReviveTick.Revived;
                }

                return ReviveTick.None; // bleed-out pauses while someone is helping
            }

            Progress = Mathf.Max(0f, Progress - deltaTime * 0.5f);
            BleedOutRemaining -= deltaTime;
            if (BleedOutRemaining <= 0f)
            {
                BleedOutRemaining = 0f;
                HasBledOut = true;
                return ReviveTick.BledOut;
            }

            return ReviveTick.None;
        }
    }
}
