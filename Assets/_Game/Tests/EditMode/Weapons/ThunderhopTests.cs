using CoopDeck.Characters;
using CoopDeck.Core;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The Lightning Rabbit's VOLT-9 Thunderhop (Docs/THUNDERHOP_SMG.md): the 帯電連射 charge rules,
    /// the design JSON converted into the weapon data, the falloff end, the starting-weapon link,
    /// and that the fox rifle kept its numbers.
    /// </summary>
    public class ThunderhopTests
    {
        const float Shot = 60f / 900f;

        static ChargeBurstSpec Spec(bool enabled = true) => new ChargeBurstSpec
        {
            enabled = enabled,
            requiredStacks = 12,
            maxSecondsBetweenHits = 2f,
            stacksPerHit = 1,
            primaryDamage = 18f,
            chainTargets = 1,
            chainRange = 6f,
            chainDamage = 12f,
            element = Element.Lightning,
            perTargetCooldown = 1.25f,
        };

        static int Hit(ChargeBurstModel model, object target, int hits)
        {
            int discharges = 0;
            for (int i = 0; i < hits; i++)
            {
                model.Tick(Shot);
                if (model.RegisterHit(target))
                {
                    discharges++;
                }
            }

            return discharges;
        }

        // ------------------------------------------------------------ charge rules

        [Test]
        public void TwelveConsecutiveHits_Discharge_AndTheChargeStartsOver()
        {
            var model = new ChargeBurstModel(Spec());
            var enemy = new object();
            Assert.That(Hit(model, enemy, 11), Is.EqualTo(0));
            Assert.That(model.Stacks, Is.EqualTo(11));
            Assert.That(model.Progress, Is.EqualTo(11f / 12f).Within(1e-4f));
            Assert.That(model.RegisterHit(enemy), Is.True, "the 12th hit discharges");
            Assert.That(model.Stacks, Is.EqualTo(0));
            Assert.That(model.Discharges, Is.EqualTo(1));
        }

        [Test]
        public void ChangingTarget_OrPausingTooLong_ResetsTheCharge()
        {
            var model = new ChargeBurstModel(Spec());
            var a = new object();
            var b = new object();
            Hit(model, a, 6);
            Assert.That(Hit(model, b, 1), Is.EqualTo(0));
            Assert.That(model.Stacks, Is.EqualTo(1), "another target starts from one");
            Assert.That(model.Target, Is.SameAs(b));

            Hit(model, b, 5);
            model.Tick(1.9f);
            Hit(model, b, 1);
            Assert.That(model.Stacks, Is.EqualTo(7), "a pause under 2 s keeps the charge");

            model.Tick(2.05f);
            Assert.That(model.Stacks, Is.EqualTo(0), "over 2 s without a hit: the charge is gone");
            Assert.That(model.Target, Is.Null);
        }

        [Test]
        public void PerTargetCooldown_HoldsAFullCharge_UntilItEnds_OtherTargetsAreFree()
        {
            var model = new ChargeBurstModel(Spec());
            var a = new object();
            var b = new object();
            Assert.That(Hit(model, a, 12), Is.EqualTo(1));

            // Twelve more hits take 0.8 s: still inside the 1.25 s cooldown, so the charge waits full.
            Assert.That(Hit(model, a, 12), Is.EqualTo(0));
            Assert.That(model.Stacks, Is.EqualTo(12));
            Assert.That(model.WaitingForCooldown, Is.True);

            int extra = 0;
            while (!model.RegisterHit(a))
            {
                model.Tick(Shot);
                extra++;
                Assert.That(extra, Is.LessThan(20));
            }

            Assert.That(model.Discharges, Is.EqualTo(2), "fires on the first hit after the cooldown");
            Assert.That(model.CooldownRemaining(a), Is.EqualTo(1.25f).Within(1e-4f));

            Assert.That(Hit(model, b, 12), Is.EqualTo(1), "another target is not on a's cooldown");
        }

        [Test]
        public void DisabledSpec_NeverDischarges()
        {
            var model = new ChargeBurstModel(Spec(false));
            Assert.That(Hit(model, new object(), 40), Is.EqualTo(0));
            Assert.That(model.Stacks, Is.EqualTo(0));
        }

        // ------------------------------------------------------------ data

        [Test]
        public void FalloffEnd_IsSeparateFromRange_AndZeroKeepsTheOldBehaviour()
        {
            var smg = ScriptableObject.CreateInstance<WeaponDefinition>();
            var rifle = ScriptableObject.CreateInstance<WeaponDefinition>();
            try
            {
                smg.range = 55f;
                smg.falloffStart = 10f;
                smg.falloffEnd = 24f;
                smg.minDamageFactor = 0.6f;
                Assert.That(smg.FalloffAt(5f), Is.EqualTo(1f));
                Assert.That(smg.FalloffAt(17f), Is.EqualTo(0.8f).Within(1e-4f));
                Assert.That(smg.FalloffAt(24f), Is.EqualTo(0.6f).Within(1e-4f));
                Assert.That(smg.FalloffAt(50f), Is.EqualTo(0.6f).Within(1e-4f), "flat after the end, up to the range");

                rifle.range = 150f;
                rifle.falloffStart = 40f;
                rifle.minDamageFactor = 0.6f;
                Assert.That(rifle.FalloffEnd, Is.EqualTo(150f), "0 = the range, as before");
                Assert.That(rifle.FalloffAt(95f), Is.EqualTo(0.8f).Within(1e-4f));
            }
            finally
            {
                Object.DestroyImmediate(smg);
                Object.DestroyImmediate(rifle);
            }
        }

        [Test]
        public void ThunderhopAsset_HoldsTheDesignJson_InTheWeaponFormat()
        {
            var w = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Game/ScriptableObjects/Weapons/WD_Volt9Thunderhop.asset");
            Assert.That(w, Is.Not.Null, "run ThunderhopAssetFactory (bootstrap)");
            Assert.That(w.weaponId, Is.EqualTo("weapon.volt9_thunderhop"));
            Assert.That(w.displayName, Is.EqualTo("VOLT-9 サンダーホップ"));
            Assert.That(w.element, Is.EqualTo(Element.Lightning));
            Assert.That(w.damage, Is.EqualTo(11f), "JSON 8, tuned (THUNDERHOP_SMG.md §5)");
            Assert.That(w.roundsPerMinute, Is.EqualTo(900f));
            Assert.That(w.automatic, Is.True);
            Assert.That(w.magazineSize, Is.EqualTo(40));
            Assert.That(w.reloadTime, Is.EqualTo(1.65f).Within(1e-4f));
            Assert.That(w.headshotMultiplier, Is.EqualTo(1.5f));
            Assert.That(w.range, Is.EqualTo(55f));
            Assert.That(w.falloffStart, Is.EqualTo(10f));
            Assert.That(w.FalloffEnd, Is.EqualTo(24f));
            Assert.That(w.minDamageFactor, Is.EqualTo(0.6f).Within(1e-4f));
            Assert.That(w.baseSpread, Is.EqualTo(0f), "hip fire has no spread (user, 2026-11-08: no ADS, fights are hip fire)");
            Assert.That(w.spreadPerShot, Is.EqualTo(0f));
            Assert.That(w.maxBloom, Is.EqualTo(0f));
            Assert.That(w.movingSpread, Is.EqualTo(0f));
            Assert.That(w.airborneSpread, Is.EqualTo(1f), "jumping keeps its own spread"); 
            Assert.That(w.recoilPitch, Is.EqualTo(0.5f).Within(1e-4f), "JSON 0.8, tuned");
            Assert.That(w.recoilYaw, Is.EqualTo(0.3f).Within(1e-4f));
            Assert.That(w.recoilRecoverySeconds, Is.EqualTo(0.16f).Within(1e-4f));

            ChargeBurstSpec c = w.chargeBurst;
            Assert.That(c.enabled, Is.True);
            Assert.That(c.displayName, Is.EqualTo("帯電連射"));
            Assert.That(c.requiredStacks, Is.EqualTo(12));
            Assert.That(c.maxSecondsBetweenHits, Is.EqualTo(2f));
            Assert.That(c.stacksPerHit, Is.EqualTo(1));
            Assert.That(c.primaryDamage, Is.EqualTo(18f));
            Assert.That(c.chainTargets, Is.EqualTo(1));
            Assert.That(c.chainRange, Is.EqualTo(6f));
            Assert.That(c.chainDamage, Is.EqualTo(12f));
            Assert.That(c.element, Is.EqualTo(Element.Lightning));
            Assert.That(c.perTargetCooldown, Is.EqualTo(1.25f).Within(1e-4f));
            Assert.That(w.muzzleFlashPrefab, Is.Not.Null);
            Assert.That(w.tracerPrefab, Is.Not.Null);
            Assert.That(c.burstFx, Is.Not.Null);
            Assert.That(c.arcFx, Is.Not.Null);
            Assert.That(c.chargeFx, Is.Not.Null);
        }

        [Test]
        public void Rabbit_StartsWithTheThunderhop_TheFoxKeepsItsRifle()
        {
            var thunderhop = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Game/ScriptableObjects/Weapons/WD_Volt9Thunderhop.asset");
            var rabbit = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/_Game/ScriptableObjects/Characters/CD_LightningRabbit.asset");
            var fox = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/_Game/ScriptableObjects/Characters/CD_FoxGunner.asset");
            Assert.That(rabbit, Is.Not.Null);
            Assert.That(fox, Is.Not.Null);
            Assert.That(rabbit.StartingWeapon, Is.SameAs(thunderhop));
            Assert.That(fox.StartingWeapon, Is.SameAs(AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Game/ScriptableObjects/Weapons/WD_Ignis27Blazetail.asset")),
                "the fox starts with the IGNIS-27 (Docs/IGNIS27_RIFLE.md); its old rifle's data below stays as it was");

            var rifle = AssetDatabase.LoadAssetAtPath<WeaponDefinition>("Assets/_Game/ScriptableObjects/Weapons/WD_AssaultRifle.asset");
            Assert.That(rifle.damage, Is.EqualTo(18f));
            Assert.That(rifle.roundsPerMinute, Is.EqualTo(600f));
            Assert.That(rifle.magazineSize, Is.EqualTo(30));
            Assert.That(rifle.reloadTime, Is.EqualTo(1.7f).Within(1e-4f));
            Assert.That(rifle.headshotMultiplier, Is.EqualTo(2f));
            Assert.That(rifle.FalloffEnd, Is.EqualTo(rifle.range), "no falloff end: unchanged falloff");
            Assert.That(rifle.recoilRecoverySeconds, Is.EqualTo(0f), "the look's default recovery");
            Assert.That(rifle.chargeBurst.enabled, Is.False);
        }
    }
}
