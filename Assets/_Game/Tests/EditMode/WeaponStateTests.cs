using CoopDeck.Weapons;
using NUnit.Framework;

namespace CoopDeck.Tests
{
    public class WeaponStateTests
    {
        static WeaponState Rifle() => new WeaponState(magazineSize: 30, secondsPerShot: 0.1f, reloadDuration: 1.5f,
            spreadPerShot: 0.2f, maxBloom: 1f, bloomRecovery: 2f);

        [Test]
        public void StartsFull_AndFiringConsumesAmmo()
        {
            WeaponState state = Rifle();
            Assert.That(state.Ammo, Is.EqualTo(30));
            Assert.That(state.TryFire(), Is.True);
            Assert.That(state.Ammo, Is.EqualTo(29));
        }

        [Test]
        public void Cooldown_BlocksImmediateSecondShot()
        {
            WeaponState state = Rifle();
            Assert.That(state.TryFire(), Is.True);
            Assert.That(state.TryFire(), Is.False);
            state.Tick(0.05f, triggerHeld: true);
            Assert.That(state.TryFire(), Is.False);
            state.Tick(0.06f, triggerHeld: true);
            Assert.That(state.TryFire(), Is.True);
        }

        [Test]
        public void FirstTriggerPull_FiresExactlyOneRound()
        {
            WeaponState state = Rifle();
            state.Tick(1f, triggerHeld: false);
            state.Tick(1f / 30f, triggerHeld: true);
            Assert.That(state.TryFire(), Is.True);
            Assert.That(state.TryFire(), Is.False);
        }

        [Test]
        public void LowFrameRate_FiresMultipleRoundsPerFrame_ToKeepRate()
        {
            var state = new WeaponState(1000, 0.1f, 1f);
            int shots = 0;
            for (int frame = 0; frame < 8; frame++)
            {
                state.Tick(0.25f, triggerHeld: true);
                while (state.TryFire())
                {
                    shots++;
                }
            }

            // First round on the first frame, then 7 more frames * 0.25 s at 600 RPM = 17.5 rounds.
            Assert.That(shots, Is.InRange(18, 19));
        }

        [TestCase(30f)]
        [TestCase(60f)]
        [TestCase(144f)]
        [TestCase(23f)]
        public void HeldTrigger_FireRateIsExact_AtAnyFrameRate(float fps)
        {
            var state = new WeaponState(1000, 0.1f, 1f);
            float dt = 1f / fps;
            int shots = 0;
            float elapsed = 0f;
            while (elapsed < 2f - 1e-4f)
            {
                state.Tick(dt, triggerHeld: true);
                while (state.TryFire())
                {
                    shots++;
                }

                elapsed += dt;
            }

            // 600 RPM for 2 s = 20 shots (first shot at t=0, so 20 or 21 depending on the final frame).
            Assert.That(shots, Is.InRange(20, 21));
        }

        [Test]
        public void HoldingTriggerDuringReload_DoesNotBankShots()
        {
            WeaponState state = Rifle();
            while (state.TryFire())
            {
                state.Tick(0.1f, triggerHeld: true);
            }

            Assert.That(state.Ammo, Is.EqualTo(0));
            Assert.That(state.StartReload(), Is.True);
            for (int i = 0; i < 20; i++)
            {
                state.Tick(0.1f, triggerHeld: true);
            }

            Assert.That(state.IsReloading, Is.False);
            Assert.That(state.TryFire(), Is.True);
            Assert.That(state.TryFire(), Is.False, "only one shot may be available right after a reload");
        }

        [Test]
        public void Reload_RefillsAfterDuration_AndBlocksFiring()
        {
            WeaponState state = Rifle();
            state.TryFire();
            Assert.That(state.StartReload(), Is.True);
            Assert.That(state.CanFire, Is.False);

            Assert.That(state.Tick(1.0f, false), Is.False);
            Assert.That(state.ReloadProgress, Is.EqualTo(1f / 1.5f).Within(1e-4f));
            Assert.That(state.Tick(0.6f, false), Is.True, "reload completes on this tick");
            Assert.That(state.Ammo, Is.EqualTo(30));
            Assert.That(state.CanFire, Is.True);
        }

        [Test]
        public void Reload_RefusedWhenFullOrAlreadyReloading()
        {
            WeaponState state = Rifle();
            Assert.That(state.StartReload(), Is.False, "full magazine");
            state.TryFire();
            Assert.That(state.StartReload(), Is.True);
            Assert.That(state.StartReload(), Is.False, "already reloading");
        }

        [Test]
        public void Bloom_GrowsPerShot_ClampsAndRecovers()
        {
            WeaponState state = Rifle();
            for (int i = 0; i < 10; i++)
            {
                state.TryFire();
                state.Tick(0.1f, true);
            }

            Assert.That(state.Bloom, Is.LessThanOrEqualTo(1f));
            state.Tick(10f, false);
            Assert.That(state.Bloom, Is.EqualTo(0f));
        }

        [Test]
        public void NeedsReload_WhenEmpty()
        {
            var state = new WeaponState(2, 0.01f, 0.5f);
            state.TryFire();
            state.Tick(0.02f, true);
            state.TryFire();
            Assert.That(state.Ammo, Is.EqualTo(0));
            Assert.That(state.NeedsReload, Is.True);
            Assert.That(state.TryFire(), Is.False);
        }
    }
}
