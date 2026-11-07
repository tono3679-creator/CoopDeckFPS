using System.Collections.Generic;
using CoopDeck.Enemies;
using NUnit.Framework;

namespace CoopDeck.Tests
{
    public class WaveScheduleTests
    {
        static List<int> Drain(WaveSchedule schedule, float dt, int ticks, bool countAsAlive = true)
        {
            var spawned = new List<int>();
            for (int i = 0; i < ticks; i++)
            {
                if (schedule.Tick(dt, out int type))
                {
                    spawned.Add(type);
                    if (countAsAlive) schedule.NotifySpawned();
                }
            }

            return spawned;
        }

        [Test]
        public void FirstWave_SpawnsItsEnemiesInterleaved()
        {
            var schedule = new WaveSchedule(new[] { new[] { 2, 1 } }, 0.5f, 3f, 8);
            schedule.Start();
            Assert.That(schedule.WaveNumber, Is.EqualTo(1));
            Assert.That(schedule.Phase, Is.EqualTo(WavePhase.Spawning));

            List<int> spawned = Drain(schedule, 0.5f, 6);
            Assert.That(spawned, Is.EqualTo(new[] { 0, 1, 0 }));
            Assert.That(schedule.Phase, Is.EqualTo(WavePhase.Fighting));
            Assert.That(schedule.RemainingInWave, Is.EqualTo(3));
        }

        [Test]
        public void MaxAlive_HoldsBackSpawns_UntilEnemiesDie()
        {
            var schedule = new WaveSchedule(new[] { new[] { 5 } }, 0f, 3f, 2);
            schedule.Start();
            Assert.That(Drain(schedule, 0.1f, 10).Count, Is.EqualTo(2));
            Assert.That(schedule.PendingSpawns, Is.EqualTo(3));

            schedule.NotifyDefeated();
            Assert.That(Drain(schedule, 0.1f, 10).Count, Is.EqualTo(1));
        }

        [Test]
        public void ClearingAWave_StartsTheNextAfterTheIntermission()
        {
            var schedule = new WaveSchedule(new[] { new[] { 1 }, new[] { 2 } }, 0f, 3f, 8);
            int cleared = 0;
            schedule.WaveCleared += _ => cleared++;
            schedule.Start();
            Drain(schedule, 0.1f, 3);
            schedule.NotifyDefeated();

            schedule.Tick(0.1f, out _);
            Assert.That(schedule.Phase, Is.EqualTo(WavePhase.Intermission));
            Assert.That(cleared, Is.EqualTo(1));

            Drain(schedule, 0.5f, 5);
            Assert.That(schedule.WaveNumber, Is.EqualTo(1), "intermission not over yet");
            Drain(schedule, 0.5f, 2);
            Assert.That(schedule.WaveNumber, Is.EqualTo(2));
        }

        [Test]
        public void AfterTheLastWave_ItLoopsWithOneMoreOfEachType()
        {
            var schedule = new WaveSchedule(new[] { new[] { 2, 0 }, new[] { 1, 1 } }, 0f, 0f, 8);
            Assert.That(schedule.CountsFor(1), Is.EqualTo(new[] { 2, 0 }));
            Assert.That(schedule.CountsFor(2), Is.EqualTo(new[] { 1, 1 }));
            Assert.That(schedule.CountsFor(3), Is.EqualTo(new[] { 3, 1 }));
            Assert.That(schedule.CountsFor(4), Is.EqualTo(new[] { 2, 2 }));
        }

        [Test]
        public void Stop_PausesSpawning_AndStartResumesTheSameWave()
        {
            var schedule = new WaveSchedule(new[] { new[] { 4 } }, 1f, 3f, 8);
            schedule.Start();
            Drain(schedule, 0.1f, 1);
            schedule.Stop();
            Assert.That(Drain(schedule, 1f, 5), Is.Empty);

            schedule.Start();
            Assert.That(schedule.WaveNumber, Is.EqualTo(1));
            Assert.That(Drain(schedule, 1f, 5).Count, Is.EqualTo(3));
        }
    }
}
