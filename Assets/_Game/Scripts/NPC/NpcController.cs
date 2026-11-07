using System;
using System.Collections;
using System.Collections.Generic;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.Player;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using UnityEngine;
using UnityEngine.AI;
using Random = System.Random;

namespace CoopDeck.NPC
{
    /// <summary>
    /// The AI controller of an NPC ally: the counterpart of the human <c>PlayerInputReader</c>.
    /// It drives a normal <see cref="FpsCharacter"/> (same motor, weapon, <see cref="SkillCaster"/>,
    /// <see cref="DeckController"/> and deck rules as the player) purely by returning
    /// <see cref="CharacterCommands"/>, so swapping it for a human or network source changes nothing
    /// else. Each frame it steers, aims, fires and presses skill slots; a few times per second it
    /// senses the fight into an <see cref="NpcSituation"/>, runs the <see cref="NpcStateMachine"/>
    /// (mode) and the <see cref="NpcDecisionMaker"/> (utility action). Online, only the host runs it.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcController : MonoBehaviour, ICharacterCommandSource, IControllerIdentity
    {
        const float ChestHeight = 1.15f;
        const float LeaderAimRange = 45f;

        [SerializeField] NpcProfile profile;
        [SerializeField] FpsCharacter character;
        [Tooltip("The player this NPC follows and takes orders from. Empty = the local human.")]
        [SerializeField] FpsCharacter leader;
        [Tooltip("Combo rules to plan with. Empty = found in the scene.")]
        [SerializeField] ComboSystem comboSystem;
        [SerializeField] NpcTactic tactic = NpcTactic.Auto;
        [Tooltip("0 = different each run.")]
        [SerializeField] int randomSeed;

        struct PendingThreat
        {
            public ThreatWarning Warning;
            public float Arrived;
            public float Roll;
            public bool Handled;
        }

        readonly NpcSituation situation = new NpcSituation();
        readonly NpcStateMachine machine = new NpcStateMachine();
        readonly List<NpcFollowUp> followUps = new List<NpcFollowUp>(8);
        readonly ICombatActor[] enemyActors = new ICombatActor[NpcSituation.MaxEnemies];
        readonly List<ICombatActor> candidates = new List<ICombatActor>(32);
        readonly List<float> candidateDistances = new List<float>(32);
        readonly Dictionary<int, float> lastSeen = new Dictionary<int, float>();
        readonly Dictionary<Health, float> teamHits = new Dictionary<Health, float>();
        readonly HashSet<int> meleeAttackers = new HashSet<int>();
        readonly Dictionary<CardDefinition, NpcCardTraits> traitCache = new Dictionary<CardDefinition, NpcCardTraits>();
        readonly Dictionary<int, float> heldFor = new Dictionary<int, float>();
        readonly Dictionary<int, float> holdBlockedUntil = new Dictionary<int, float>();
        readonly PendingThreat[] pending = new PendingThreat[NpcSituation.MaxThreats];
        readonly List<Health> staleHits = new List<Health>(4);

        DeckController deck;
        DeckController leaderDeck;
        Revivable revivable;
        Revivable leaderRevivable;
        FpsMotor motor;
        FpsLook look;
        HitscanWeapon weapon;
        NpcNavigator navigator;
        Random rng;
        int pendingCount;
        bool subscribedLeader;
        FpsCharacter subscribedTo;

        NpcDecision decision = NpcDecision.None;
        float thinkTimer;
        float lastThinkTime;
        bool wantsEvade;
        Vector3 evadeDirection;
        bool evadeDashPending;

        // Skill being aimed / cast.
        int castSlot = -1;
        int castInstance;
        float castAimTime;
        ICombatActor castTarget;
        NpcActionKind castAction;
        int castRule = -1;
        bool castCoop;
        NpcActionKind lastCastAction;
        int lastCastRule = -1;
        bool lastCastCoop;

        ICombatActor shootTarget;
        ICombatActor focusTarget;
        bool burstOn;
        float burstTimer;
        float setupBlockedUntil;

        // Movement.
        Vector3 combatSpot;
        bool hasCombatSpot;
        float nextSpotTime;
        int followSide = 1;
        float stuckTimer;
        float fireLineTimer;
        Vector3 sidestep;
        float sidestepTimer;
        float pathFailTimer;
        float noiseSeed;
        bool recovering;

        /// <summary>Raised when a card was actually used (after the deck accepted it).</summary>
        public event Action<NpcController, CardDefinition, NpcActionKind> SkillCast;

        /// <summary>Short message for the HUD (order acknowledged, back in the fight …).</summary>
        public event Action<NpcController, string> Notice;

        public event Action<NpcController> TacticChanged;

        public ControllerKind Kind => ControllerKind.AI;
        public NpcProfile Profile => profile;
        public FpsCharacter Character => character;
        public FpsCharacter Leader => leader;
        public DeckController Deck => deck;
        public Revivable Revivable => revivable;
        public ComboSystem ComboSystem => comboSystem;
        public string DisplayName => profile != null ? profile.displayName : character != null ? character.DisplayName : name;
        public NpcTactic Tactic => tactic;
        public NpcMode Mode => character != null && !character.IsAlive ? NpcMode.Downed : machine.Mode;
        public NpcDecision LastDecision => decision;
        public NpcSituation Situation => situation;
        public ICombatActor FocusTarget => focusTarget != null && focusTarget.IsAlive ? focusTarget : null;
        public ICombatActor ShootTarget => shootTarget;
        public IReadOnlyList<NpcFollowUp> FollowUps => followUps;
        public CardDefinition LastSkill { get; private set; }
        public NpcActionKind LastSkillAction => lastCastAction;

        /// <summary>Combo rule the last cast aimed for (-1 none) and whether it finished an ally's element.</summary>
        public int LastSkillRule => lastCastRule;
        public bool LastSkillCoop => lastCastCoop;
        public float LastSkillTime { get; private set; } = float.NegativeInfinity;
        public int ThinkCount { get; private set; }
        public int CastCount { get; private set; }
        public int ComboFinishCasts { get; private set; }
        public int ComboSetupCasts { get; private set; }
        public int EvadeCount { get; private set; }
        public int TeleportCount { get; private set; }

        /// <summary>The enemy behind index <paramref name="index"/> of the last think's situation.</summary>
        public ICombatActor EnemyAt(int index) => index >= 0 && index < situation.EnemyCount ? enemyActors[index] : null;

        public void Configure(NpcProfile npcProfile, FpsCharacter body, FpsCharacter leaderCharacter, ComboSystem combos, int seed = 0)
        {
            profile = npcProfile;
            character = body;
            comboSystem = combos;
            randomSeed = seed;
            SetLeader(leaderCharacter);
        }

        // ---------------------------------------------------------------- lifecycle

        void Awake()
        {
            if (profile == null)
            {
                profile = NpcProfile.CreateRuntimeDefault();
            }

            if (character == null)
            {
                character = GetComponent<FpsCharacter>();
            }

            rng = new Random(randomSeed != 0 ? randomSeed : Environment.TickCount ^ (name.GetHashCode() * 31 + transform.position.GetHashCode()));
            navigator = new NpcNavigator(profile.repathInterval);
            thinkTimer = (float)rng.NextDouble() * profile.thinkInterval; // spread NPC thinks over frames
            noiseSeed = (float)rng.NextDouble() * 100f;
        }

        void Start()
        {
            CacheComponents();
            if (character != null && character.CommandSource == null)
            {
                character.SetCommandSource(this);
            }

            if (leader == null)
            {
                FpsCharacter human = FpsCharacter.FindLocalHuman();
                if (human != null && human != character)
                {
                    SetLeader(human);
                }
            }

            if (comboSystem == null)
            {
                comboSystem = FindAnyObjectByType<ComboSystem>();
            }

            SubscribeLeader();
        }

        void CacheComponents()
        {
            if (character == null)
            {
                return;
            }

            deck = character.GetComponent<DeckController>();
            revivable = character.GetComponent<Revivable>();
            motor = character.Motor;
            look = character.Look;
            weapon = character.Weapon;
        }

        void OnEnable()
        {
            CombatEvents.ThreatRaised += OnThreat;
            CombatEvents.DamageApplied += OnDamage;
            if (character == null)
            {
                character = GetComponent<FpsCharacter>();
            }

            CacheComponents();
            if (deck != null)
            {
                deck.CardActivated += OnCardActivated;
                deck.DeckRebuilt += OnDeckRebuilt;
            }

            if (revivable != null)
            {
                revivable.BledOut += OnBledOut;
                revivable.RevivedByAlly += OnRevivedByAlly;
            }

            SubscribeLeader();
        }

        void OnDisable()
        {
            CombatEvents.ThreatRaised -= OnThreat;
            CombatEvents.DamageApplied -= OnDamage;
            if (deck != null)
            {
                deck.CardActivated -= OnCardActivated;
                deck.DeckRebuilt -= OnDeckRebuilt;
            }

            if (revivable != null)
            {
                revivable.BledOut -= OnBledOut;
                revivable.RevivedByAlly -= OnRevivedByAlly;
            }

            UnsubscribeLeader();
        }

        public void SetLeader(FpsCharacter newLeader)
        {
            UnsubscribeLeader();
            leader = newLeader;
            leaderDeck = leader != null ? leader.GetComponent<DeckController>() : null;
            leaderRevivable = leader != null ? leader.GetComponent<Revivable>() : null;
            if (isActiveAndEnabled)
            {
                SubscribeLeader();
            }
        }

        void SubscribeLeader()
        {
            if (subscribedLeader || leader == null)
            {
                return;
            }

            leaderDeck = leader.GetComponent<DeckController>();
            leaderRevivable = leader.GetComponent<Revivable>();
            leader.OrderTacticRequested += OnOrderTactic;
            leader.OrderMarkRequested += OnOrderMark;
            subscribedTo = leader;
            subscribedLeader = true;
        }

        void UnsubscribeLeader()
        {
            if (!subscribedLeader)
            {
                return;
            }

            if (subscribedTo != null)
            {
                subscribedTo.OrderTacticRequested -= OnOrderTactic;
                subscribedTo.OrderMarkRequested -= OnOrderMark;
            }

            subscribedTo = null;
            subscribedLeader = false;
        }

        // ---------------------------------------------------------------- orders from the player

        public void SetTactic(NpcTactic newTactic, bool announce = true)
        {
            if (tactic == newTactic)
            {
                return;
            }

            tactic = newTactic;
            foreach (int id in new List<int>(heldFor.Keys))
            {
                heldFor[id] = 0f; // a new policy starts its holds fresh
            }

            TacticChanged?.Invoke(this);
            if (announce)
            {
                Notice?.Invoke(this, $"{DisplayName}：方針「{NpcLabels.Tactic(tactic)}」");
            }

            thinkTimer = 0f;
        }

        void OnOrderTactic(FpsCharacter from) => SetTactic(NpcLabels.Next(tactic));

        void OnOrderMark(FpsCharacter from)
        {
            if (from == null || from.Look == null)
            {
                return;
            }

            ICombatActor marked = FindActorUnderAim(from.Look.GetAimRay(), from.Team);
            SetFocus(marked);
        }

        /// <summary>Sets (or clears, with null) the focus target. Marking switches Auto to Focus.</summary>
        public void SetFocus(ICombatActor target)
        {
            if (target != null && target.IsAlive && IsHostile(target))
            {
                focusTarget = target;
                if (tactic == NpcTactic.Auto)
                {
                    SetTactic(NpcTactic.Focus, announce: false);
                }

                Notice?.Invoke(this, $"{DisplayName}：集中攻撃 → {target.DisplayName}");
            }
            else
            {
                focusTarget = null;
                Notice?.Invoke(this, $"{DisplayName}：集中攻撃の対象を解除");
            }

            thinkTimer = 0f;
        }

        ICombatActor FindActorUnderAim(Ray ray, TeamId team)
        {
            if (Physics.Raycast(ray, out RaycastHit hit, 120f, GameLayers.PlayerShotMask, QueryTriggerInteraction.Ignore))
            {
                Health health = hit.collider.GetComponentInParent<Health>();
                ICombatActor actor = ActorOf(health);
                if (actor != null && IsHostile(actor))
                {
                    return actor;
                }
            }

            // Nothing exactly under the crosshair: the hostile closest to the aim line (within 6°).
            ICombatActor best = null;
            float bestAngle = 6f;
            var actors = CombatantRegistry.All; // indexed: no boxed enumerator (GC) in per-frame / per-think loops
            for (int ai = 0; ai < actors.Count; ai++)
            {
                ICombatActor actor = actors[ai];
                if (!Valid(actor) || !actor.IsAlive || !IsHostile(actor))
                {
                    continue;
                }

                Vector3 to = actor.Position + Vector3.up * ChestHeight - ray.origin;
                if (to.sqrMagnitude > 120f * 120f)
                {
                    continue;
                }

                float angle = Vector3.Angle(ray.direction, to);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    best = actor;
                }
            }

            return best;
        }

