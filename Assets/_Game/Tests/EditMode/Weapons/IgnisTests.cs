using CoopDeck.Characters;
using CoopDeck.Core;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEditor;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The fox's IGNIS-27 Blazetail (Docs/IGNIS27_RIFLE.md): the design JSON converted to weapon data, the 火種刻印
    /// rules (6 hits, 2.25 s gap, target switch, 4 s per target), the splash ignition as a second shape of the
    /// consecutive-hit ability next to the VOLT-9's chain, the fox's starting weapon, and the previous rifle (プロトAR)
    /// left as it was.
    /// </summary>
    public class IgnisTests
    {
        const string IgnisPath = "Assets/_Game/ScriptableObjects/Weapons/WD_Ignis27Blazetail.asset";
        const string ThunderhopPath = "Assets/_Game/ScriptableObjects/Weapons/WD_Volt9Thunderhop.asset";
        const string RiflePath = "Assets/_Game/ScriptableObjects/Weapons/WD_AssaultRifle.asset";
        const string FoxPath = "Assets/_Game/ScriptableObjects/Characters/CD_FoxGunner.asset";
        const string RabbitPath = "Assets/_Game/ScriptableObjects/Characters/CD_LightningRabbit.asset";

        static T Load<T>(string path) where T : UnityEngine.Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path + " missing: run the bootstrap (or IgnisAssetFactory.RunStandalone)");
            return asset;
        }

        static ChargeBurstSpec Brand() => new ChargeBurstSpec
        {
            enabled = true, requiredStacks = 6, maxSecondsBetweenHits = 2.25f, stacksPerHit = 1, perTargetCooldown = 4f,
            shape = ChargeBurstShape.Splash,
        };

        [Test]
        public void DesignJson_IsConvertedToTheWeaponData()
        {
            var w = Load<WeaponDefinition>(IgnisPath);
            Assert.That((w.weaponId, w.displayName, w.categoryLabel), Is.EqualTo(("weapon.ignis27_blazetail", "IGNIS-27 ブレイズテイル", "アサルトライフル")));
            Assert.That(w.element, Is.EqualTo(Element.Fire));
            Assert.That((w.damage, w.roundsPerMinute, w.magazineSize, w.reloadTime), Is.EqualTo((18f, 480f, 28, 2.1f)));
            Assert.That(w.automatic, Is.True);
            Assert.That(w.pelletsPerShot, Is.EqualTo(1));
            Assert.That(w.headshotMultiplier, Is.EqualTo(1.65f).Within(1e-4f));
            Assert.That((w.range, w.falloffStart, w.FalloffEnd), Is.EqualTo((85f, 24f, 60f)));
            Assert.That(w.minDamageFactor, Is.EqualTo(0.7f).Within(1e-4f));
            Assert.That(w.FalloffAt(24f), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(w.FalloffAt(60f), Is.EqualTo(0.7f).Within(1e-4f));
            Assert.That(w.FalloffAt(80f), Is.EqualTo(0.7f).Within(1e-4f), "0.7 from 60 m to the 85 m range");
            Assert.That((w.baseSpread, w.spreadPerShot, w.maxBloom, w.movingSpread), Is.EqualTo((0f, 0f, 0f, 0f)),
                "no ADS; hip fire has no spread (user, 2026-11-08)");
            Assert.That((w.recoilPitch, w.recoilYaw, w.recoilRecoverySeconds), Is.EqualTo((1.05f, 0.35f, 0.2f)));

            ChargeBurstSpec s = w.chargeBurst;
            Assert.That(s.enabled, Is.True);
            Assert.That(s.displayName, Is.EqualTo("火種刻印"));
            Assert.That(s.shape, Is.EqualTo(ChargeBurstShape.Splash));
            Assert.That((s.requiredStacks, s.stacksPerHit), Is.EqualTo((6, 1)));
            Assert.That(s.maxSecondsBetweenHits, Is.EqualTo(2.25f).Within(1e-4f));
            Assert.That((s.primaryDamage, s.splashRadius, s.splashTargets, s.splashDamage), Is.EqualTo((22f, 2.5f, 4, 12f)));
            Assert.That(s.element, Is.EqualTo(Element.Fire));
            Assert.That((s.statusDamagePerSecond, s.statusDuration), Is.EqualTo((6f, 4f)), "the existing burn (fireball card's rate) for 4 s");
            Assert.That(s.perTargetCooldown, Is.EqualTo(4f));
            Assert.That(s.chainTargets, Is.EqualTo(0), "no chain on a splash");

            Assert.That(w.muzzleFlashPrefab != null && w.tracerPrefab != null && w.impactPrefab != null && w.damageableImpactPrefab != null, Is.True);
            Assert.That(s.burstFx != null && s.chainBurstFx != null && s.chargeFx != null, Is.True, "ignition, splash hit and ember charge effects");
            Assert.That(s.arcFx, Is.Null, "no bolt for a burst on the target");
        }

        [Test]
        public void TheFoxStartsWithIgnis_TheRabbitWithVolt9_AndTheOldRifleIsUnchanged()
        {
            var ignis = Load<WeaponDefinition>(IgnisPath);
            var volt = Load<WeaponDefinition>(ThunderhopPath);
            Assert.That(Load<CharacterDefinition>(FoxPath).StartingWeapon, Is.SameAs(ignis));
            Assert.That(Load<CharacterDefinition>(RabbitPath).StartingWeapon, Is.SameAs(volt));
            Assert.That(volt.chargeBurst.shape, Is.EqualTo(ChargeBurstShape.Chain), "the VOLT-9 keeps its chain");
            Assert.That(volt.chargeBurst.statusDamagePerSecond, Is.EqualTo(0f), "and no damage over time");

            var rifle = Load<WeaponDefinition>(RiflePath);
            Assert.That((rifle.displayName, rifle.damage, rifle.roundsPerMinute, rifle.magazineSize, rifle.reloadTime),
                Is.EqualTo(("プロトAR", 18f, 600f, 30, 1.7f)), "the previous rifle's data is not overwritten");
            Assert.That((rifle.range, rifle.falloffStart, rifle.minDamageFactor, rifle.headshotMultiplier), Is.EqualTo((150f, 40f, 0.6f, 2f)));
            Assert.That(rifle.chargeBurst.enabled, Is.False);
        }

        [Test]
        public void Brand_IgnitesOnTheSixthHit_AndResetsOnSwitchOrPause()
        {
            var model = new ChargeBurstModel(Brand());
            var a = new object();
            var b = new object();
            for (int i = 0; i < 5; i++)
            {
                Assert.That(model.RegisterHit(a), Is.False);
                model.Tick(0.125f); // 480 rpm
            }

            Assert.That(model.RegisterHit(a), Is.True, "the 6th consecutive hit ignites");
            Assert.That(model.Stacks, Is.EqualTo(0));

            model.Tick(5f);
            for (int i = 0; i < 3; i++) model.RegisterHit(a);
            model.RegisterHit(b);
            Assert.That((model.Target, model.Stacks), Is.EqualTo((b, 1)), "another target starts over");

            model.Tick(2.2f);
            model.RegisterHit(b);
            Assert.That(model.Stacks, Is.EqualTo(2), "2.2 s is within the 2.25 s gap");
            model.Tick(2.3f);
            model.RegisterHit(b);
            Assert.That(model.Stacks, Is.EqualTo(1), "a longer pause resets");
        }

        [Test]
        public void Brand_WaitsForTheFourSecondCooldown_OnTheSameTarget()
        {
            var model = new ChargeBurstModel(Brand());
            var a = new object();
            for (int i = 0; i < 6; i++) model.RegisterHit(a);
            Assert.That(model.Discharges, Is.EqualTo(1));

            for (int i = 0; i < 20; i++) // 2.5 s of hits: full, but cooling down
            {
                model.Tick(0.125f);
                Assert.That(model.RegisterHit(a), Is.False);
            }

            Assert.That(model.WaitingForCooldown, Is.True);
            model.Tick(1.6f); // past 4 s since the ignition (the gap check: 1.6 s < 2.25 s)
            Assert.That(model.RegisterHit(a), Is.True, "the first hit after the cooldown ignites");
            Assert.That(model.Discharges, Is.EqualTo(2));
        }

        [Test]
        public void ADefaultSpec_IsAChainWithoutStatus_SoOldDataIsUnchanged()
        {
            var spec = new ChargeBurstSpec();
            Assert.That(spec.shape, Is.EqualTo(ChargeBurstShape.Chain));
            Assert.That((spec.splashRadius, spec.splashTargets, spec.splashDamage, spec.statusDamagePerSecond, spec.statusDuration),
                Is.EqualTo((0f, 0, 0f, 0f, 0f)));
        }
    }
}
