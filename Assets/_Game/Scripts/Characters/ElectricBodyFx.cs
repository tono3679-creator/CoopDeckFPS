using System.Collections.Generic;
using CoopDeck.Animation;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.Characters
{
    /// <summary>
    /// The electric character's own lightning (the Lightning Rabbit's kit), driven by what its animator driver and its
    /// weapon report.
    /// Gameplay is untouched; this only looks:
    ///   glow       the glowing marks (CH_GlowLines on copies of the glowing triangles, body and first-person arms)
    ///              breathe slowly, burn brighter while sprinting and as the weapon's consecutive-hit charge builds
    ///              (the Thunderhop's 帯電連射), and flare on casts, dashes, hits and the charge's discharge (<see cref="ElectricPulse"/>)
    ///   sparks     small sparks jump from the glove lenses: about once a second at rest, several a second when busy
    ///   body only  short static arcs at the ear tips and blue afterimages left behind by a dash. These play only while
    ///              the body is drawn (others see it; the local player sees only its shadow and the first-person arms)
    /// Quality (<see cref="PerformanceSettings.Vfx"/>): Low keeps the glow and the sparks of casts and dashes; Medium adds
    /// the idle sparks and two afterimages per dash; High adds the ear arcs and a third afterimage.
    /// </summary>
    [DisallowMultipleComponent]
    public class ElectricBodyFx : MonoBehaviour, ICharacterKitListener
    {
        static readonly int GlowBoostId = Shader.PropertyToID("_GlowBoost");
        static readonly int FadeId = Shader.PropertyToID("_Fade");

        [Header("Glowing marks")]
        [SerializeField] Renderer[] bodyGlow = new Renderer[0];
        [SerializeField] Renderer[] viewGlow = new Renderer[0];

        [Header("Body (seen by others)")]
        [Tooltip("The body's surface renderers (every LOD): body effects play only while one of them is drawn.")]
        [SerializeField] Renderer[] bodySurfaces = new Renderer[0];
        [SerializeField] Transform[] bodyHands = new Transform[0];
        [SerializeField] Transform[] earTips = new Transform[0];
        [Tooltip("Baked into the dash afterimages (a low LOD is enough).")]
        [SerializeField] SkinnedMeshRenderer ghostSource;
        [SerializeField] Material ghostMaterial;

        [Header("First-person")]
        [SerializeField] Transform[] viewHands = new Transform[0];

        [Header("Effects")]
        [Tooltip("Sparks at the first-person gloves (sized for 0.3-0.5 m from the eyes).")]
        [SerializeField] GameObject sparkFx;
        [Tooltip("Sparks at the body's gloves (sized to read from a few metres).")]
        [SerializeField] GameObject bodySparkFx;
        [SerializeField] GameObject arcFx;
        [SerializeField, Range(0.01f, 0.2f)] float earArcLength = 0.07f;
        [SerializeField, Min(0.05f)] float ghostLife = 0.32f;
        [SerializeField, Min(0.01f)] float ghostInterval = 0.05f;

        sealed class Ghost
        {
            public GameObject GameObject;
            public Mesh Mesh;
            public MeshRenderer Renderer;
            public float Age;
        }

        readonly ElectricPulse pulse = new ElectricPulse(7);
        readonly List<Ghost> ghosts = new List<Ghost>();
        MaterialPropertyBlock block;
        CharacterAnimatorDriver driver;
        HitscanWeapon weapon;
        Transform ghostRoot;
        int pendingGhosts;
        float ghostTimer;

        public float Boost => pulse.Boost;
        public ElectricPulse Pulse => pulse;
        public IReadOnlyList<Renderer> BodyGlow => bodyGlow;
        public IReadOnlyList<Renderer> ViewGlow => viewGlow;
        public IReadOnlyList<Transform> BodyHands => bodyHands;
        public IReadOnlyList<Transform> ViewHands => viewHands;
        public IReadOnlyList<Transform> EarTips => earTips;
        public CharacterAnimatorDriver Driver => driver;
        public HitscanWeapon Weapon => weapon;

        /// <summary>Counters for checks: sparks, ear arcs and afterimages played since this component woke up.</summary>
        public int SparksPlayed { get; private set; }
        public int ArcsPlayed { get; private set; }
        public int GhostsShown { get; private set; }
        public int BurstsSeen { get; private set; }

        public int ActiveGhosts
        {
            get
            {
                int n = 0;
                foreach (Ghost g in ghosts)
                {
                    if (g.GameObject != null && g.GameObject.activeSelf) n++;
                }

                return n;
            }
        }

        public void Configure(Renderer[] bodyGlowRenderers, Renderer[] viewGlowRenderers, Renderer[] surfaces, Transform[] hands,
            Transform[] ears, SkinnedMeshRenderer ghostMesh, Material ghost, Transform[] firstPersonHands, GameObject spark, GameObject bodySpark, GameObject arc)
        {
            bodyGlow = bodyGlowRenderers;
            viewGlow = viewGlowRenderers;
            bodySurfaces = surfaces;
            bodyHands = hands;
            earTips = ears;
            ghostSource = ghostMesh;
            ghostMaterial = ghost;
            viewHands = firstPersonHands;
            sparkFx = spark;
            bodySparkFx = bodySpark;
            arcFx = arc;
        }

        public void OnKitApplied(CharacterAnimatorDriver animatorDriver) => Bind(animatorDriver);

        void OnEnable()
        {
            if (driver == null)
            {
                Bind(GetComponentInParent<CharacterAnimatorDriver>());
            }
        }

        void OnDisable() => Bind(null);

        void OnDestroy()
        {
            foreach (Ghost g in ghosts)
            {
                if (g.Mesh != null) Destroy(g.Mesh);
            }

            if (ghostRoot != null)
            {
                Destroy(ghostRoot.gameObject);
            }
        }

        void Bind(CharacterAnimatorDriver next)
        {
            if (driver == next)
            {
                return;
            }

            if (driver != null)
            {
                driver.SkillStarted -= OnSkill;
                driver.Dashed -= OnDash;
                driver.Hurt -= OnHurt;
            }

            if (weapon != null)
            {
                weapon.ChargeBurstTriggered -= OnChargeBurst;
            }

            driver = next;
            weapon = driver != null ? driver.GetComponentInParent<HitscanWeapon>() : null; // the player's gun sits beside its driver
            if (driver != null && isActiveAndEnabled)
            {
                driver.SkillStarted += OnSkill;
                driver.Dashed += OnDash;
                driver.Hurt += OnHurt;
                if (weapon != null)
                {
                    weapon.ChargeBurstTriggered += OnChargeBurst;
                }
            }
        }

        // ---------------------------------------------------------------- events

        void OnSkill(SkillMotion motion)
        {
            pulse.Kick(ElectricPulse.CastSpike);
            SparkBurst(2);
        }

        void OnDash(Vector3 direction)
        {
            pulse.Kick(ElectricPulse.DashSpike);
            SparkBurst(2);
            int count = PerformanceSettings.Vfx >= VfxQuality.High ? 3 : PerformanceSettings.Vfx >= VfxQuality.Medium ? 2 : 0;
            if (count > 0 && BodyDrawn())
            {
                pendingGhosts = count;
                ghostTimer = 0f;
            }
        }

        void OnHurt(bool heavy) => pulse.Kick(heavy ? ElectricPulse.HeavyHitSpike : ElectricPulse.HitSpike);

        /// <summary>The weapon's consecutive-hit charge discharged (VOLT-9 Thunderhop): the marks flare, both gloves spark.</summary>
        void OnChargeBurst(ChargeBurstReport report)
        {
            BurstsSeen++;
            pulse.Kick(ElectricPulse.BurstSpike);
            SparkBurst(3);
            if (PerformanceSettings.Vfx >= VfxQuality.Medium && BodyDrawn())
            {
                EarArc();
            }
        }

        /// <summary>The weapon's consecutive-hit charge (0-1), or 0 when it has none.</summary>
        float WeaponCharge()
        {
            ChargeBurstModel model = weapon != null ? weapon.ChargeBurst : null;
            return model != null && model.Enabled ? model.Progress : 0f;
        }

        // ---------------------------------------------------------------- frame

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            pulse.Step(dt, driver != null && driver.IsSprinting, WeaponCharge());
            SyncBodyGlow();
            ApplyGlow();

            if (pulse.TakeSpark() && PerformanceSettings.Vfx >= VfxQuality.Medium)
            {
                SparkBurst(1);
                if (PerformanceSettings.Vfx >= VfxQuality.High && BodyDrawn() && Random.value < 0.5f)
                {
                    EarArc();
                }
            }

            UpdateGhosts(dt);
        }

        void ApplyGlow()
        {
            block ??= new MaterialPropertyBlock();
            block.Clear();
            block.SetFloat(GlowBoostId, pulse.Boost);
            Apply(bodyGlow);
            Apply(viewGlow);

            void Apply(Renderer[] renderers)
            {
                foreach (Renderer r in renderers)
                {
                    if (r != null) r.SetPropertyBlock(block);
                }
            }
        }

        /// <summary>
        /// The body's glow copies follow its surfaces: off while the body only casts a shadow (the local player's own body),
        /// so they are not skinned for nothing.
        /// </summary>
        void SyncBodyGlow()
        {
            bool drawn = false;
            foreach (Renderer r in bodySurfaces)
            {
                if (r != null && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                {
                    drawn = true;
                    break;
                }
            }

            foreach (Renderer r in bodyGlow)
            {
                if (r != null && r.enabled != drawn) r.enabled = drawn;
            }
        }

        /// <summary>True while any of the body's surfaces is drawn by a camera (not just its shadow).</summary>
        public bool BodyDrawn()
        {
            foreach (Renderer r in bodySurfaces)
            {
                if (r != null && r.enabled && r.isVisible && r.shadowCastingMode != ShadowCastingMode.ShadowsOnly)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True while the first-person arms' glow is drawn (the local player's view).</summary>
        public bool ViewDrawn()
        {
            foreach (Renderer r in viewGlow)
            {
                if (r != null && r.enabled && r.isVisible)
                {
                    return true;
                }
            }

            return false;
        }

        void SparkBurst(int perHand)
        {
            if (ViewDrawn())
            {
                SparkAt(viewHands, perHand, sparkFx);
            }

            if (BodyDrawn())
            {
                SparkAt(bodyHands, perHand, bodySparkFx != null ? bodySparkFx : sparkFx);
            }
        }

        void SparkAt(Transform[] hands, int count, GameObject prefab)
        {
            if (prefab == null || hands.Length == 0)
            {
                return;
            }

            for (int i = 0; i < count; i++)
            {
                Transform hand = hands[Random.Range(0, hands.Length)];
                if (hand == null || !hand.gameObject.activeInHierarchy)
                {
                    continue;
                }

                Vector3 at = hand.position + Random.insideUnitSphere * 0.01f;
                FxPool.Spawn(prefab, at, Random.rotation, hand, hand.gameObject.layer);
                SparksPlayed++;
            }
        }

        void EarArc()
        {
            if (arcFx == null || earTips.Length == 0)
            {
                return;
            }

            Transform tip = earTips[Random.Range(0, earTips.Length)];
            if (tip == null)
            {
                return;
            }

            Vector3 start = tip.position;
            Vector3 away = (tip.position - tip.parent.position).normalized;
            Vector3 end = start + (away + Random.insideUnitSphere * 0.9f).normalized * (earArcLength * Random.Range(0.6f, 1f));
            PooledFx fx = FxPool.Spawn(arcFx, start, Quaternion.identity, null, tip.gameObject.layer);
            if (fx != null && fx.TryGetComponent(out LightningArcFx arc))
            {
                arc.Play(start, end);
                ArcsPlayed++;
            }
        }

        // ---------------------------------------------------------------- dash afterimages

        void UpdateGhosts(float dt)
        {
            if (pendingGhosts > 0)
            {
                ghostTimer -= dt;
                if (ghostTimer <= 0f)
                {
                    ShowGhost();
                    pendingGhosts--;
                    ghostTimer = ghostInterval;
                }
            }

            block ??= new MaterialPropertyBlock();
            foreach (Ghost g in ghosts)
            {
                if (g.GameObject == null || !g.GameObject.activeSelf)
                {
                    continue;
                }

                g.Age += dt;
                float fade = 1f - g.Age / ghostLife;
                if (fade <= 0f)
                {
                    g.GameObject.SetActive(false);
                    continue;
                }

                block.Clear();
                block.SetFloat(FadeId, fade * fade);
                g.Renderer.SetPropertyBlock(block);
            }
        }

        void ShowGhost()
        {
            if (ghostSource == null || ghostMaterial == null)
            {
                return;
            }

            Ghost ghost = null;
            foreach (Ghost g in ghosts)
            {
                if (g.GameObject != null && !g.GameObject.activeSelf)
                {
                    ghost = g;
                    break;
                }
            }

            if (ghost == null)
            {
                if (ghostRoot == null)
                {
                    ghostRoot = new GameObject(name + " Afterimages").transform; // in the world: left behind by the dash
                }

                var go = new GameObject("Afterimage" + ghosts.Count);
                go.transform.SetParent(ghostRoot, false);
                var mesh = new Mesh { name = "Afterimage" };
                mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ghostMaterial;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                ghost = new Ghost { GameObject = go, Mesh = mesh, Renderer = renderer };
                ghosts.Add(ghost);
            }

            Transform source = ghostSource.transform;
            ghostSource.BakeMesh(ghost.Mesh, true);
            ghost.GameObject.layer = ghostSource.gameObject.layer;
            ghost.GameObject.transform.SetPositionAndRotation(source.position, source.rotation);
            ghost.Age = 0f;
            ghost.GameObject.SetActive(true);
            GhostsShown++;
        }
    }
}