        static ICombatActor ActorOf(Health health)
        {
            if (health == null)
            {
                return null;
            }

            var actors = CombatantRegistry.All; // indexed: no boxed enumerator (GC) in per-frame / per-think loops
            for (int ai = 0; ai < actors.Count; ai++)
            {
                ICombatActor actor = actors[ai];
                if (Valid(actor) && actor.Health == health)
                {
                    return actor;
                }
            }

            return null;
        }

        // ---------------------------------------------------------------- events

        void OnThreat(ThreatWarning warning)
        {
            if (character == null || !ReferenceEquals(warning.Target, character))
            {
                return;
            }

            if (warning.Kind == ThreatKind.Melee)
            {
                meleeAttackers.Add(CombatantRegistry.IdOf(warning.Attacker));
            }

            int index = pendingCount < pending.Length ? pendingCount++ : OldestPending();
            pending[index] = new PendingThreat { Warning = warning, Arrived = Time.time, Roll = (float)rng.NextDouble() };
            lastSeen[CombatantRegistry.IdOf(warning.Attacker)] = Time.time;
        }

        int OldestPending()
        {
            int oldest = 0;
            for (int i = 1; i < pendingCount; i++)
            {
                if (pending[i].Arrived < pending[oldest].Arrived)
                {
                    oldest = i;
                }
            }

            return oldest;
        }

