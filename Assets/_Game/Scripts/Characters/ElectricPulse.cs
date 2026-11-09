using System;

namespace CoopDeck.Characters
{
    /// <summary>
    /// How strongly an electric character's glowing marks shine and how often sparks jump from its hands, from what the
    /// character is doing (plain C#; <see cref="ElectricBodyFx"/> applies it):
    ///   Boost  a slow breathing glow, raised while sprinting, with short spikes on casts, dashes and hits that decay
    ///   charge the weapon's consecutive-hit charge (the Thunderhop's 帯電連射, 0-1) brightens the marks as it builds;
    ///          its discharge is the biggest spike
    ///   sparks a random interval between sparks that shortens with activity (idle: about one a second; sprinting,
    ///          a full charge or right after a cast: several a second)
    /// </summary>
    public sealed class ElectricPulse
    {
        public const float BreathPeriod = 2.6f;
        public const float BreathAmplitude = 0.18f;
        public const float SprintBoost = 0.45f;
        public const float SprintEase = 4f;      // per second toward the sprint level
        public const float SpikeDecay = 5f;      // per second (exponential)
        public const float CastSpike = 1.6f;
        public const float DashSpike = 2.2f;
        public const float HitSpike = 0.7f;
        public const float HeavyHitSpike = 1.2f;
        public const float ChargeGlow = 0.7f;    // added at a full charge
        public const float ChargeEase = 10f;     // per second toward the charge level
        public const float BurstSpike = 2.8f;    // the charge's discharge
        public const float IdleSparkInterval = 1.3f;
        public const float BusySparkInterval = 0.16f;

        readonly Random random;
        float time;
        float sprint;
        float spike;
        float charge;
        float sparkTimer;

        public ElectricPulse(int seed = 1)
        {
            random = new Random(seed);
            sparkTimer = NextInterval();
        }

        /// <summary>Added to the glow's base intensity (the shader multiplies by 1 + Boost).</summary>
        public float Boost { get; private set; }

        /// <summary>0 at rest, about 1 while sprinting, higher right after a cast or dash.</summary>
        public float Activity => sprint / SprintBoost + spike + charge;

        /// <summary>The eased weapon charge (0-1) the glow follows.</summary>
        public float Charge => charge;

        public float Spike => spike;

        public void Step(float deltaTime, bool sprinting) => Step(deltaTime, sprinting, 0f);

        /// <summary>One frame; <paramref name="chargeProgress"/> is the weapon's consecutive-hit charge (0-1, 0 without one).</summary>
        public void Step(float deltaTime, bool sprinting, float chargeProgress)
        {
            if (deltaTime <= 0f)
            {
                return;
            }

            time = (time + deltaTime) % (BreathPeriod * 1000f);
            float target = sprinting ? SprintBoost : 0f;
            sprint += (target - sprint) * Math.Min(1f, SprintEase * deltaTime);
            spike *= (float)Math.Exp(-SpikeDecay * deltaTime);
            float targetCharge = Math.Min(1f, Math.Max(0f, chargeProgress));
            charge += (targetCharge - charge) * Math.Min(1f, ChargeEase * deltaTime);
            float breath = BreathAmplitude * 0.5f * (1f + (float)Math.Sin(time * 2.0 * Math.PI / BreathPeriod));
            Boost = breath + sprint + ChargeGlow * charge + spike;
            sparkTimer -= deltaTime;
        }

        /// <summary>A short spike (cast, dash, hit); a stronger spike replaces a weaker one, they do not stack.</summary>
        public void Kick(float amount)
        {
            spike = Math.Max(spike, amount);
            sparkTimer = Math.Min(sparkTimer, NextInterval() * 0.25f);
        }

        /// <summary>True when a spark is due (then the next interval starts).</summary>
        public bool TakeSpark()
        {
            if (sparkTimer > 0f)
            {
                return false;
            }

            sparkTimer = NextInterval();
            return true;
        }

        /// <summary>The next gap between sparks: shorter the busier the character, jittered ±40%.</summary>
        public float NextInterval()
        {
            float busy = Math.Min(1f, Math.Max(0f, Activity / 1.5f));
            float mean = IdleSparkInterval + (BusySparkInterval - IdleSparkInterval) * busy;
            return mean * (0.6f + 0.8f * (float)random.NextDouble());
        }
    }
}
