using CoopDeck.AI;
using NUnit.Framework;

namespace CoopDeck.Tests
{
    public class EnemyStateMachineTests
    {
        static readonly EnemyTuning Tuning = new EnemyTuning
        {
            DetectionRange = 30f,
            LoseInterestRange = 60f,
            AttackRange = 2.2f,
            Windup = 0.5f,
            Recover = 0.3f,
            Cooldown = 1f,
        };

        static EnemyPerception Sees(float distance) => new EnemyPerception { HasTarget = true, Distance = distance, LineOfSight = true };

        [Test]
        public void WithoutTarget_StaysIdle()
        {
            var machine = new EnemyStateMachine();
            EnemyIntent intent = machine.Tick(0.1f, new EnemyPerception(), Tuning);
            Assert.That(machine.State, Is.EqualTo(EnemyState.Idle));
            Assert.That(intent.Move || intent.BeginWindup || intent.ReleaseAttack, Is.False);
        }

        [Test]
        public void SeeingATarget_StartsTheChase()
        {
            var machine = new EnemyStateMachine();
            EnemyIntent intent = machine.Tick(0.1f, Sees(20f), Tuning);
            Assert.That(machine.State, Is.EqualTo(EnemyState.Chase));
            Assert.That(intent.Move, Is.True);
        }

        [Test]
        public void HiddenTarget_IsIgnored_UntilItHitsTheEnemy()
        {
            var machine = new EnemyStateMachine();
            var hidden = new EnemyPerception { HasTarget = true, Distance = 20f, LineOfSight = false };
            machine.Tick(0.1f, hidden, Tuning);
            Assert.That(machine.State, Is.EqualTo(EnemyState.Idle));

            hidden.WasDamaged = true;
            machine.Tick(0.1f, hidden, Tuning);
            Assert.That(machine.State, Is.EqualTo(EnemyState.Chase), "being hit reveals the attacker");
            hidden.WasDamaged = false;
            Assert.That(machine.Tick(0.1f, hidden, Tuning).Move, Is.True, "keeps chasing without line of sight");
        }

        [Test]
        public void InRange_WindsUp_AndReleasesExactlyOnceAfterTheWindup()
        {
            var machine = new EnemyStateMachine();
            machine.Tick(0.1f, Sees(10f), Tuning);
            EnemyIntent start = machine.Tick(0.1f, Sees(2f), Tuning);
            Assert.That(start.BeginWindup, Is.True);
            Assert.That(start.Move, Is.False, "stands still while telegraphing");
            Assert.That(machine.State, Is.EqualTo(EnemyState.Windup));

            int releases = 0;
            for (int i = 0; i < 9; i++) // 0.45 s of the 0.5 s telegraph
            {
                if (machine.Tick(0.05f, Sees(2f), Tuning).ReleaseAttack) releases++;
            }

            Assert.That(releases, Is.EqualTo(0), "no hit before the telegraph ends");
            for (int i = 0; i < 10; i++)
            {
                if (machine.Tick(0.05f, Sees(2f), Tuning).ReleaseAttack) releases++;
            }

            Assert.That(releases, Is.EqualTo(1));
            Assert.That(machine.CooldownRemaining, Is.GreaterThan(0f));
        }

        [Test]
        public void Cooldown_PreventsAnImmediateSecondAttack()
        {
            var machine = new EnemyStateMachine();
            machine.Tick(0.1f, Sees(10f), Tuning);
            machine.Tick(0.1f, Sees(2f), Tuning); // windup
            machine.Tick(0.6f, Sees(2f), Tuning); // release → recover
            machine.Tick(0.35f, Sees(2f), Tuning); // recover → chase

            Assert.That(machine.State, Is.EqualTo(EnemyState.Chase));
            Assert.That(machine.Tick(0.1f, Sees(2f), Tuning).BeginWindup, Is.False, "still cooling down");
            for (int i = 0; i < 12; i++)
            {
                if (machine.Tick(0.05f, Sees(2f), Tuning).BeginWindup)
                {
                    Assert.Pass();
                }
            }

            Assert.Fail("never attacked again after the cooldown");
        }

        [Test]
        public void InRangeWithoutLineOfSight_KeepsMovingInsteadOfAttacking()
        {
            var machine = new EnemyStateMachine();
            machine.Tick(0.1f, Sees(10f), Tuning);
            var blocked = new EnemyPerception { HasTarget = true, Distance = 1.5f, LineOfSight = false };
            EnemyIntent intent = machine.Tick(0.1f, blocked, Tuning);
            Assert.That(intent.BeginWindup, Is.False);
            Assert.That(intent.Move, Is.True);
        }

        [Test]
        public void TooFarAway_GivesUp()
        {
            var machine = new EnemyStateMachine();
            machine.Tick(0.1f, Sees(10f), Tuning);
            machine.Tick(0.1f, Sees(70f), Tuning);
            Assert.That(machine.State, Is.EqualTo(EnemyState.Idle));
        }

        [Test]
        public void Dead_DoesNothing()
        {
            var machine = new EnemyStateMachine();
            machine.Tick(0.1f, Sees(2f), Tuning);
            machine.Kill();
            EnemyIntent intent = machine.Tick(1f, Sees(1f), Tuning);
            Assert.That(machine.State, Is.EqualTo(EnemyState.Dead));
            Assert.That(intent.Move || intent.BeginWindup || intent.ReleaseAttack, Is.False);
        }
    }
}