        void OnDamage(DamageEvent e)
        {
            if (character == null || e.Target == null)
            {
                return;
            }

            TeamId team = character.Team;
            if (e.Info.SourceTeam == team && e.Target.Team != team)
            {
                // A teammate is hitting this one: join in (dummies only count while someone else is on them,
                // so the NPC never keeps itself busy on the shooting range).
                if (!ReferenceEquals(e.Info.Source, character))
                {
                    teamHits[e.Target] = Time.time;
                }
            }
            else if (e.Target.Team == team && e.Info.Source != null && e.Info.SourceTeam != team && e.Info.SourceTeam != TeamId.Neutral)
            {
                lastSeen[CombatantRegistry.IdOf(e.Info.Source)] = Time.time; // it attacked one of us: we know where it is
            }
        }

        void OnCardActivated(CardUseResult result, SkillCastReport report)
        {
            LastSkill = result.Card.Definition;
            LastSkillTime = Time.time;
            CastCount++;
            NpcActionKind action = lastCastAction;
            if (action == NpcActionKind.ComboFinish)
            {
                ComboFinishCasts++;
            }
            else if (action == NpcActionKind.ComboSetup)
            {
                ComboSetupCasts++;
                setupBlockedUntil = Time.time + 4f;
            }

            heldFor.Remove(result.Card.InstanceId);
            SkillCast?.Invoke(this, LastSkill, action);
        }

        void OnDeckRebuilt()
        {
            CancelCast();
            heldFor.Clear();
            holdBlockedUntil.Clear();
            thinkTimer = 0f;
        }

        void OnRevivedByAlly(Revivable target, ICombatActor reviver)
        {
            Notice?.Invoke(this, $"{DisplayName} が復帰しました");
        }

        void OnBledOut(Revivable target)
        {
            if (!recovering)
            {
                StartCoroutine(RecoverAfterBleedOut());
            }
        }

        /// <summary>Nobody came: after a short delay the NPC gets back up next to its leader.</summary>
        IEnumerator RecoverAfterBleedOut()
        {
            recovering = true;
            yield return new WaitForSeconds(profile.recoverDelay);
            if (character != null && !character.IsAlive)
            {
                TeleportNearLeader();
                character.Health.Revive(profile.recoverHealthFraction);
                Notice?.Invoke(this, $"{DisplayName} が戦線に復帰しました");
            }

            recovering = false;
        }

        // ---------------------------------------------------------------- per frame

        /// <summary>Called by the character once per simulation step (the AI's "input").</summary>
        public CharacterCommands GetCommands(float deltaTime)
        {
            var commands = new CharacterCommands();
            if (character == null || profile == null)
            {
                return commands;
            }

            if (motor == null || look == null)
            {
                CacheComponents();
            }

            float now = Time.time;
            thinkTimer -= deltaTime;
            if (thinkTimer <= 0f)
            {
                thinkTimer = Mathf.Max(0.02f, thinkTimer + profile.thinkInterval);
                Think(now);
            }

            CheckThreats(now);
            float leaderDistance = leader != null ? FlatDistance(leader.Position, character.Position) : 0f;
            var input = new NpcModeInput
            {
                Alive = character.IsAlive,
                HasLeader = leader != null,
                LeaderDistance = leaderDistance,
                LeaderDowned = leader != null && !leader.IsAlive && (leaderRevivable == null || !leaderRevivable.HasBledOut),
                WantsEvade = wantsEvade,
                HasEngagedEnemy = situation.HasEngagedEnemy,
            };
            wantsEvade = false;
            NpcMode mode = machine.Tick(deltaTime, input, profile);
            if (mode == NpcMode.Evade && machine.ModeTime <= 0f)
            {
                EvadeCount++;
            }

            if (leader != null && leaderDistance > profile.teleportDistance)
            {
                TeleportNearLeader();
            }

            Execute(mode, deltaTime, now, ref commands);
            return commands;
        }

        // ---------------------------------------------------------------- think (a few times per second)

        void Think(float now)
        {
            ThinkCount++;
            float elapsed = lastThinkTime > 0f ? now - lastThinkTime : profile.thinkInterval;
            lastThinkTime = now;
            Sense(now);
            decision = NpcDecisionMaker.Decide(situation, profile, followUps);
            UpdateHolds(decision.ReservedMask, elapsed, now);

            shootTarget = EnemyAt(decision.ShootTarget);
            if (decision.Action == NpcActionKind.Evade)
            {
                RequestEvade(decision);
            }
            else if (IsSkillAction(decision.Action) && castSlot < 0 && decision.Slot >= 0)
            {
                BeginCast(decision);
            }
        }

        static bool IsSkillAction(NpcActionKind action) =>
            action == NpcActionKind.ComboFinish || action == NpcActionKind.ComboSetup || action == NpcActionKind.Support ||
            action == NpcActionKind.AreaSkill || action == NpcActionKind.SingleSkill;

        void RequestEvade(in NpcDecision d)
        {
            if (machine.Mode == NpcMode.Evade || machine.Mode == NpcMode.Downed)
            {
                return;
            }

            wantsEvade = true;
            evadeDirection = ChooseEvadeDirection(d.EvadeDirection);
            evadeDashPending = d.EvadeDash;
            for (int i = 0; i < pendingCount; i++)
            {
                pending[i].Handled = true; // one dodge per wave of threats
            }
        }

        /// <summary>Flips the dodge if the NavMesh says the chosen side is a wall.</summary>
        Vector3 ChooseEvadeDirection(Vector3 wanted)
        {
            Vector3 from = character.Position;
            if (NavMesh.SamplePosition(from, out NavMeshHit start, 1.5f, NavMesh.AllAreas) &&
                NavMesh.Raycast(start.position, start.position + wanted * 2.5f, out _, NavMesh.AllAreas))
            {
                Vector3 other = new Vector3(-wanted.x, 0f, -wanted.z);
                if (!NavMesh.Raycast(start.position, start.position + other * 2.5f, out _, NavMesh.AllAreas))
                {
                    return other;
                }
            }

            return wanted;
        }

