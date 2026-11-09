using System.Collections.Generic;
using System.Text;
using CoopDeck.Cards;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.NPC;
using CoopDeck.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// NPC ally HUD. A compact panel on the left (name, HP, tactic, current intent, last skill,
    /// its two cards with gauges, down / revive state) and small world markers: the ally's name
    /// over its head, and over enemies "combo ready" (primed element → what finishes it) plus
    /// "NPC can follow up" when the NPC has the finisher ready. Markers fade near the crosshair so
    /// they never hide the aim point or the enemy under it. Text is rebuilt only when it changes.
    /// </summary>
    public class NpcHud : MonoBehaviour
    {
        const int MarkerCount = 10;
        const float MarkerRange = 45f;
        const float ContentInterval = 0.12f;
        const float CrosshairClearRadius = 120f;

        [SerializeField] NpcController npc;
        [SerializeField] FpsCharacter player;
        [SerializeField] ComboSystem comboSystem;

        sealed class Marker
        {
            public RectTransform Rect;
            public Text Label;
            public ICombatActor Target;
            public int Key;
        }

        RectTransform root;
        RectTransform panel;
        Image portraitRing;
        Image portraitIcon;
        RectTransform tacticChip;
        Text titleText;
        HudBar hpBar;
        Text hpText;
        Text tacticText;
        Text intentText;
        Text skillText;
        Text noticeText;
        HudBar reviveBar;
        readonly Text[] cardTexts = new Text[2];
        readonly HudBar[] cardBars = new HudBar[2];
        RectTransform allyMarker;
        Text allyMarkerText;
        Image allyMarkerHp;
        readonly Marker[] markers = new Marker[MarkerCount];
        readonly StringBuilder builder = new StringBuilder(128);
        readonly List<ICombatActor> markerTargets = new List<ICombatActor>(MarkerCount);
        Camera viewCamera;
        float contentTimer;
        float noticeAge = float.PositiveInfinity;
        float skillFlash;
        string lastIntent;
        int lastHpKey = int.MinValue;
        int lastSkillKey = int.MinValue;
        readonly int[] lastCardKeys = { int.MinValue, int.MinValue };
        int lastTacticValue = -1;
        int lastIntentKey = -1;
        string lastSkillLine;
        readonly string[] lastCardLines = new string[2];
        NpcController subscribed;
        Revivable subscribedRevivable;
        InputScheme tacticScheme = (InputScheme)(-1);
        int downKey = -1;

        [Tooltip("Ally card slot (0 = just above the vitals panel; more allies stack upward).")]
        [SerializeField, Min(0)] int squadSlot;

        public NpcController Npc => npc;
        public string IntentLine => intentText != null ? intentText.text : "";
        public string TacticLine => tacticText != null ? tacticText.text : "";
        public string SkillLine => skillText != null ? skillText.text : "";
        public string NoticeLine => noticeText != null ? noticeText.text : "";

        /// <summary>Enemy markers currently showing "NPC can follow up" (tests, debugging).</summary>
        public int FollowUpMarkersShown { get; private set; }

        /// <summary>Enemy markers currently showing "combo ready".</summary>
        public int ComboMarkersShown { get; private set; }

        public void Configure(NpcController controller, FpsCharacter localPlayer, ComboSystem combos)
        {
            Unsubscribe();
            npc = controller;
            player = localPlayer;
            comboSystem = combos;
            Subscribe();
        }

        void Awake()
        {
            Build();
        }

        void OnEnable() => Subscribe();

        void OnDisable() => Unsubscribe();

        void Start()
        {
            if (npc == null)
            {
                npc = FindAnyObjectByType<NpcController>();
            }

            if (player == null)
            {
                player = FpsCharacter.FindLocalHuman();
            }

            if (comboSystem == null)
            {
                comboSystem = FindAnyObjectByType<ComboSystem>();
            }

            Subscribe();
        }

        void Subscribe()
        {
            if (npc == null || subscribed == npc)
            {
                return;
            }

            Unsubscribe();
            npc.Notice += OnNotice;
            npc.SkillCast += OnSkillCast;
            subscribed = npc;
            subscribedRevivable = npc.Revivable;
            if (subscribedRevivable != null)
            {
                subscribedRevivable.Downed += OnAllyDowned;
                subscribedRevivable.RevivedByAlly += OnAllyRevived;
                subscribedRevivable.BledOut += OnAllyBledOut;
            }
        }

        void Unsubscribe()
        {
            if (subscribed == null)
            {
                return;
            }

            subscribed.Notice -= OnNotice;
            subscribed.SkillCast -= OnSkillCast;
            if (subscribedRevivable != null)
            {
                subscribedRevivable.Downed -= OnAllyDowned;
                subscribedRevivable.RevivedByAlly -= OnAllyRevived;
                subscribedRevivable.BledOut -= OnAllyBledOut;
                subscribedRevivable = null;
            }

            subscribed = null;
        }

        // Down / revive go to the combat feed (the card itself shows the live state).
        void OnAllyDowned(Revivable revivable) =>
            HudNotifications.Post(HudNoticeKind.Danger, $"{npc.DisplayName} がダウン — 近くに立って蘇生");

        void OnAllyRevived(Revivable revivable, ICombatActor reviver)
        {
            bool byPlayer = reviver != null && ReferenceEquals(reviver, player);
            HudNotifications.Post(HudNoticeKind.Revive, byPlayer ? $"{npc.DisplayName} を蘇生しました" : $"{npc.DisplayName} が復帰しました");
        }

        void OnAllyBledOut(Revivable revivable) =>
            HudNotifications.Post(HudNoticeKind.Warning, $"{npc.DisplayName} が後退 — まもなく合流");

        void OnNotice(NpcController source, string message)
        {
            noticeText.text = message;
            HudNotifications.Post(HudNoticeKind.Info, $"{source.DisplayName}：{message}");
            noticeAge = 0f;
            noticeText.gameObject.SetActive(true);
        }

        void OnSkillCast(NpcController source, CardDefinition card, NpcActionKind action)
        {
            skillFlash = 1f;
            contentTimer = 0f;
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            // World markers use the full canvas; the ally card sits in the 16:9 safe frame.
            root = HudKit.CreateCanvas(transform, "NPC HUD Canvas", 11, false);
            RectTransform frame = HudKit.CreateSafeFrame(root);

            // Ally row (compact): element mark, name, tactic (or down / revive state) and a thin HP bar.
            Rect layout = HudLayout.Squad;
            layout.y += squadSlot * HudLayout.SquadStep;
            panel = HudKit.CreateRect("AllyRow", frame, Vector2.zero, Vector2.zero, Vector2.zero, layout.size);
            HudLayout.Place(panel, layout);
            var topLeft = new Vector2(0f, 1f);
            portraitRing = UiWidgets.Icon("Scrim", panel, UiSprites.Glow, UiTokens.WithAlpha(UiTokens.Background, 0.4f), new Vector2(0.5f, 0.5f), Vector2.zero, 1f);
            portraitRing.preserveAspect = false;
            portraitRing.rectTransform.sizeDelta = new Vector2(layout.width + 120f, layout.height + 50f);
            portraitIcon = UiWidgets.Icon("Element", panel, UiSprites.ElementIcon(Element.None), UiTokens.Neutral, topLeft, new Vector2(9f, -12f), 16f);
            HudKit.AddOutline(portraitIcon, 0.6f);
            titleText = UiWidgets.Label("Name", panel, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleLeft, topLeft, new Vector2(92f, -12f),
                new Vector2(140f, 22f), true, false);
            HudKit.AddOutline(titleText, 0.85f);
            tacticChip = HudKit.CreateRect("Tactic", panel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -1f), new Vector2(150f, 22f));
            tacticText = UiWidgets.Label("Value", tacticChip, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleRight, new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(150f, 22f), true, false);
            HudKit.AddOutline(tacticText, 0.85f);
            tacticText.supportRichText = true;

            hpBar = UiWidgets.Bar("Hp", panel, topLeft, new Vector2(0f, 0.5f), new Vector2(0f, -30f), new Vector2(layout.width, 8f), UiTokens.Health);
            reviveBar = UiWidgets.Bar("Revive", panel, topLeft, new Vector2(0f, 0.5f), new Vector2(0f, -39f), new Vector2(layout.width, 3f), UiTokens.Energy);

            // Kept for the API (IntentLine, SkillLine …) but not shown: the row stays simple.
            RectTransform hidden = HudKit.CreateRect("Details", panel, topLeft, Vector2.zero, Vector2.zero);
            hidden.gameObject.SetActive(false);
            hpText = UiWidgets.Label("HpText", hidden, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleRight, topLeft, Vector2.zero, new Vector2(90f, 22f));
            intentText = UiWidgets.Label("Intent", hidden, UiTokens.Caption, UiTokens.TextMain, TextAnchor.MiddleLeft, topLeft, Vector2.zero, new Vector2(384f, 24f));
            skillText = UiWidgets.Label("Skill", hidden, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleLeft, topLeft, Vector2.zero, new Vector2(384f, 24f));
            for (int i = 0; i < cardTexts.Length; i++)
            {
                cardTexts[i] = UiWidgets.Label($"Card{i}", hidden, UiTokens.Caption, UiTokens.TextMain, TextAnchor.MiddleLeft, topLeft, Vector2.zero, new Vector2(184f, 22f));
                cardBars[i] = UiWidgets.Bar($"Card{i}Gauge", hidden, topLeft, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(180f, 5f), Color.white);
            }

            noticeText = UiWidgets.Label("Notice", hidden, UiTokens.Caption, UiTokens.Accent, TextAnchor.MiddleLeft, topLeft, Vector2.zero, new Vector2(384f, 22f), true);
            noticeText.gameObject.SetActive(false);

            // World markers.
            allyMarker = HudKit.CreateRect("AllyMarker", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 44f));
            allyMarkerText = HudKit.CreateText("Label", allyMarker, UiTokens.Caption, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 9f),
                new Vector2(240f, 24f));
            allyMarkerText.fontStyle = FontStyle.Bold;
            HudKit.AddOutline(allyMarkerText, 0.8f);
            Image allyHpBack = HudKit.CreateImage("HpBack", allyMarker, UiTokens.WithAlpha(UiTokens.FrameDark, 0.75f), new Vector2(0.5f, 0.5f), new Vector2(0f, -9f),
                new Vector2(68f, 6f));
            allyMarkerHp = HudKit.CreateImage("Hp", allyHpBack.rectTransform, UiTokens.Health, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(68f, 6f));
            allyMarkerHp.rectTransform.pivot = new Vector2(0f, 0.5f);
            allyMarker.gameObject.SetActive(false);

            for (int i = 0; i < markers.Length; i++)
            {
                RectTransform rect = HudKit.CreateRect("EnemyMarker", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 48f));
                Text label = HudKit.CreateText("Label", rect, UiTokens.Caption - 2, TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(0f, 22f),
                    new Vector2(260f, 44f));
                label.fontStyle = FontStyle.Bold;
                label.supportRichText = true;
                HudKit.AddOutline(label, 0.8f);
                rect.gameObject.SetActive(false);
                markers[i] = new Marker { Rect = rect, Label = label };
            }
        }

        // ---------------------------------------------------------------- update

        void LateUpdate()
        {
            bool visible = npc != null && npc.Character != null;
            panel.gameObject.SetActive(visible);
            if (!visible)
            {
                allyMarker.gameObject.SetActive(false);
                HideMarkers(0);
                return;
            }

            if (viewCamera == null)
            {
                viewCamera = player != null && player.Look != null && player.Look.ViewCamera != null ? player.Look.ViewCamera : Camera.main;
            }

            float dt = Time.unscaledDeltaTime;
            contentTimer -= dt;
            if (contentTimer <= 0f)
            {
                contentTimer = ContentInterval;
                UpdatePanelText();
                CollectMarkerTargets();
            }

            UpdatePanelBars(dt);
            UpdateAllyMarker();
            UpdateEnemyMarkers();

            if (noticeText.gameObject.activeSelf)
            {
                noticeAge += dt;
                Color c = noticeText.color;
                c.a = noticeAge < 2.4f ? 1f : Mathf.Clamp01(1f - (noticeAge - 2.4f) / 0.5f);
                noticeText.color = c;
                if (c.a <= 0f)
                {
                    noticeText.gameObject.SetActive(false);
                }
            }
        }

        void UpdatePanelText()
        {
            NpcProfile profile = npc.Profile;
            if (string.IsNullOrEmpty(titleText.text))
            {
                Element element = profile != null ? profile.favoredElement : Element.None;
                titleText.supportRichText = true;
                titleText.text = $"{npc.DisplayName} <size={UiTokens.Caption - 2}><color=#{ColorUtility.ToHtmlStringRGB(UiTokens.TextSub)}>NPC</color></size>";
                portraitIcon.sprite = UiSprites.ElementIcon(element);
                portraitIcon.color = UiTokens.ElementColor(element);
            }

            Health health = npc.Character.Health;
            int hpKey = health != null ? Mathf.CeilToInt(health.Current) * 10000 + Mathf.CeilToInt(health.Max) : -1;
            if (hpKey != lastHpKey)
            {
                lastHpKey = hpKey;
                hpText.text = health != null ? $"{Mathf.CeilToInt(health.Current)} / {Mathf.CeilToInt(health.Max)}" : "";
            }

            if ((lastTacticValue != (int)npc.Tactic || tacticScheme != InputGlyphs.Active) && downKey < 0)
            {
                lastTacticValue = (int)npc.Tactic;
                tacticScheme = InputGlyphs.Active;
                tacticText.text = $"{NpcLabels.Tactic(npc.Tactic)} <color=#{ColorUtility.ToHtmlStringRGB(UiTokens.Accent)}>{InputGlyphs.Label(HudAction.Tactic)}</color>";
            }

            // The intent text only changes with the mode, the action, its card or its combo.
            NpcDecision decision = npc.LastDecision;
            int intentKey = (int)npc.Mode * 4096 + (int)decision.Action * 256 + (decision.Slot + 1) * 16 + decision.RuleIndex + 1;
            bool downed = npc.Mode == NpcMode.Downed; // counts down every second
            if (intentKey != lastIntentKey || downed)
            {
                lastIntentKey = intentKey;
                string intent = "行動：" + npc.DescribeIntent();
                if (intent != lastIntent)
                {
                    intentText.text = intent;
                    lastIntent = intent;
                }
            }

            float ago = Time.time - npc.LastSkillTime;
            int skillKey = npc.LastSkill != null ? npc.LastSkill.GetHashCode() * 31 + (int)npc.LastSkillAction * 7 + (ago < 1f ? -1 : Mathf.FloorToInt(Mathf.Min(ago, 999f))) : 0;
            string skill = lastSkillLine;
            if (skillKey == lastSkillKey)
            {
                // unchanged: no new string
            }
            else if (npc.LastSkill != null)
            {
                lastSkillKey = skillKey;
                string tag = npc.LastSkillAction == NpcActionKind.ComboFinish ? "（コンボ追撃）" :
                    npc.LastSkillAction == NpcActionKind.ComboSetup ? "（コンボの起点）" : "";
                skill = $"スキル：{npc.LastSkill.displayName}{tag}　{(ago < 1f ? "今" : $"{ago:0} 秒前")}";
            }
            else
            {
                lastSkillKey = skillKey;
                skill = "スキル：まだ使っていません";
            }

            if (skill != lastSkillLine)
            {
                skillText.text = skill;
                lastSkillLine = skill;
            }

            DeckState deck = npc.Deck != null ? npc.Deck.Deck : null;
            for (int i = 0; i < cardTexts.Length; i++)
            {
                CardRuntime card = deck != null && i < deck.HandSize ? deck.GetHandCard(i) : null;
                bool heldNow = card != null && (npc.LastDecision.ReservedMask & (1 << i)) != 0;
                int cardKey = card == null ? -1 - (deck != null ? deck.HandSize : 0) :
                    card.InstanceId * 100000 + Mathf.RoundToInt(card.Gauge) * 100 + Mathf.CeilToInt(card.CooldownRemaining * 10f) % 100 + (heldNow ? 50000000 : 0);
                if (cardKey == lastCardKeys[i])
                {
                    continue; // nothing to rewrite (bars update separately)
                }

                lastCardKeys[i] = cardKey;
                string line;
                if (card == null)
                {
                    line = i < (deck != null ? deck.HandSize : 0) ? "（空き）" : "";
                }
                else
                {
                    string cd = card.IsOnCooldown ? $"  CD {card.CooldownRemaining:0.0}" : "";
                    string held = npc.LastDecision.ReservedMask != 0 && (npc.LastDecision.ReservedMask & (1 << i)) != 0 ? "  温存中" : "";
                    line = $"[{ElementStyle.DisplayName(card.Definition.element)}] {card.Definition.displayName}  {card.Gauge:0}/{card.MaxGauge:0}{cd}{held}";
                }

                if (line != lastCardLines[i])
                {
                    cardTexts[i].text = line;
                    lastCardLines[i] = line;
                }

                cardBars[i].Root.gameObject.SetActive(card != null);
                if (card != null)
                {
                    cardBars[i].FillColor = UiTokens.ElementColor(card.Definition.element);
                }
            }
        }

        void UpdatePanelBars(float dt)
        {
            Health health = npc.Character.Health;
            float hp = health != null ? health.Normalized : 1f;
            hpBar.FillColor = hp < 0.35f ? UiTokens.Danger : UiTokens.Health;
            hpBar.Set(hp);
            hpBar.Tick(dt);

            Revivable revive = npc.Revivable;
            bool downed = revive != null && revive.IsDowned;
            reviveBar.Root.gameObject.SetActive(downed);
            if (downed)
            {
                reviveBar.Set(revive.Progress01, true);
            }

            UpdateDownState(revive, downed);
            hpText.color = downed ? UiTokens.DangerText : UiTokens.TextSub;

            DeckState deck = npc.Deck != null ? npc.Deck.Deck : null;
            for (int i = 0; i < cardBars.Length; i++)
            {
                CardRuntime card = deck != null && i < deck.HandSize ? deck.GetHandCard(i) : null;
                if (card != null)
                {
                    cardBars[i].Set(card.GaugeNormalized);
                }

                cardBars[i].Tick(dt);
            }

            skillFlash = Mathf.Max(0f, skillFlash - dt * 2f);
            skillText.color = Color.Lerp(UiTokens.TextSub, UiTokens.Accent, skillFlash);
        }

        /// <summary>While downed the tactic text becomes the down / revive state (5 % steps).</summary>
        void UpdateDownState(Revivable revive, bool downed)
        {
            int key = !downed ? -1 : revive.CurrentReviver != null ? 1000 + Mathf.FloorToInt(revive.Progress01 * 20f) : Mathf.CeilToInt(revive.BleedOutRemaining);
            if (key == downKey)
            {
                return;
            }

            downKey = key;
            if (!downed)
            {
                lastTacticValue = -1; // rebuild the tactic text next refresh
                tacticText.color = UiTokens.TextSub;
                return;
            }

            tacticText.text = revive.CurrentReviver != null ? $"蘇生中 {revive.Progress01 * 100f:0}%" : $"ダウン {revive.BleedOutRemaining:0}s";
            tacticText.color = revive.CurrentReviver != null ? UiTokens.Energy : UiTokens.DangerText;
        }

        // ---------------------------------------------------------------- world markers

        bool Project(Vector3 world, out Vector2 canvasPosition, out float centerDistance)
        {
            canvasPosition = default;
            centerDistance = float.MaxValue;
            if (viewCamera == null)
            {
                return false;
            }

            Vector3 screen = viewCamera.WorldToScreenPoint(world);
            if (screen.z <= 0.2f)
            {
                return false;
            }

            float scale = root.rect.height / Mathf.Max(1f, Screen.height);
            canvasPosition = new Vector2((screen.x - Screen.width * 0.5f) * scale, (screen.y - Screen.height * 0.5f) * scale);
            centerDistance = canvasPosition.magnitude;
            return Mathf.Abs(canvasPosition.x) < root.rect.width * 0.5f && Mathf.Abs(canvasPosition.y) < root.rect.height * 0.5f;
        }

        void UpdateAllyMarker()
        {
            FpsCharacter body = npc.Character;
            if (!Project(body.Position + Vector3.up * 2.25f, out Vector2 position, out float center))
            {
                allyMarker.gameObject.SetActive(false);
                return;
            }

            allyMarker.gameObject.SetActive(true);
            allyMarker.anchoredPosition = position;
            Revivable revive = npc.Revivable;
            string label = revive != null && revive.IsDowned
                ? (revive.CurrentReviver != null ? $"▼ {npc.DisplayName} 蘇生中 {revive.Progress01 * 100f:0}%" : $"▼ {npc.DisplayName} ダウン（近くで蘇生）")
                : $"▼ {npc.DisplayName}";
            if (allyMarkerText.text != label)
            {
                allyMarkerText.text = label;
            }

            Color color = npc.Profile != null ? npc.Profile.color : Color.white;
            color.a = center < CrosshairClearRadius ? 0.35f : 1f;
            allyMarkerText.color = color;
            Health health = body.Health;
            allyMarkerHp.rectTransform.sizeDelta = new Vector2(64f * (health != null ? health.Normalized : 1f), 5f);
        }

        void CollectMarkerTargets()
        {
            markerTargets.Clear();
            if (viewCamera == null)
            {
                return;
            }

            Vector3 eye = viewCamera.transform.position;
            ICombatActor focus = npc.FocusTarget;
            var actors = CombatantRegistry.All; // indexed: no boxed enumerator (GC) in per-frame / per-think loops
            for (int ai = 0; ai < actors.Count; ai++)
            {
                ICombatActor actor = actors[ai];
                if (markerTargets.Count >= MarkerCount)
                {
                    break;
                }

                if (actor == null || actor is Object o && o == null || !actor.IsAlive || actor.Team == TeamId.Players || actor.Team == TeamId.Neutral)
                {
                    continue;
                }

                if ((actor.Position - eye).sqrMagnitude > MarkerRange * MarkerRange)
                {
                    continue;
                }

                bool primed = actor.Status != null && actor.Status.Elements.ActiveCount > 0;
                if (primed || ReferenceEquals(actor, focus))
                {
                    markerTargets.Add(actor);
                }
            }
        }

        void UpdateEnemyMarkers()
        {
            IReadOnlyList<ComboRule> rules = comboSystem != null ? comboSystem.Rules : null;
            int used = 0;
            int followUps = 0;
            int ready = 0;
            ICombatActor focus = npc.FocusTarget;
            foreach (ICombatActor actor in markerTargets)
            {
                if (actor == null || actor is Object o && o == null || !actor.IsAlive || used >= markers.Length)
                {
                    continue;
                }

                if (!Project(actor.Position + Vector3.up * 2.55f, out Vector2 position, out float center))
                {
                    continue;
                }

                Marker marker = markers[used++];
                ElementAuraModel auras = actor.Status != null ? actor.Status.Elements : null;
                int finishers = auras != null && rules != null ? ComboPlanner.FinisherMask(auras, rules, 0.15f) : 0;
                bool canFollow = npc.TryGetFollowUp(actor, out string comboName, out bool coop);
                bool isFocus = ReferenceEquals(actor, focus);
                int key = finishers * 8 + (canFollow ? 4 + 31 * (comboName != null ? comboName.GetHashCode() : 0) : 0) + (isFocus ? 2 : 0) +
                          (auras != null ? AuraKey(auras) * 512 : 0);
                if (!ReferenceEquals(marker.Target, actor) || marker.Key != key)
                {
                    marker.Target = actor;
                    marker.Key = key;
                    marker.Label.text = BuildMarkerText(auras, finishers, canFollow, comboName, isFocus);
                }

                if (finishers != 0) ready++;
                if (canFollow) followUps++;
                marker.Rect.gameObject.SetActive(true);
                marker.Rect.anchoredPosition = position;
                Color c = marker.Label.color;
                c.a = center < CrosshairClearRadius ? 0.3f : 1f; // keep the aim point clear
                marker.Label.color = c;
            }

            HideMarkers(used);
            FollowUpMarkersShown = followUps;
            ComboMarkersShown = ready;
        }

        static int AuraKey(ElementAuraModel auras)
        {
            int key = 0;
            for (int e = 1; e <= 4; e++)
            {
                if (auras.Has((Element)e))
                {
                    key |= 1 << e;
                }
            }

            return key;
        }

        string BuildMarkerText(ElementAuraModel auras, int finishers, bool canFollow, string comboName, bool isFocus)
        {
            builder.Clear();
            if (isFocus)
            {
                builder.Append("<color=#FF5A4A>◎ 集中</color>\n");
            }

            if (canFollow)
            {
                Color npcColor = npc.Profile != null ? npc.Profile.color : Color.yellow;
                builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(npcColor)).Append(">⚡")
                    .Append(npc.DisplayName).Append(" 追撃可：").Append(comboName).Append("</color>\n");
            }

            if (auras != null)
            {
                for (int e = 1; e <= 4; e++)
                {
                    var element = (Element)e;
                    if (!auras.Has(element))
                    {
                        continue;
                    }

                    builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(UiTokens.ElementColor(element))).Append(">◆")
                        .Append(ElementStyle.DisplayName(element)).Append("</color>");
                }

                if (finishers != 0)
                {
                    builder.Append(" → ");
                    bool first = true;
                    for (int e = 1; e <= 4; e++)
                    {
                        if ((finishers & (1 << e)) == 0)
                        {
                            continue;
                        }

                        var element = (Element)e;
                        if (!first) builder.Append('/');
                        first = false;
                        builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(UiTokens.ElementColor(element))).Append('>')
                            .Append(ElementStyle.DisplayName(element)).Append("</color>");
                    }

                    builder.Append(" でコンボ");
                }
            }

            return builder.ToString();
        }

        void HideMarkers(int from)
        {
            for (int i = from; i < markers.Length; i++)
            {
                if (markers[i].Rect.gameObject.activeSelf)
                {
                    markers[i].Rect.gameObject.SetActive(false);
                }

                markers[i].Target = null;
            }
        }
    }
}
