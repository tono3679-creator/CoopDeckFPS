namespace CoopDeck.AI
{
    public enum EnemyState
    {
        Idle = 0,
        Chase = 1,
        Windup = 2,
        Recover = 3,
        Dead = 4,
    }

    /// <summary>What the enemy senses this frame.</summary>
    public struct EnemyPerception
    {
        public bool HasTarget;
        public float Distance;
        public bool LineOfSight;

        /// <summary>Took damage this frame (aggro even out of sight).</summary>
        public bool WasDamaged;
    }

    /// <summary>Timing and ranges, copied from the enemy definition.</summary>
    public struct EnemyTuning
    {
        public float DetectionRange;
        public float LoseInterestRange;
        public float AttackRange;
        public float Windup;
        public float Recover;
        public float Cooldown;
    }

    /// <summary>What the body should do this frame.</summary>
    public struct EnemyIntent
    {
        public bool Move;
        public bool FaceTarget;

        /// <summary>The wind-up (telegraph) starts this frame.</summary>
        public bool BeginWindup;

        /// <summary>The attack lands this frame. Happens exactly once per wind-up.</summary>
        public bool ReleaseAttack;
    }

    /// <summary>
    /// Enemy behaviour as a plain state machine: Idle → Chase → Windup → (attack) → Recover → Chase.
    /// No physics or Unity objects, so every transition is unit-tested; the brain applies the intent.
    /// </summary>
    public sealed class EnemyStateMachine
    {
        bool aggro;

        public EnemyState State { get; private set; } = EnemyState.Idle;
        public float StateTime { get; private set; }
        public float CooldownRemaining { get; private set; }
        public bool IsAggro => aggro;

        public EnemyIntent Tick(float deltaTime, in EnemyPerception perception, in EnemyTuning tuning)
        {
            var intent = new EnemyIntent();
            StateTime += deltaTime;
            if (CooldownRemaining > 0f)
            {
                CooldownRemaining -= deltaTime;
            }

            if (State == EnemyState.Dead)
            {
                return intent;
            }

            if (!perception.HasTarget)
            {
                aggro = false;
                if (State != EnemyState.Idle)
                {
                    Enter(EnemyState.Idle);
                }

                return intent;
            }

            if (perception.WasDamaged || perception.LineOfSight && perception.Distance <= tuning.DetectionRange)
            {
                aggro = true;
            }

            if (aggro && perception.Distance > tuning.LoseInterestRange)
            {
                aggro = false;
            }

            switch (State)
            {
                case EnemyState.Idle:
                    if (aggro)
                    {
                        Enter(EnemyState.Chase);
                        intent.Move = true;
                    }

                    break;

                case EnemyState.Chase:
                    if (!aggro)
                    {
                        Enter(EnemyState.Idle);
                        break;
                    }

                    bool inRange = perception.Distance <= tuning.AttackRange && perception.LineOfSight;
                    if (inRange && CooldownRemaining <= 0f)
                    {
                        Enter(EnemyState.Windup);
                        intent.BeginWindup = true;
                        intent.FaceTarget = true;
                        break;
                    }

                    intent.Move = true;
                    intent.FaceTarget = perception.Distance <= tuning.AttackRange * 1.5f;
                    break;

                case EnemyState.Windup:
                    intent.FaceTarget = true;
                    if (StateTime >= tuning.Windup)
                    {
                        intent.ReleaseAttack = true;
                        CooldownRemaining = tuning.Cooldown;
                        Enter(EnemyState.Recover);
                    }

                    break;

                case EnemyState.Recover:
                    intent.FaceTarget = true;
                    if (StateTime >= tuning.Recover)
                    {
                        Enter(EnemyState.Chase);
                    }

                    break;
            }

            return intent;
        }

        public void Kill()
        {
            Enter(EnemyState.Dead);
        }

        void Enter(EnemyState state)
        {
            State = state;
            StateTime = 0f;
        }
    }
}