        /// <summary>Threats are checked every frame (not only on think) so dodges are not late.</summary>
        void CheckThreats(float now)
        {
            if (pendingCount == 0 || !character.IsAlive)
            {
                return;
            }

            bool fresh = false;
            for (int i = pendingCount - 1; i >= 0; i--)
            {
                PendingThreat t = pending[i];
                float timeLeft = t.Warning.TimeToImpact - (now - t.Arrived);
                if (timeLeft < -0.3f)
                {
                    pending[i] = pending[--pendingCount];
                    continue;
                }

                fresh |= !t.Handled && now - t.Arrived >= profile.reactionTime;
            }

            if (!fresh || machine.Mode == NpcMode.Evade)
            {
                return;
            }

            FillThreats(now);
            FillSelf();
            NpcDecision evade = NpcDecisionMaker.EvaluateEvade(situation, profile);
            for (int i = 0; i < pendingCount; i++)
            {
                if (now - pending[i].Arrived >= profile.reactionTime)
                {
                    pending[i].Handled = true;
                }
            }

            if (evade.Action == NpcActionKind.Evade)
            {
                RequestEvade(evade);
            }
        }

        void Sense(float now)
        {
            situation.Clear();
            situation.Time = now;
            situation.Tactic = tactic;
            situation.SelfId = CombatantRegistry.IdOf(character);
            situation.Team = character.Team;
            situation.Rules = comboSystem != null ? comboSystem.Rules : null;
            situation.SetupAllowed = now >= setupBlockedUntil;
            situation.SinceLastCast = now - LastSkillTime;
            situation.FavoredElement = profile.favoredElement;
            FillSelf();
            FillLeader();
            FillEnemies(now);
            FillHand(now);
            FillThreats(now);
        }

        void FillSelf()
        {
            situation.Position = character.Position;
            situation.Forward = character.transform.forward;
            situation.Alive = character.IsAlive;
            Health health = character.Health;
            situation.Health = health != null ? health.Current : 100f;
            situation.Health01 = health != null ? health.Normalized : 1f;
            situation.DashCharges = motor != null ? motor.DashCharge : 0f;
            CombatStatus status = character.Status;
            situation.ShieldAmount = status != null ? status.Model.Shield : 0f;
            situation.DamageBuff = status != null ? status.Model.DamageBuff : 0f;
        }

        void FillLeader()
        {
            situation.HasLeader = leader != null;
            if (leader == null)
            {
                return;
            }

            situation.LeaderId = CombatantRegistry.IdOf(leader);
            situation.LeaderPosition = leader.Position;
            situation.LeaderAlive = leader.IsAlive;
            situation.LeaderDowned = !leader.IsAlive && (leaderRevivable == null || !leaderRevivable.HasBledOut);
            situation.LeaderHealth01 = leader.Health != null ? leader.Health.Normalized : 1f;
            situation.LeaderDistance = FlatDistance(leader.Position, character.Position);
            if (leader.Look != null)
            {
                Ray aim = leader.Look.GetAimRay();
                situation.LeaderAimOrigin = aim.origin;
                situation.LeaderAimDirection = aim.direction;
            }

            int mask = 0;
            DeckState leaderCards = leaderDeck != null ? leaderDeck.Deck : null;
            if (leaderCards != null && leader.IsAlive)
            {
                for (int i = 0; i < leaderCards.HandSize; i++)
                {
                    CardRuntime card = leaderCards.GetHandCard(i);
                    if (card == null || card.Gauge <= 0f || card.CooldownRemaining > 0.75f)
                    {
                        continue;
                    }

                    NpcCardTraits traits = TraitsOf(card.Definition);
                    if (traits.HitsEnemies && traits.Element != Element.None)
                    {
                        mask |= 1 << (int)traits.Element;
                    }
                }
            }

            situation.LeaderElementMask = mask;
        }

        void FillEnemies(float now)
        {
            candidates.Clear();
            candidateDistances.Clear();
            Vector3 position = character.Position;
            float range = profile.perceptionRange;
            var actors = CombatantRegistry.All; // indexed: no boxed enumerator (GC) in per-frame / per-think loops
            for (int ai = 0; ai < actors.Count; ai++)
            {
                ICombatActor actor = actors[ai];
                if (!Valid(actor) || !actor.IsAlive || !IsHostile(actor))
                {
                    continue;
                }

                float distance = Vector3.Distance(actor.Position, position);
                if (distance > range)
                {
                    continue;
                }

                // Insert sorted by distance (few enemies; no allocation).
                int at = candidates.Count;
                while (at > 0 && candidateDistances[at - 1] > distance)
                {
                    at--;
                }

                candidates.Insert(at, actor);
                candidateDistances.Insert(at, distance);
            }

            if (focusTarget != null && (!Valid(focusTarget) || !focusTarget.IsAlive))
            {
                focusTarget = null;
            }

            Vector3 eye = look != null ? look.GetAimRay().origin : position + Vector3.up * 1.6f;
            int count = Mathf.Min(candidates.Count, NpcSituation.MaxEnemies);
            float bestLeaderAngle = 8f;
            for (int i = 0; i < count; i++)
            {
                ICombatActor actor = candidates[i];
                float distance = candidateDistances[i];
                int id = CombatantRegistry.IdOf(actor);
                Vector3 chest = actor.Position + Vector3.up * ChestHeight;
                bool isFocus = ReferenceEquals(actor, focusTarget);

                // Line of sight for the nearest few (and the focus); the rest count as hidden.
                bool los = (i < 8 || isFocus) && !Physics.Linecast(eye, chest, GameLayers.SightBlockingMask, QueryTriggerInteraction.Ignore);
                bool training = actor is ITrainingTarget;
                if (!training && (los || distance <= profile.hearingRange))
                {
                    lastSeen[id] = now;
                }

                bool engaged;
                if (training)
                {
                    engaged = isFocus || teamHits.TryGetValue(actor.Health, out float hitTime) && now - hitTime < 6f;
                }
                else
                {
                    // Seen or heard recently, it attacked one of us, or the team is shooting it.
                    engaged = lastSeen.TryGetValue(id, out float seen) && now - seen < 4f ||
                              teamHits.TryGetValue(actor.Health, out float shot) && now - shot < 4f;
                }

                Health health = actor.Health;
                CombatStatus status = actor.Status;
                int index = situation.AddEnemy(new NpcEnemyInfo
                {
                    Id = id,
                    Position = actor.Position,
                    Distance = distance,
                    LineOfSight = los,
                    Health01 = health != null ? health.Normalized : 1f,
                    IsTraining = training,
                    Engaged = engaged,
                    IsMelee = meleeAttackers.Contains(id),
                    Auras = status != null ? status.Elements : null,
                });
                if (index < 0)
                {
                    break;
                }

                enemyActors[index] = actor;
                if (isFocus)
                {
                    situation.FocusTarget = index;
                }

                if (ReferenceEquals(actor, shootTarget))
                {
                    situation.CurrentTarget = index;
                }

                if (leader != null && leader.IsAlive)
                {
                    Vector3 to = chest - situation.LeaderAimOrigin;
                    if (to.sqrMagnitude < LeaderAimRange * LeaderAimRange * 1.8f)
                    {
                        float angle = Vector3.Angle(situation.LeaderAimDirection, to);
                        if (angle < bestLeaderAngle)
                        {
                            bestLeaderAngle = angle;
                            situation.LeaderTarget = index;
                        }
                    }
                }
            }

            for (int i = situation.EnemyCount; i < enemyActors.Length; i++)
            {
                enemyActors[i] = null;
            }

            // Forget dummies the team stopped shooting a while ago.
            if (teamHits.Count > 8)
            {
                staleHits.Clear();
                foreach (KeyValuePair<Health, float> pair in teamHits)
                {
                    if (pair.Key == null || now - pair.Value > 10f)
                    {
                        staleHits.Add(pair.Key);
                    }
                }

                foreach (Health h in staleHits)
                {
                    teamHits.Remove(h);
                }
            }
        }

