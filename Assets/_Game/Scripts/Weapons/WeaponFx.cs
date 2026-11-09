using System.Collections.Generic;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Effects;
using UnityEngine;

namespace CoopDeck.Weapons
{
    /// <summary>
    /// Plays muzzle flash, tracer and impact effects for a <see cref="HitscanWeapon"/>, and for a
    /// weapon with a <see cref="ChargeBurstSpec"/>: a crackle at the muzzle while the charge is half
    /// full or more, and the discharge (Docs/LIGHTNING_VFX.md). A lightning discharge (shape Chain) plays in two beats:
    /// the bolt lands and the first enemy bursts on the discharge frame (the shooter's own camera jolts), then after
    /// <see cref="DischargeTiming"/> an arc runs to each chained enemy and it bursts as the arc arrives, its hit flash
    /// moved to that moment. A Splash discharge (IGNIS-27) bursts everywhere at once. Only the display is timed: damage,
    /// targets and element hits all happen in the discharge frame (<see cref="HitscanWeapon"/>).
    /// Swapping the prefabs on the <see cref="WeaponDefinition"/> changes the look without code.
    /// </summary>
    [RequireComponent(typeof(HitscanWeapon))]
    public class WeaponFx : MonoBehaviour
    {
        [SerializeField] HitscanWeapon weapon;
        [Tooltip("Layer for the muzzle flash. Use the ViewModel layer for first-person weapons, -1 to keep the prefab's layer.")]
        [SerializeField] int muzzleFlashLayer = -1;
        [SerializeField, Min(0)] int prewarmCount = 6;

        [Header("Lightning discharge (shape Chain; display only)")]
        [SerializeField] DischargeTiming dischargeTiming = DischargeTiming.Default;
        [Tooltip("Hit flash on the first enemy as it bursts (1 = a normal hit; lower keeps its outline readable).")]
        [SerializeField, Range(0f, 1f)] float primaryFlashStrength = 0.5f;
        [Tooltip("Hit flash on each chained enemy, shown when the arc reaches it (not with its damage).")]
        [SerializeField, Range(0f, 1f)] float chainFlashStrength = 0.45f;
        [Tooltip("The discharging player's own camera jolt, degrees (x < 0 tips the view up; y alternates sides).")]
        [SerializeField] Vector3 dischargeKickEuler = new Vector3(-0.7f, 0.25f, 0f);
        [SerializeField] Vector3 dischargeKickOffset = new Vector3(0f, 0f, -0.02f);
        [SerializeField, Range(0.03f, 0.2f)] float dischargeKickDuration = 0.075f;

        WeaponDefinition prewarmed;
        int chargeShot;
        int arcSeed;
        CameraShaker ownView;
        bool viewLookedUp;
        readonly List<Hop> hops = new List<Hop>(4);
        readonly List<HitZone> zoneBuffer = new List<HitZone>(8);
        readonly List<Collider> colliderBuffer = new List<Collider>(8);
        readonly List<IHitFlashView> flashBuffer = new List<IHitFlashView>(2);

        /// <summary>A chained hop waiting for its moment on screen.</summary>
        struct Hop
        {
            public float ArcAt;
            public float ArriveAt;
            public bool ArcPlayed;
            public bool Done;
            public int Previous;          // index of the hop before (-1 = it starts at the first strike's contact)
            public Vector3 From;          // the first strike's contact
            public Health Target;
            public Vector3 TargetStart;   // where the target was at the discharge (the hit follows it while it lives)
            public Vector3 Point;         // contact at the discharge, toward the shooter
            public Vector3 Resolved;      // contact used by the arc and its burst
            public bool Grounded;
            public Vector3 Ground;
            public float Width;
            public float Strength;
            public GameObject Arc;
            public GameObject Hit;
        }

        /// <summary>Chained hops still waiting to be shown (display only).</summary>
        public int PendingHops
        {
            get
            {
                int count = 0;
                foreach (Hop hop in hops)
                {
                    count += hop.Done ? 0 : 1;
                }

                return count;
            }
        }

        public DischargeTiming Timing => dischargeTiming;

        void Awake()
        {
            if (weapon == null)
            {
                weapon = GetComponent<HitscanWeapon>();
            }
        }

        void Start()
        {
            Prewarm();
        }

