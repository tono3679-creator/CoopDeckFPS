namespace CoopDeck.NPC
{
    /// <summary>What the mode layer needs to know this frame.</summary>
    public struct NpcModeInput
    {
        public bool Alive;
        public bool HasLeader;
        public float LeaderDistance;
        public bool LeaderDowned;

        /// <summary>The utility layer asked to dodge (one-shot).</summary>
        public bool WantsEvade;

        /// <summary>An enemy the team is fighting is known.</summary>
        public bool HasEngagedEnemy;
    }

    /// <summary>
    /// The NPC's high-level mode as a plain state machine. Priority: Downed → Evade (short, timed)
    /// → Rescue (leader down) → Regroup (too far, with hysteresis) → Engage (with a short linger
    /// after the last enemy) → Follow. The utility layer chooses actions inside the mode.
    /// </summary>
    public sealed class NpcStateMachine
    {
        float evadeLeft;
        float engageLinger;

        public NpcMode Mode { get; private set; } = NpcMode.Follow;
        public float ModeTime { get; private set; }
        public int Transitions { get; private set; }

        public NpcMode Tick(float deltaTime, in NpcModeInput input, NpcProfile profile)
        {
            ModeTime += deltaTime;
            if (!input.Alive)
            {
                evadeLeft = 0f;
                engageLinger = 0f;
                return Enter(NpcMode.Downed);
            }

            if (evadeLeft > 0f)
            {
                evadeLeft -= deltaTime;
                if (evadeLeft > 0f)
                {
                    return Enter(NpcMode.Evade);
                }
            }

            if (input.WantsEvade && Mode != NpcMode.Evade)
            {
                evadeLeft = profile.evadeDuration;
                return Enter(NpcMode.Evade);
            }

            if (input.HasLeader && input.LeaderDowned)
            {
                return Enter(NpcMode.Rescue);
            }

            if (input.HasLeader)
            {
                bool far = Mode == NpcMode.Regroup
                    ? input.LeaderDistance > profile.regroupDoneDistance
                    : input.LeaderDistance > profile.regroupDistance;
                if (far)
                {
                    return Enter(NpcMode.Regroup);
                }
            }

            if (input.HasEngagedEnemy)
            {
                engageLinger = 2.5f;
                return Enter(NpcMode.Engage);
            }

            if (engageLinger > 0f)
            {
                engageLinger -= deltaTime;
                return Enter(NpcMode.Engage);
            }

            return Enter(NpcMode.Follow);
        }

        NpcMode Enter(NpcMode mode)
        {
            if (mode != Mode)
            {
                Mode = mode;
                ModeTime = 0f;
                Transitions++;
            }

            return Mode;
        }
    }
}