        void FillHand(float now)
        {
            DeckState cards = deck != null ? deck.Deck : null;
            if (cards == null)
            {
                return;
            }

            int slots = Mathf.Min(cards.HandSize, NpcSituation.MaxHand);
            for (int i = 0; i < slots; i++)
            {
                CardRuntime card = cards.GetHandCard(i);
                if (card == null)
                {
                    continue;
                }

                heldFor.TryGetValue(card.InstanceId, out float held);
                situation.Hand[i] = new NpcHandCard
                {
                    Present = true,
                    Slot = i,
                    InstanceId = card.InstanceId,
                    Name = card.Definition.displayName,
                    Traits = TraitsOf(card.Definition),
                    Gauge = card.Gauge,
                    GaugeCost = card.GaugeCost,
                    Cooldown = card.CooldownRemaining,
                    HeldFor = held,
                    HoldBlocked = holdBlockedUntil.TryGetValue(card.InstanceId, out float until) && now < until,
                };
            }
        }

        void FillThreats(float now)
        {
            situation.ThreatCount = 0;
            for (int i = 0; i < pendingCount; i++)
            {
                PendingThreat t = pending[i];
                if (now - t.Arrived < profile.reactionTime || t.Handled)
                {
                    continue; // not noticed yet, or already dodged
                }

                situation.AddThreat(new NpcThreat
                {
                    Kind = t.Warning.Kind,
                    Origin = t.Warning.Attacker != null && Valid(t.Warning.Attacker) ? t.Warning.Attacker.Position : t.Warning.Origin,
                    Direction = t.Warning.Direction,
                    TimeLeft = t.Warning.TimeToImpact - (now - t.Arrived),
                    Damage = t.Warning.Damage,
                    Reach = t.Warning.Reach,
                    Roll = t.Roll,
                });
            }
        }

        /// <summary>Accumulates hold time on saved cards; when a hold runs out the card is freed for a while.</summary>
        void UpdateHolds(int reservedMask, float elapsed, float now)
        {
            for (int slot = 0; slot < NpcSituation.MaxHand; slot++)
            {
                NpcHandCard card = situation.Hand[slot];
                if (!card.Present)
                {
                    continue;
                }

                if ((reservedMask & (1 << slot)) != 0)
                {
                    float held = card.HeldFor + elapsed;
                    heldFor[card.InstanceId] = held;
                    if (held >= profile.HoldFor(tactic))
                    {
                        heldFor[card.InstanceId] = 0f;
                        holdBlockedUntil[card.InstanceId] = now + profile.holdCooldown;
                    }
                }
            }
        }

        NpcCardTraits TraitsOf(CardDefinition card)
        {
            if (card == null)
            {
                return default;
            }

            if (!traitCache.TryGetValue(card, out NpcCardTraits traits))
            {
                traits = NpcCardTraits.From(card);
                traitCache[card] = traits;
            }

            return traits;
        }

        // ---------------------------------------------------------------- casting

        void BeginCast(in NpcDecision d)
        {
            CardRuntime card = deck != null && deck.Deck != null ? deck.Deck.GetHandCard(d.Slot) : null;
            if (card == null)
            {
                return;
            }

            castSlot = d.Slot;
            castInstance = card.InstanceId;
            castAimTime = 0f;
            castTarget = EnemyAt(d.Target);
            castAction = d.Action;
            castRule = d.RuleIndex;
            castCoop = d.Coop;
        }

        void CancelCast()
        {
            castSlot = -1;
            castTarget = null;
        }

        /// <summary>Presses the skill slot once the aim is on target. Returns true when pressed this frame.</summary>
        bool UpdateCast(float deltaTime, float aimError, ref CharacterCommands commands)
        {
            if (castSlot < 0)
            {
                return false;
            }

            CardRuntime card = deck != null && deck.Deck != null ? deck.Deck.GetHandCard(castSlot) : null;
            if (card == null || card.InstanceId != castInstance || card.Gauge <= 0f)
            {
                CancelCast();
                return false;
            }

            castAimTime += deltaTime;
            if (castAimTime > profile.skillAimTimeout || castTarget != null && !castTarget.IsAlive)
            {
                CancelCast();
                return false;
            }

            if (card.IsOnCooldown)
            {
                return false;
            }

            NpcCardTraits traits = TraitsOf(card.Definition);
            if (traits.NeedsAim && (castTarget == null || aimError > CastTolerance(traits, castTarget) || !CrosshairOnTarget(traits, card.Definition.range)))
            {
                return false;
            }

            commands.SetSkillPressed(castSlot);
            lastCastAction = castAction;
            lastCastRule = castRule;
            lastCastCoop = castCoop;
            CancelCast();
            return true;
        }

        /// <summary>
        /// Where to point a skill: aimed cards at the chest (they need the body under the crosshair),
        /// ground-targeted area cards low on the body, so a near miss still lands next to the enemy
        /// instead of flying over it to the far ground. No aim wobble for skills.
        /// </summary>
        static Vector3 CastAimPoint(ICombatActor target, in NpcCardTraits traits) =>
            target.Position + Vector3.up * (traits.Targeting == CardTargeting.AimPoint ? 0.5f : ChestHeight);