        void Prewarm()
        {
            WeaponDefinition def = weapon.Definition;
            if (def == null || def == prewarmed)
            {
                return;
            }

            prewarmed = def;
            FxPool.Prewarm(def.tracerPrefab, prewarmCount);
            FxPool.Prewarm(def.impactPrefab, prewarmCount);
            FxPool.Prewarm(def.damageableImpactPrefab, prewarmCount);
            FxPool.Prewarm(def.muzzleFlashPrefab, 2);
            if (def.chargeBurst.enabled)
            {
                FxPool.Prewarm(def.chargeBurst.chargeFx, 3);
                FxPool.Prewarm(def.chargeBurst.burstFx, 2);
                FxPool.Prewarm(def.chargeBurst.chainBurstFx, 2);
                FxPool.Prewarm(def.chargeBurst.arcFx, 2 * Mathf.Max(1, def.chargeBurst.chainTargets));
            }
        }

        void OnEnable()
        {
            weapon.Fired += OnFired;
            weapon.ChargeBurstTriggered += OnChargeBurst;
            weapon.AmmoChanged += Prewarm; // a new definition (character swap) is prewarmed too
        }

        void OnDisable()
        {
            weapon.Fired -= OnFired;
            weapon.ChargeBurstTriggered -= OnChargeBurst;
            weapon.AmmoChanged -= Prewarm;
            hops.Clear();
        }

        public void SetMuzzleFlashLayer(int layer)
        {
            muzzleFlashLayer = layer;
        }

        void OnFired(ShotEvent shot)
        {
            WeaponDefinition def = weapon.Definition;
            if (def == null)
            {
                return;
            }

            Transform muzzle = weapon.Muzzle;
            if (def.muzzleFlashPrefab != null && muzzle != null)
            {
                FxPool.Spawn(def.muzzleFlashPrefab, muzzle.position, muzzle.rotation, muzzle, muzzleFlashLayer);
            }

            // Charge feedback on the weapon: a crackle every other shot once the charge is half full.
            ChargeBurstModel charge = weapon.ChargeBurst;
            if (charge != null && def.chargeBurst.chargeFx != null && muzzle != null && charge.Progress >= 0.5f && (++chargeShot & 1) == 0)
            {
                FxPool.Spawn(def.chargeBurst.chargeFx, muzzle.position, muzzle.rotation, muzzle, muzzleFlashLayer);
            }

            if (def.tracerPrefab != null)
            {
                PooledFx tracer = FxPool.Spawn(def.tracerPrefab, shot.Muzzle, Quaternion.identity);
                if (tracer != null && tracer.TryGetComponent(out IBeamFx tracerFx))
                {
                    tracerFx.Play(shot.Muzzle, shot.End);
                }
            }

            if (shot.HitSomething)
            {
                GameObject impact = shot.HitDamageable && def.damageableImpactPrefab != null
                    ? def.damageableImpactPrefab
                    : def.impactPrefab;
                if (impact != null)
                {
                    FxPool.Spawn(impact, shot.End, Quaternion.LookRotation(shot.Normal));
                }
            }
        }

        void OnChargeBurst(ChargeBurstReport report)
        {
            WeaponDefinition def = weapon.Definition;
            if (def == null)
            {
                return;
            }

            if (def.chargeBurst.shape == ChargeBurstShape.Chain)
            {
                PlayLightning(report, def.chargeBurst);
            }
            else
            {
                PlayAllAtOnce(report, def.chargeBurst);
            }
        }

        /// <summary>
        /// Beat 1 now: the bolt lands on the first enemy and it bursts from the same point (chest flash, shards, floor
        /// ring), softened hit flash, the shooter's camera jolt. Beat 2 later: each chained enemy's arc and burst.
        /// </summary>
        void PlayLightning(ChargeBurstReport report, ChargeBurstSpec spec)
        {
            Transform muzzle = weapon.Muzzle;
            Vector3 eye = muzzle != null ? muzzle.position : report.Point - Vector3.forward;
            Vector3 contact = Toward(report.Point, eye, SurfaceHitPull);

            // The main bolt strikes from above at a slant: across the view it reads thick and clear (a bolt from the
            // muzzle runs along the view, collapses into a clump and hides the aim point). It ends where the burst is.
            if (spec.arcFx != null)
            {
                Vector3 view = report.Point - eye;
                view.y = 0f;
                Vector3 side = view.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, view.normalized) : Vector3.right;
                float lean = (++arcSeed & 1) == 0 ? StrikeLean : -StrikeLean;
                PlayBolt(spec.arcFx, contact + Vector3.up * StrikeHeight + side * lean, contact, 1f, 0f);
            }

            Body(report.Primary, contact, out float width, out bool grounded, out Vector3 ground);
            SpawnImpact(spec.burstFx, contact, width, grounded, ground);
            ShowFlash(report.Primary, primaryFlashStrength, 0f);
            KickOwnView();

