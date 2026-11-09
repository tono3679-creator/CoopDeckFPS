using System.Collections;
using CoopDeck.Core;
using CoopDeck.Effects;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CoopDeck.Tests
{
    /// <summary>
    /// The stylized lightning prefabs in play: the first strike shows whole at once with more branches than a chained hop
    /// and is gone in about 0.1 s; a hop's tip travels before it shows whole and gets its own core and edge widths; the
    /// hit leaves 0.1–0.2 s of surface arcs, and the amounts follow the effect quality.
    /// </summary>
    public class LightningVfxPlayModeTests
    {
        const string Prefabs = "Assets/_Game/Prefabs/VFX/Lightning/";
        VfxQuality previous;

        [SetUp]
        public void SetUp()
        {
            previous = PerformanceSettings.Vfx;
        }

        [TearDown]
        public void TearDown()
        {
            PerformanceSettings.SetVfxQuality(previous);
        }

        static GameObject Load(string name)
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + name + ".prefab");
#else
            return null;
#endif
        }

        [UnityTest]
        public IEnumerator FirstStrike_ShowsAtOnce_AndGoesQuickly_AChainedHopTravels()
        {
            PerformanceSettings.SetVfxQuality(VfxQuality.High);
            GameObject prefab = Load("FX_Lightning_Bolt");
            Assert.That(prefab, Is.Not.Null);

            LightningBoltFx first = FxPool.Spawn(prefab, Vector3.zero, Quaternion.identity).GetComponent<LightningBoltFx>();
            first.Play(new Vector3(0f, 1.5f, 0f), new Vector3(0f, 1f, 10f), 1f, 11);
            LightningBoltFx chain = FxPool.Spawn(prefab, Vector3.zero, Quaternion.identity).GetComponent<LightningBoltFx>();
            chain.Play(new Vector3(0f, 1f, 10f), new Vector3(3f, 1f, 11f), 0.65f, 12, 0.2f);

            var firstMain = first.transform.Find("Main").GetComponent<LineRenderer>();
            var chainMain = chain.transform.Find("Main").GetComponent<LineRenderer>();
            Assert.That(first.Arrived, Is.True, "the first strike shows whole at once");
            Assert.That(first.MainPointCount, Is.GreaterThanOrEqualTo(7), "a zig-zag, not a straight line");
            Assert.That(chain.Arrived, Is.False);
            Assert.That(chain.MainPointCount, Is.LessThan(first.MainPointCount), "the hop's tip has only just left");
            Assert.That(first.ActiveBranches, Is.EqualTo(4));
            Assert.That(chain.ActiveBranches, Is.EqualTo(2));
            Assert.That(firstMain.HasPropertyBlock(), Is.False, "the first strike uses its material as is");
            Assert.That(chainMain.HasPropertyBlock(), Is.True, "a hop has its own core and edge widths");
            Assert.That(first.Duration, Is.InRange(0.08f, 0.12f));
            yield return null;
            Assert.That(firstMain.enabled, Is.True);

            yield return new WaitForSeconds(0.21f); // past its 0.2 s travel, inside its 0.09 s fade
            Assert.That(chain.Arrived, Is.True);
            Assert.That(chain.Playing, Is.True, "it shows whole for its fade after arriving");
            Assert.That(first.Playing, Is.False, "the first strike is long gone");

            yield return new WaitForSeconds(0.15f);
            Assert.That(first.Playing, Is.False);
            Assert.That(firstMain.enabled, Is.False);
        }

        [UnityTest]
        public IEnumerator Hit_LeavesShortSurfaceArcs_ByQuality()
        {
            GameObject prefab = Load("FX_Lightning_Hit");
            Assert.That(prefab, Is.Not.Null);
            foreach (VfxQuality quality in new[] { VfxQuality.High, VfxQuality.Medium, VfxQuality.Low })
            {
                PerformanceSettings.SetVfxQuality(quality);
                PooledFx hit = FxPool.Spawn(prefab, new Vector3(0f, 1f, 5f), Quaternion.identity);
                var residual = hit.GetComponentInChildren<SurfaceArcFx>();
                yield return null;
                Assert.That(residual.Playing, Is.True);
                Assert.That(residual.ActiveArcs, Is.EqualTo(LightningPath.ResidualArcCount(quality)));
                Assert.That(hit.transform.Find("GroundRing").gameObject.activeSelf, Is.EqualTo(quality >= VfxQuality.Medium), "floor ring from Medium up");
                yield return new WaitForSeconds(0.21f); // past its 0.2 s travel, inside its 0.09 s fade
                Assert.That(residual.Playing, Is.False, "the current is gone within 0.2 s");
            }
        }
    }
}