        /// <summary>How far off (degrees) the aim may be when the skill is released, from the distance and the card's area.</summary>
        float CastTolerance(in NpcCardTraits traits, ICombatActor target)
        {
            float distance = Mathf.Max(1f, Vector3.Distance(look.GetAimRay().origin, CastAimPoint(target, traits)));
            if (traits.Targeting == CardTargeting.AimPoint)
            {
                // Cheap pre-check only: the release itself waits for the ray to land in the area (CrosshairOnTarget).
                return Mathf.Clamp(Mathf.Atan2(Mathf.Max(0.4f, traits.EnemyRadius * 0.5f), distance) * Mathf.Rad2Deg, 0.8f, 4f);
            }

            return Mathf.Clamp(Mathf.Atan2(0.5f, distance) * Mathf.Rad2Deg, 0.8f, 3f);
        }

        /// <summary>
        /// Like a player waiting for the crosshair to sit on the enemy: casts the same ray the
        /// SkillCaster will cast and only releases when it hits the target (aimed cards) or lands
        /// within the area of the target (ground-targeted cards). One raycast per frame while aiming.
        /// </summary>
        bool CrosshairOnTarget(in NpcCardTraits traits, float range)
        {
            Ray ray = look.GetAimRay();
            if (!Physics.Raycast(ray, out RaycastHit hit, range, GameLayers.PlayerShotMask, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            Health health = hit.collider.GetComponentInParent<Health>();
            if (health != null && health == castTarget.Health)
            {
                return true;
            }

            return traits.Targeting == CardTargeting.AimPoint && FlatDistance(hit.point, castTarget.Position) <= Mathf.Max(0.6f, traits.EnemyRadius * 0.7f);
        }

        // ---------------------------------------------------------------- execute (every frame)

        void Execute(NpcMode mode, float dt, float now, ref CharacterCommands c)
        {
            if (look == null)
            {
                return;
            }

            if (mode == NpcMode.Downed)
            {
                CancelCast();
                return;
            }

            Vector3 position = character.Position;
            Vector3 move = Vector3.zero;
            bool sprint = false;
            bool allowFire = true;
            bool allowCast = true;
            bool faceMovement = false;

            switch (mode)
            {
                case NpcMode.Evade:
                    move = evadeDirection;
                    if (evadeDashPending)
                    {
                        c.DashPressed = true;
                        evadeDashPending = false;
                    }

                    allowCast = false;
                    break;

                case NpcMode.Rescue:
                    allowFire = false;
                    allowCast = castAction == NpcActionKind.Support;
                    if (leader != null)
                    {
                        float d = FlatDistance(leader.Position, position);
                        if (d > 1.2f)
                        {
                            move = PathTo(position, leader.Position, now, 0.8f);
                            sprint = d > 6f;
                            faceMovement = true;
                        }
                    }

                    break;

                case NpcMode.Regroup:
                    allowFire = false;
                    allowCast = false;
                    move = PathTo(position, FollowPoint(), now, 1.5f);
                    sprint = true;
                    faceMovement = true;
                    break;

                case NpcMode.Engage:
                    move = EngageMovement(position, now);
                    break;

                default:
                    move = FollowMovement(position, now, out sprint);
                    faceMovement = sprint;
                    break;
            }

            if (mode == NpcMode.Engage || mode == NpcMode.Follow)
            {
                move = AvoidLeaderLine(position, move, dt);
                move += KeepAwayFromMelee(position);
            }

            // Aim: the skill target, then the shooting target, then where we go / where the leader looks.
            ICombatActor aimActor = castSlot >= 0 && castTarget != null ? castTarget : shootTarget;
            if (aimActor != null && !Valid(aimActor))
            {
                aimActor = null;
            }

            float aimError = 180f;
            if (!faceMovement && aimActor != null && aimActor.IsAlive)
            {
                CardRuntime casting = castSlot >= 0 && castTarget != null && deck != null && deck.Deck != null ? deck.Deck.GetHandCard(castSlot) : null;
                Vector3 point = casting != null ? CastAimPoint(castTarget, TraitsOf(casting.Definition)) : AimPoint(aimActor, now);
                aimError = AimAt(point, dt, ref c);
            }
            else
            {
                Vector3 lookDirection = move.sqrMagnitude > 0.04f ? move : leader != null && leader.Look != null ? situation.LeaderAimDirection : character.transform.forward;
                lookDirection.y = 0f;
                if (lookDirection.sqrMagnitude > 0.0001f)
                {
                    AimAt(look.GetAimRay().origin + lookDirection.normalized * 10f, dt, ref c);
                }
            }

            bool pressed = allowCast && UpdateCast(dt, aimError, ref c);
            if (!allowCast && castSlot >= 0 && mode != NpcMode.Rescue)
            {
                CancelCast();
            }

            // Fire in bursts when on target, never through the leader.
            bool fire = false;
            bool aimingSkill = castSlot >= 0 && castTarget != null; // steady hands while lining up a skill (no recoil)
            if (allowFire && !sprint && !aimingSkill && aimActor != null && aimActor.IsAlive && aimError <= profile.fireTolerance && !pressed)
            {
                Vector3 muzzle = look.GetAimRay().origin;
                bool blocked = leader != null && leader.IsAlive &&
                               NpcSteering.AllyBlocksShot(muzzle, aimActor.Position + Vector3.up * ChestHeight, leader.Position + Vector3.up * 1f, 0.9f);
                bool visible = !Physics.Linecast(muzzle, aimActor.Position + Vector3.up * ChestHeight, GameLayers.SightBlockingMask, QueryTriggerInteraction.Ignore);
                fire = !blocked && visible && Burst(dt);
            }

            c.FireHeld = fire;
            c.FirePressed = fire && !burstWasOn;
            burstWasOn = fire;

            // Top up the magazine in quiet moments.
            if (weapon != null && weapon.State != null && aimActor == null && !weapon.IsReloading &&
                weapon.State.Ammo < weapon.State.MagazineSize * 0.5f)
            {
                c.ReloadPressed = true;
            }

            UpdateStuck(move, dt, now, ref c);
            c.Move = NpcSteering.LocalMove(Vector3.ClampMagnitude(move, 1f), character.transform.forward, character.transform.right);
            c.SprintHeld = sprint && c.Move.y > 0.5f;
        }

        bool burstWasOn;

        bool Burst(float dt)
        {
            burstTimer -= dt;
            if (burstTimer <= 0f)
            {
                burstOn = !burstOn;
                Vector2 range = burstOn ? profile.burstSeconds : profile.pauseSeconds;
                burstTimer = Mathf.Lerp(range.x, range.y, (float)rng.NextDouble());
            }

            return burstOn;
        }

        Vector3 AimPoint(ICombatActor target, float now)
        {
            Vector3 point = target.Position + Vector3.up * ChestHeight;
            float distance = Vector3.Distance(point, character.Position);
            float wobble = Mathf.Tan(profile.aimError * Mathf.Deg2Rad) * distance;
            float t = now * 1.7f;
            point.x += (Mathf.PerlinNoise(noiseSeed, t) * 2f - 1f) * wobble;
            point.y += (Mathf.PerlinNoise(noiseSeed + 7f, t) * 2f - 1f) * wobble * 0.6f;
            point.z += (Mathf.PerlinNoise(noiseSeed + 13f, t) * 2f - 1f) * wobble;
            return point;
        }

        /// <summary>Turns the view toward a point; returns the remaining error in degrees.</summary>
        float AimAt(Vector3 point, float dt, ref CharacterCommands c)
        {
            Ray aim = look.GetAimRay();
            Vector2 error = NpcSteering.AimError(aim.direction, point - aim.origin);
            c.LookDelta = NpcSteering.LookDelta(error, profile.aimGain, profile.turnSpeed, dt);
            return error.magnitude;
        }

        // ---------------------------------------------------------------- movement

        Vector3 FollowPoint()
        {
            if (leader == null)
            {
                return character.Position;
            }

            Vector3 view = situation.LeaderAimDirection;
            return NpcSteering.FollowSlot(leader.Position, view, profile.followSlotAngle, profile.followSlotDistance, followSide);
        }

        Vector3 FollowMovement(Vector3 position, float now, out bool sprint)
        {
            sprint = false;
            if (leader == null)
            {
                return Vector3.zero;
            }

            float leaderDistance = FlatDistance(leader.Position, position);
            followSide = NpcSteering.PreferredSide(position, leader.Position, situation.LeaderAimDirection);
            Vector3 slot = FollowPoint();
            float slotDistance = FlatDistance(slot, position);
            bool outOfBand = leaderDistance > profile.followMaxDistance || leaderDistance < profile.followMinDistance * 0.6f;
            if (!outOfBand && slotDistance < 2.5f)
            {
                navigator.Clear();
                return Vector3.zero;
            }

            sprint = slotDistance > 7f;
            return PathTo(position, slot, now, 1.2f);
        }

        Vector3 EngageMovement(Vector3 position, float now)
        {
            ICombatActor anchor = shootTarget ?? FocusTarget ?? NearestEngaged();
            if (anchor == null)
            {
                return FollowMovement(position, now, out _);
            }

            bool lostSight = situation.CurrentTarget < 0;
            bool spotInFireLine = hasCombatSpot && leader != null &&
                                  NpcSteering.InFireLine(combatSpot + Vector3.up * 1.2f, situation.LeaderAimOrigin, situation.LeaderAimDirection, 40f, 1.3f);
            if (!hasCombatSpot || now >= nextSpotTime || spotInFireLine || lostSight && now >= nextSpotTime - 0.8f)
            {
                hasCombatSpot = ChooseCombatSpot(position, anchor, out combatSpot);
                nextSpotTime = now + 1.2f;
            }

            if (!hasCombatSpot || FlatDistance(combatSpot, position) < 0.8f)
            {
                navigator.Clear();
                return Vector3.zero;
            }

            return PathTo(position, combatSpot, now, 1f) * 0.85f;
        }

        ICombatActor NearestEngaged()
        {
            for (int i = 0; i < situation.EnemyCount; i++)
            {
                if (situation.Enemies[i].Engaged)
                {
                    return enemyActors[i];
                }
            }

            return null;
        }

        static readonly Vector3[] SpotDirections =
        {
            new Vector3(1f, 0f, 0f), new Vector3(-1f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, -1f),
            new Vector3(0.71f, 0f, 0.71f), new Vector3(-0.71f, 0f, 0.71f), new Vector3(0.71f, 0f, -0.71f), new Vector3(-0.71f, 0f, -0.71f),
        };

        /// <summary>
        /// Picks where to stand in a fight: line of sight to the target, inside the preferred range,
        /// near the leader, out of the leader's line of fire, away from melee enemies. ~13 samples,
        /// about once a second.
        /// </summary>
        bool ChooseCombatSpot(Vector3 position, ICombatActor anchor, out Vector3 spot)
        {
            spot = position;
            Vector3 targetChest = anchor.Position + Vector3.up * ChestHeight;
            float bestScore = float.MinValue;
            bool found = false;
            for (int i = -1; i < SpotDirections.Length + 4; i++)
            {
                Vector3 candidate;
                if (i < 0)
                {
                    candidate = position; // staying put is a candidate too
                }
                else if (i < SpotDirections.Length)
                {
                    candidate = position + SpotDirections[i] * 3.5f;
                }
                else if (leader != null)
                {
                    float angle = (i - SpotDirections.Length) switch { 0 => 70f, 1 => -70f, 2 => 120f, _ => -120f };
                    candidate = NpcSteering.FollowSlot(leader.Position, situation.LeaderAimDirection, angle, 5f, 1);
                }
                else
                {
                    continue;
                }

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
                {
                    if (i >= 0)
                    {
                        continue;
                    }

                    hit.position = position;
                }

                Vector3 p = hit.position;
                float score = 0f;
                float d = Vector3.Distance(p, anchor.Position);
                if (d < profile.preferredRangeMin) score -= (profile.preferredRangeMin - d) * 2f;
                if (d > profile.preferredRangeMax) score -= (d - profile.preferredRangeMax) * 1.5f;
                if (!Physics.Linecast(p + Vector3.up * 1.6f, targetChest, GameLayers.SightBlockingMask, QueryTriggerInteraction.Ignore))
                {
                    score += 12f;
                }

                if (leader != null)
                {
                    float dl = FlatDistance(p, leader.Position);
                    if (dl > profile.engageLeash) score -= (dl - profile.engageLeash) * 3f;
                    if (dl < 2.5f) score -= 6f;
                    if (NpcSteering.InFireLine(p + Vector3.up * 1.2f, situation.LeaderAimOrigin, situation.LeaderAimDirection, 40f, 1.3f))
                    {
                        score -= 15f;
                    }
                }

                for (int e = 0; e < situation.EnemyCount; e++)
                {
                    if (situation.Enemies[e].Engaged && FlatDistance(situation.Enemies[e].Position, p) < profile.meleeKeepAway)
                    {
                        score -= situation.Enemies[e].IsMelee ? 10f : 4f;
                    }
                }

                score -= FlatDistance(p, position) * 0.25f;
                if (i < 0)
                {
                    score += 1.5f; // small bias against fidgeting
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    spot = p;
                    found = true;
                }
            }

            return found;
        }

        Vector3 PathTo(Vector3 position, Vector3 destination, float now, float arriveRadius)
        {
            navigator.RepathInterval = profile.repathInterval;
            navigator.SetDestination(position, destination, now);
            return navigator.Steer(position, arriveRadius, out _);
        }

        /// <summary>
        /// Never stand in the leader's line of fire for long: after a moment in it, step out sideways
        /// (the side the NPC is already on).
        /// </summary>
        Vector3 AvoidLeaderLine(Vector3 position, Vector3 move, float dt)
        {
            if (leader == null || !leader.IsAlive)
            {
                return move;
            }

            if (sidestepTimer > 0f)
            {
                sidestepTimer -= dt;
                return sidestep;
            }

            bool inLine = NpcSteering.InFireLine(position + Vector3.up * 1.1f, situation.LeaderAimOrigin, situation.LeaderAimDirection, 40f, 0.9f);
            fireLineTimer = inLine ? fireLineTimer + dt : 0f;
            if (fireLineTimer < 0.25f)
            {
                return move;
            }

            Vector3 aim = new Vector3(situation.LeaderAimDirection.x, 0f, situation.LeaderAimDirection.z).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, aim);
            float side = Vector3.Dot(position - situation.LeaderAimOrigin, right) >= 0f ? 1f : -1f;
            sidestep = right * side;
            sidestepTimer = 0.6f;
            fireLineTimer = 0f;
            hasCombatSpot = false;
            return sidestep;
        }

        Vector3 KeepAwayFromMelee(Vector3 position)
        {
            Vector3 push = Vector3.zero;
            for (int i = 0; i < situation.EnemyCount; i++)
            {
                NpcEnemyInfo e = situation.Enemies[i];
                if (!e.Engaged || e.IsTraining)
                {
                    continue;
                }

                Vector3 away = position - e.Position;
                away.y = 0f;
                float d = away.magnitude;
                float keep = e.IsMelee ? profile.meleeKeepAway : 2f;
                if (d < keep && d > 0.01f)
                {
                    push += away / d * (1f - d / keep);
                }
            }

            return push;
        }

        void UpdateStuck(Vector3 move, float dt, float now, ref CharacterCommands c)
        {
            if (motor == null || move.sqrMagnitude < 0.09f)
            {
                stuckTimer = 0f;
                return;
            }

            stuckTimer = motor.HorizontalVelocity.magnitude < 0.6f ? stuckTimer + dt : 0f;
            if (stuckTimer > 0.9f && stuckTimer - dt <= 0.9f)
            {
                c.JumpPressed = true; // a lip or a crate edge
                navigator.SetDestination(character.Position, navigator.Destination, now, force: true);
                hasCombatSpot = false;
            }

            pathFailTimer = navigator.LastPathFailed ? pathFailTimer + dt : 0f;
            if (leader != null && (stuckTimer > 3.5f || pathFailTimer > 4f) && FlatDistance(leader.Position, character.Position) > 8f)
            {
                TeleportNearLeader();
            }
        }

        /// <summary>Back next to the leader (behind them, on the NavMesh): the "too far / stuck" recovery.</summary>
        public void TeleportNearLeader()
        {
            if (leader == null || motor == null)
            {
                return;
            }

            Vector3 view = new Vector3(situation.LeaderAimDirection.x, 0f, situation.LeaderAimDirection.z);
            if (view.sqrMagnitude < 0.01f)
            {
                view = leader.transform.forward;
            }

            Vector3 behind = NpcSteering.FollowSlot(leader.Position, view, 160f, 3f, followSide);
            Vector3 target = NavMesh.SamplePosition(behind, out NavMeshHit hit, 4f, NavMesh.AllAreas) ? hit.position : leader.Position + Vector3.right * 1.5f;
            motor.Teleport(target + Vector3.up * 0.05f);
            Physics.SyncTransforms();
            navigator.Clear();
            hasCombatSpot = false;
            stuckTimer = 0f;
            pathFailTimer = 0f;
            TeleportCount++;
        }

        // ---------------------------------------------------------------- helpers

        bool IsHostile(ICombatActor actor) =>
            actor != null && character != null && actor.Team != character.Team && actor.Team != TeamId.Neutral;

        static bool Valid(ICombatActor actor) => actor != null && !(actor is UnityEngine.Object o && o == null);

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// <summary>Human-readable current intent for the HUD (computed on demand).</summary>
        public string DescribeIntent()
        {
            switch (Mode)
            {
                case NpcMode.Downed: return revivable != null && revivable.IsDowned ? $"ダウン中（救助を待っています {revivable.BleedOutRemaining:0} 秒）" : "ダウン中";
                case NpcMode.Rescue: return "プレイヤーを救助中";
                case NpcMode.Regroup: return "プレイヤーに合流中";
                case NpcMode.Evade: return "攻撃を回避";
            }

            switch (decision.Action)
            {
                case NpcActionKind.ComboFinish:
                case NpcActionKind.ComboSetup:
                {
                    string combo = ComboName(decision.RuleIndex);
                    string card = decision.Slot >= 0 ? situation.Hand[decision.Slot].Name : "";
                    return decision.Action == NpcActionKind.ComboFinish
                        ? $"{combo}を狙う（{card}で追撃）"
                        : $"{combo}の起点を作る（{card}）";
                }

                case NpcActionKind.Support: return "支援スキル";
                case NpcActionKind.AreaSkill: return "範囲スキルで攻撃";
                case NpcActionKind.SingleSkill: return "スキルで攻撃";
                case NpcActionKind.Shoot: return "射撃";
                case NpcActionKind.Revive: return "プレイヤーを救助中";
                default: return Mode == NpcMode.Engage ? "敵を探索" : "プレイヤーに追従";
            }
        }

        public string ComboName(int ruleIndex)
        {
            IReadOnlyList<ComboRule> rules = comboSystem != null ? comboSystem.Rules : null;
            if (rules == null || ruleIndex < 0 || ruleIndex >= rules.Count)
            {
                return "コンボ";
            }

            ComboDefinition def = comboSystem.DefinitionOf(rules[ruleIndex]);
            return def != null ? def.displayName : "コンボ";
        }

        /// <summary>The NPC can finish a combo on this enemy right now (HUD marker). Returns the combo name.</summary>
        public bool TryGetFollowUp(ICombatActor enemy, out string comboName, out bool coop)
        {
            comboName = null;
            coop = false;
            if (enemy == null || !character.IsAlive)
            {
                return false;
            }

            foreach (NpcFollowUp f in followUps)
            {
                if (ReferenceEquals(EnemyAt(f.Enemy), enemy))
                {
                    comboName = ComboName(f.RuleIndex);
                    coop = f.Coop;
                    return true;
                }
            }

            return false;
        }
    }
}