            // The report's lists are reused after the event: copy what the later beats need.
            GameObject chainHit = spec.chainBurstFx != null ? spec.chainBurstFx : spec.burstFx;
            float strength = ChainStrength;
            float now = Time.time;
            int first = hops.Count;
            for (int i = 0; i < report.ChainPoints.Count; i++)
            {
                Health target = i < report.Chained.Count ? report.Chained[i] : null;
                Vector3 point = Toward(report.ChainPoints[i], eye, CentreHitPull);
                Body(target, point, out float hopWidth, out bool hopGrounded, out Vector3 hopGround);
                hops.Add(new Hop
                {
                    ArcAt = now + dischargeTiming.ArcStart(i),
                    ArriveAt = now + dischargeTiming.Arrival(i),
                    Previous = i == 0 ? -1 : first + i - 1,
                    From = contact,
                    Target = target,
                    TargetStart = target != null ? target.transform.position : point,
                    Point = point,
                    Grounded = hopGrounded,
                    Ground = hopGround,
                    Width = hopWidth,
                    Strength = strength,
                    Arc = spec.arcFx,
                    Hit = chainHit,
                });

                // The chained enemy lights up when the arc reaches it, not now with its damage (two bodies at once).
                ShowFlash(target, chainFlashStrength, dischargeTiming.Arrival(i));
                strength *= 0.85f;
            }
        }

        void Update()
        {
            if (hops.Count == 0)
            {
                return;
            }

            float now = Time.time;
            bool pending = false;
            for (int i = 0; i < hops.Count; i++)
            {
                Hop hop = hops[i];
                if (hop.Done)
                {
                    continue;
                }

                if (!hop.ArcPlayed && now >= hop.ArcAt)
                {
                    hop.Resolved = Follow(hop, hop.Point);
                    Vector3 from = hop.Previous >= 0 ? hops[hop.Previous].Resolved : hop.From;
                    if (hop.Arc != null)
                    {
                        PlayBolt(hop.Arc, from, hop.Resolved, hop.Strength, dischargeTiming.chainTravel);
                    }

                    hop.ArcPlayed = true;
                }

                if (hop.ArcPlayed && now >= hop.ArriveAt)
                {
                    SpawnImpact(hop.Hit, hop.Resolved, hop.Width, hop.Grounded, Follow(hop, hop.Ground));
                    hop.Done = true;
                }

                hops[i] = hop;
                pending |= !hop.Done;
            }

            if (!pending)
            {
                hops.Clear();
            }
        }

        /// <summary>A point stored at the discharge, moved with the target while it still exists (else where it was).</summary>
        static Vector3 Follow(in Hop hop, Vector3 point)
        {
            Health target = hop.Target;
            return target != null && target.gameObject.activeInHierarchy ? point + (target.transform.position - hop.TargetStart) : point;
        }

        /// <summary>A Splash discharge (IGNIS-27): every burst at once, no lightning timing.</summary>
        void PlayAllAtOnce(ChargeBurstReport report, ChargeBurstSpec spec)
        {
            Transform muzzle = weapon.Muzzle;
            Vector3 eye = muzzle != null ? muzzle.position : report.Point - Vector3.forward;
            if (spec.burstFx != null)
            {
                FxPool.Spawn(spec.burstFx, Toward(report.Point, eye, SurfaceHitPull), Quaternion.identity);
            }

            if (spec.arcFx != null)
            {
                Vector3 view = report.Point - eye;
                view.y = 0f;
                Vector3 side = view.sqrMagnitude > 1e-4f ? Vector3.Cross(Vector3.up, view.normalized) : Vector3.right;
                float lean = (++arcSeed & 1) == 0 ? StrikeLean : -StrikeLean;
                PlayBolt(spec.arcFx, report.Point + Vector3.up * StrikeHeight + side * lean, report.Point, 1f, 0f);
            }

            GameObject chainHit = spec.chainBurstFx != null ? spec.chainBurstFx : spec.burstFx;
            Vector3 from = report.Point;
            float strength = ChainStrength;
            for (int i = 0; i < report.ChainPoints.Count; i++)
            {
                Vector3 point = Toward(report.ChainPoints[i], eye, CentreHitPull);
                if (chainHit != null)
                {
                    FxPool.Spawn(chainHit, point, Quaternion.identity);
                }

                if (spec.arcFx != null)
                {
                    PlayBolt(spec.arcFx, from, point, strength, 0f);
                }

                from = point;
                strength *= 0.85f;
            }
        }

        void SpawnImpact(GameObject prefab, Vector3 point, float bodyWidth, bool grounded, Vector3 ground)
        {
            if (prefab == null)
            {
                return;
            }

            PooledFx fx = FxPool.Spawn(prefab, point, Quaternion.identity);
            if (fx != null && fx.TryGetComponent(out LightningImpactFx impact))
            {
                impact.Play(bodyWidth, grounded ? ground : (Vector3?)null);
            }
        }

