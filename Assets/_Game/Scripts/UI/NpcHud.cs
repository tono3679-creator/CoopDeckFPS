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
        Text titleText;
        Image hpFill;
        Image hpBack;
        Text hpText;
        Text tacticText;
        Text intentText;
        Text skillText;
        Text noticeText;
        Image reviveBack;
        Image reviveFill;
        readonly Text[] cardTexts = new Text[2];
        readonly Image[] cardFills = new Image[2];
        readonly Image[] cardBacks = new Image[2];
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
        }

        void Unsubscribe()
        {
            if (subscribed == null)
            {
                return;
            }

            subscribed.Notice -= OnNotice;
            subscribed.SkillCast -= OnSkillCast;
            subscribed = null;
        }

        void OnNotice(NpcController source, string message)
        {
            noticeText.text = message;
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
            root = HudKit.CreateCanvas(transform, "NPC HUD Canvas", 11);

            // Left-middle panel: away from the crosshair, the cards (bottom centre) and the player HP (bottom left).
            Image back = HudKit.CreateImage("NpcPanel", root, HudKit.PanelColor, new Vector2(0f, 0.5f), new Vector2(200f, -10f), new Vector2(360f, 232f));
            panel = back.rectTransform;
            Vector2 topLeft = new Vector2(0f, 1f);
            titleText = HudKit.CreateText("Name", panel, 22, TextAnchor.MiddleLeft, topLeft, new Vector2(180f, -18f), new Vector2(340f, 28f));
            HudKit.AddOutline(titleText, 0.5f);
            hpBack = HudKit.CreateImage("HpBack", panel, new Color(1f, 1f, 1f, 0.12f), topLeft, new Vector2(130f, -44f), new Vector2(240f, 14f));
            hpFill = HudKit.CreateImage("HpFill", hpBack.rectTransform, new Color(0.45f, 1f, 0.6f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(240f, 14f));
            hpFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            hpText = HudKit.CreateText("HpText", panel, 16, TextAnchor.MiddleLeft, topLeft, new Vector2(310f, -44f), new Vector2(100f, 20f));
            reviveBack = HudKit.CreateImage("ReviveBack", panel, new Color(1f, 1f, 1f, 0.12f), topLeft, new Vector2(130f, -58f), new Vector2(240f, 5f));
            reviveFill = HudKit.CreateImage("ReviveFill", reviveBack.rectTransform, new Color(0.4f, 0.9f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(240f, 5f));
            reviveFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            tacticText = HudKit.CreateText("Tactic", panel, 18, TextAnchor.MiddleLeft, topLeft, new Vector2(180f, -76f), new Vector2(340f, 24f));
            intentText = HudKit.CreateText("Intent", panel, 17, TextAnchor.MiddleLeft, topLeft, new Vector2(180f, -100f), new Vector2(340f, 24f));
            skillText = HudKit.CreateText("Skill", panel, 17, TextAnchor.MiddleLeft, topLeft, new Vector2(180f, -124f), new Vector2(340f, 24f));
            for (int i = 0; i < cardTexts.Length; i++)
            {
                float y = -152f - i * 26f;
                cardBacks[i] = HudKit.CreateImage($"Card{i}Back", panel, new Color(1f, 1f, 1f, 0.1f), topLeft, new Vector2(250f, y - 8f), new Vector2(200f, 5f));
                cardFills[i] = HudKit.CreateImage($"Card{i}Fill", cardBacks[i].rectTransform, Color.white, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(200f, 5f));
                cardFills[i].rectTransform.pivot = new Vector2(0f, 0.5f);
                cardTexts[i] = HudKit.CreateText($"Card{i}", panel, 16, TextAnchor.MiddleLeft, topLeft, new Vector2(180f, y), new Vector2(340f, 20f));
            }

            noticeText = HudKit.CreateText("Notice", panel, 16, TextAnchor.MiddleLeft, topLeft, new Vector2(180f, -216f), new Vector2(340f, 22f));
            noticeText.color = new Color(1f, 0.9f, 0.55f);
            HudKit.AddOutline(noticeText, 0.6f);
            noticeText.gameObject.SetActive(false);

            // World markers.
            allyMarker = HudKit.CreateRect("AllyMarker", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 40f));
            allyMarkerText = HudKit.CreateText("Label", allyMarker, 16, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 8f), new Vector2(200f, 22f));
            HudKit.AddOutline(allyMarkerText, 0.7f);
            Image allyHpBack = HudKit.CreateImage("HpBack", allyMarker, new Color(0f, 0f, 0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(64f, 5f));
            allyMarkerHp = HudKit.CreateImage("Hp", allyHpBack.rectTransform, new Color(0.45f, 1f, 0.6f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(64f, 5f));
            allyMarkerHp.rectTransform.pivot = new Vector2(0f, 0.5f);
            allyMarker.gameObject.SetActive(false);

            for (int i = 0; i < markers.Length; i++)
            {
                RectTransform rect = HudKit.CreateRect("EnemyMarker", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 44f));
                Text label = HudKit.CreateText("Label", rect, 15, TextAnchor.LowerCenter, new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(240f, 40f));
                label.supportRichText = true;
                HudKit.AddOutline(label, 0.75f);
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
            Color color = profile != null ? profile.color : Color.white;
            if (string.IsNullOrEmpty(titleText.text))
            {
                string element = profile != null ? ElementStyle.DisplayName(profile.favoredElement) : "";
                titleText.text = $"◆ {npc.DisplayName}（{element}属性）  NPC";
                titleText.color = Color.Lerp(color, Color.white, 0.2f);
            }

            Health health = npc.Character.Health;
            int hpKey = health != null ? Mathf.CeilToInt(health.Current) * 10000 + Mathf.CeilToInt(health.Max) : -1;
            if (hpKey != lastHpKey)
            {
                lastHpKey = hpKey;
                hpText.text = health != null ? $"{Mathf.CeilToInt(health.Current)} / {Mathf.CeilToInt(health.Max)}" : "";
            }

            if (lastTacticValue != (int)npc.Tactic)
            {
                lastTacticValue = (int)npc.Tactic;
                tacticText.supportRichText = true;
                tacticText.text = $"方針：{NpcLabels.Tactic(npc.Tactic)}　<size=14>（V / R3 切替・中クリック / X / LT で集中）</size>";
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

                cardBacks[i].gameObject.SetActive(card != null);
                if (card != null)
                {
                    cardFills[i].color = ElementStyle.ColorOf(card.Definition.element);
                }
            }
        }

        void UpdatePanelBars(float dt)
        {
            Health health = npc.Character.Health;
            float hp = health != null ? health.Normalized : 1f;
            hpFill.rectTransform.sizeDelta = new Vector2(240f * hp, 14f);
            hpFill.color = hp < 0.35f ? new Color(1f, 0.4f, 0.35f) : new Color(0.45f, 1f, 0.6f);

            Revivable revive = npc.Revivable;
            bool downed = revive != null && revive.IsDowned;
            reviveBack.gameObject.SetActive(downed);
            if (downed)
            {
                reviveFill.rectTransform.sizeDelta = new Vector2(240f * revive.Progress01, 5f);
            }

            DeckState deck = npc.Deck != null ? npc.Deck.Deck : null;
            for (int i = 0; i < cardFills.Length; i++)
            {
                CardRuntime card = deck != null && i < deck.HandSize ? deck.GetHandCard(i) : null;
                if (card != null)
                {
                    cardFills[i].rectTransform.sizeDelta = new Vector2(200f * card.GaugeNormalized, 5f);
                }
            }

            skillFlash = Mathf.Max(0f, skillFlash - dt * 2f);
            skillText.color = Color.Lerp(new Color(1f, 1f, 1f, 0.9f), new Color(1f, 0.9f, 0.4f), skillFlash);
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
                builder.Append("<color=#FF6060>◎ 集中</color>\n");
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

                    builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ElementStyle.ColorOf(element))).Append(">◆")
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
                        builder.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(ElementStyle.ColorOf(element))).Append('>')
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