        void ShowFlash(Health target, float strength, float delay)
        {
            if (target == null)
            {
                return;
            }

            target.GetComponentsInChildren(true, flashBuffer);
            foreach (IHitFlashView view in flashBuffer)
            {
                view.ShowFlash(strength, delay);
            }
        }

        /// <summary>The discharging player's own view only (an NPC's weapon has no camera, other players are untouched).</summary>
        void KickOwnView()
        {
            if (!viewLookedUp)
            {
                ownView = GetComponentInChildren<CameraShaker>(true);
                viewLookedUp = true;
            }

            if (ownView != null && ownView.isActiveAndEnabled)
            {
                Vector3 euler = dischargeKickEuler;
                euler.y *= (arcSeed & 1) == 0 ? 1f : -1f;
                ownView.Kick(euler, dischargeKickOffset, dischargeKickDuration);
            }
        }

        /// <summary>
        /// The enemy's body width (its hit zones, else its colliders; <see cref="DefaultBodyWidth"/> without any) and the
        /// floor under it (none in the air or without a body: then no floor ring).
        /// </summary>
        void Body(Health target, Vector3 point, out float width, out bool grounded, out Vector3 ground)
        {
            width = DefaultBodyWidth;
            grounded = false;
            ground = default;
            Vector3 origin = point;
            float drop = FreeFallProbe;
            if (target != null && TryBodyBounds(target, out Bounds body))
            {
                width = Mathf.Clamp(Mathf.Min(body.size.x, body.size.z), 0.3f, 3f);
                origin = new Vector3(body.center.x, body.min.y + 0.25f, body.center.z);
                drop = 0.25f + GroundTolerance;
            }

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, drop, GameLayers.SightBlockingMask, QueryTriggerInteraction.Ignore))
            {
                grounded = true;
                ground = hit.point + Vector3.up * 0.03f;
            }
        }

        bool TryBodyBounds(Health target, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            target.GetComponentsInChildren(false, zoneBuffer);
            foreach (HitZone zone in zoneBuffer)
            {
                if (zone.TryGetComponent(out Collider c) && c.enabled)
                {
                    Add(ref bounds, ref any, c.bounds);
                }
            }

            if (!any)
            {
                target.GetComponentsInChildren(false, colliderBuffer);
                foreach (Collider c in colliderBuffer)
                {
                    if (c.enabled && !c.isTrigger)
                    {
                        Add(ref bounds, ref any, c.bounds);
                    }
                }
            }

            return any;
        }

        static void Add(ref Bounds bounds, ref bool any, Bounds more)
        {
            if (any)
            {
                bounds.Encapsulate(more);
            }
            else
            {
                bounds = more;
                any = true;
            }
        }

        const float SurfaceHitPull = 0.25f;
        const float CentreHitPull = 0.5f;

        /// <summary>Body width assumed without colliders (a humanoid enemy's chest).</summary>
        public const float DefaultBodyWidth = 0.8f;

        /// <summary>Feet this far above the floor still count as standing (else the enemy is in the air: no floor ring).</summary>
        public const float GroundTolerance = 0.45f;

        const float FreeFallProbe = 2.2f;

        static Vector3 Toward(Vector3 point, Vector3 eye, float distance)
        {
            Vector3 toEye = eye - point;
            float length = toEye.magnitude;
            return length > distance * 2f ? point + toEye / length * distance : point;
        }

        /// <summary>Bolt strength of the first chained strike (the first strike is 1).</summary>
        public const float ChainStrength = 0.65f;

        /// <summary>
        /// The main bolt starts this high above the hit, leaning this far to one side (alternating). 7.5 m comes in from
        /// above the top of the screen at the usual 8–12 m range, so the strike reads as a full bolt from the sky
        /// (3.4 m was a short wisp over the target in first person).
        /// </summary>
        public const float StrikeHeight = 7.5f;
        public const float StrikeLean = 1.6f;

        void PlayBolt(GameObject prefab, Vector3 start, Vector3 end, float strength, float travel)
        {
            PooledFx fx = FxPool.Spawn(prefab, start, Quaternion.identity);
            if (fx == null)
            {
                return;
            }

            if (fx.TryGetComponent(out LightningBoltFx bolt))
            {
                bolt.Play(start, end, strength, ++arcSeed, travel);
            }
            else if (fx.TryGetComponent(out LightningArcFx arc))
            {
                arc.Play(start, end, ++arcSeed); // older thin arc prefabs
            }
        }
    }
}
