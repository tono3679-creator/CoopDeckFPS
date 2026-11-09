using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Player;
using CoopDeck.Skills;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Combo name banner (name in the combo's colour, tier and co-op tag, a count when the same
    /// combo repeats quickly) above the crosshair, kept small so it never hides enemies.
    /// Also owns the combo debug keys: F4 effect quality, F6 forced combo tier, F7 practice deck.
    /// </summary>
    public class ComboHud : MonoBehaviour
    {
        const float PopTime = 0.15f;
        const float HoldTime = 1.1f;
        const float FadeTime = 0.35f;
        const float RepeatWindow = 1.2f;

        [SerializeField] DeckController deck;

        RectTransform root;
        RectTransform banner;
        Text nameText;
        Text detailText;
        Text coopTagText;
        Text participantsText;
        Image coopGlow;

        readonly Image[] elementIcons = new Image[2];
        bool coopBanner;
        Text noticeText;
        float bannerAge = float.PositiveInfinity;
        float noticeAge = float.PositiveInfinity;
        ComboDefinition lastCombo;
        int repeatCount;

        public string LastShownName => nameText != null ? nameText.text : "";

        /// <summary>The "who did it" line of the last banner (tests, debugging).</summary>
        public string LastParticipants { get; private set; } = "";

        /// <summary>The local human is shown as "あなた", everyone else by name.</summary>
        static string Participant(CoopDeck.Combat.ICombatActor actor)
        {
            if (actor == null || actor is Object o && o == null)
            {
                return "？";
            }

            // The local player (human, or the autopilot standing in for them) reads as "you".
            bool local = actor is FpsCharacter character && (character.IsHumanControlled || ReferenceEquals(character, FpsCharacter.FindLocalHuman()));
            return local ? "あなた" : actor.DisplayName;
        }

        static string ElementName(Element element) => CoopDeck.Cards.ElementStyle.DisplayName(element);

        public void Configure(DeckController deckController)
        {
            deck = deckController;
        }

        void Awake()
        {
            root = HudKit.CreateCanvas(transform, "Combo HUD Canvas", 12);
            Rect layout = HudLayout.ComboBanner;
            banner = HudKit.CreateRect("Banner", root, new Vector2(0.5f, 0.5f), new Vector2(0f, layout.center.y - HudLayout.Height * 0.5f), layout.size);
            coopGlow = HudKit.CreateImage("CoopGlow", banner, UiTokens.WithAlpha(UiTokens.Coop, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 2f), new Vector2(640f, 120f));
            coopGlow.sprite = UiSprites.Glow;
            coopGlow.transform.SetAsFirstSibling();
            for (int i = 0; i < elementIcons.Length; i++)
            {
                elementIcons[i] = UiWidgets.ElementIcon($"Element{i}", banner, Element.None, new Vector2(0.5f, 0.5f), new Vector2(i == 0 ? -230f : 230f, 4f), 32f);
                HudKit.AddOutline(elementIcons[i], 0.6f);
            }

            nameText = HudKit.CreateText("ComboName", banner, UiTokens.Display, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(900f, 56f));
            nameText.fontStyle = FontStyle.Bold;
            HudKit.AddOutline(nameText, 0.85f);
            detailText = HudKit.CreateText("ComboDetail", banner, UiTokens.Body, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(900f, 30f));
            HudKit.AddOutline(detailText, 0.7f);
            // Co-op extras: a gold tag above the name, who set it up and who finished it below.
            coopTagText = HudKit.CreateText("CoopTag", banner, UiTokens.Label, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 42f), new Vector2(900f, 30f));
            coopTagText.color = UiTokens.Coop;
            coopTagText.fontStyle = FontStyle.Bold;
            HudKit.AddOutline(coopTagText, 0.85f);
            participantsText = HudKit.CreateText("Participants", banner, UiTokens.Caption, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -42f), new Vector2(900f, 28f));
            HudKit.AddOutline(participantsText, 0.75f);
            coopTagText.gameObject.SetActive(false);
            participantsText.gameObject.SetActive(false);
            noticeText = HudKit.CreateText("DebugNotice", root, UiTokens.Body, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -200f), new Vector2(1000f, 30f));
            HudKit.AddOutline(noticeText, 0.7f);
            banner.gameObject.SetActive(false);
            noticeText.gameObject.SetActive(false);
        }

        void OnEnable()
        {
            ComboEvents.Triggered += OnComboTriggered;
        }

        void OnDisable()
        {
            ComboEvents.Triggered -= OnComboTriggered;
        }

        void Start()
        {
            if (deck == null)
            {
                FpsCharacter human = FpsCharacter.FindLocalHuman();
                deck = human != null ? human.GetComponent<DeckController>() : FindAnyObjectByType<DeckController>();
            }
        }

        void OnComboTriggered(ComboReport report)
        {
            if (report.Definition == null)
            {
                return;
            }

            bool repeat = report.Definition == lastCombo && bannerAge < RepeatWindow;
            repeatCount = repeat ? repeatCount + 1 : 1;
            lastCombo = report.Definition;
            bannerAge = 0f;

            nameText.text = repeatCount > 1 ? $"{report.Definition.displayName}  ×{repeatCount}" : report.Definition.displayName;
            nameText.color = Color.Lerp(report.Definition.color, Color.white, 0.25f);

            string tier = ComboDefinition.TierLabel(report.Tier);
            string detail = string.IsNullOrEmpty(tier) ? "コンボ" : tier + "コンボ";
            coopBanner = report.IsCoop;
            if (report.IsCoop)
            {
                float bonus = (report.Trigger.DamageMultiplier - 1f) * 100f;
                detail += $"　協力ボーナス +{bonus:0}%（×{report.Trigger.DamageMultiplier:0.0#}）";
                coopTagText.text = "★ 協力コンボ！ ★";
                participantsText.text = $"{Participant(report.Trigger.Applier)}［{ElementName(report.Trigger.PartnerElement)}］ → " +
                                        $"{Participant(report.Trigger.Completer)}［{ElementName(report.Trigger.IncomingElement)}］";
            }
            else
            {
                participantsText.text = $"{Participant(report.Trigger.Completer)}［{ElementName(report.Trigger.PartnerElement)}＋{ElementName(report.Trigger.IncomingElement)}］";
            }

            coopTagText.gameObject.SetActive(report.IsCoop);
            participantsText.gameObject.SetActive(true);
            participantsText.color = report.IsCoop ? Color.Lerp(UiTokens.Coop, UiTokens.TextMain, 0.5f) : UiTokens.TextMain;
            SetElementIcon(elementIcons[0], report.Trigger.PartnerElement);
            SetElementIcon(elementIcons[1], report.Trigger.IncomingElement);
            LastParticipants = participantsText.text;
            detailText.text = detail;
            banner.gameObject.SetActive(true);
        }

        void ShowNotice(string message)
        {
            noticeText.text = message;
            noticeAge = 0f;
            noticeText.gameObject.SetActive(true);
        }

        void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.f4Key.wasPressedThisFrame)
            {
                VfxQuality quality = PerformanceSettings.CycleVfxQuality();
                ShowNotice("エフェクト品質: " + QualityLabel(quality));
            }

            if (keyboard.f6Key.wasPressedThisFrame)
            {
                ComboSystem.TierOverride = NextTier(ComboSystem.TierOverride);
                ShowNotice("コンボ段階: " + (ComboSystem.TierOverride.HasValue ? TierName(ComboSystem.TierOverride.Value) : "自動（カードの強化段階）"));
            }

            if (keyboard.f7Key.wasPressedThisFrame && deck != null && deck.HasPracticeDeck)
            {
                deck.SetPracticeDeck(!deck.UsingPracticeDeck);
                ShowNotice(deck.UsingPracticeDeck ? "コンボ練習デッキ（F7 で初期デッキに戻す）" : "初期デッキ");
            }
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (banner.gameObject.activeSelf)
            {
                bannerAge += Time.deltaTime; // game time: the banner belongs to the moment in the fight
                float pop = bannerAge < PopTime ? Mathf.Lerp(coopBanner ? 1.45f : 1.3f, 1f, bannerAge / PopTime) : 1f;
                banner.localScale = Vector3.one * pop;
                float hold = coopBanner ? HoldTime + 0.5f : HoldTime;
                float alpha = bannerAge < PopTime + hold ? 1f : 1f - (bannerAge - PopTime - hold) / FadeTime;
                SetAlpha(nameText, alpha);
                SetAlpha(detailText, alpha * 0.9f);
                SetAlpha(coopTagText, alpha);
                SetAlpha(participantsText, alpha * 0.9f);

                // The two element icons resonate: they pulse together and drift toward the name.
                float resonance = bannerAge < 0.5f ? Mathf.Sin(bannerAge / 0.5f * Mathf.PI * 3f) * (1f - bannerAge / 0.5f) : 0f;
                float converge = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(bannerAge / 0.25f));
                for (int i = 0; i < elementIcons.Length; i++)
                {
                    Image icon = elementIcons[i];
                    icon.rectTransform.anchoredPosition = new Vector2((i == 0 ? -1f : 1f) * Mathf.Lerp(250f, 230f, converge), 4f);
                    icon.rectTransform.localScale = Vector3.one * (1f + 0.25f * resonance);
                    SetAlpha(icon, alpha);
                }
                // Soft gold glow behind a co-op banner (very low alpha so it never hides enemies).
                float glow = coopBanner ? alpha * (0.22f + 0.08f * Mathf.Sin(bannerAge * 18f)) : 0f;
                SetAlpha(coopGlow, glow);
                if (alpha <= 0f)
                {
                    banner.gameObject.SetActive(false);
                }
            }

            if (noticeText.gameObject.activeSelf)
            {
                noticeAge += dt;
                float alpha = noticeAge < 1.4f ? 1f : 1f - (noticeAge - 1.4f) / 0.4f;
                SetAlpha(noticeText, alpha);
                if (alpha <= 0f)
                {
                    noticeText.gameObject.SetActive(false);
                }
            }
        }

        static void SetElementIcon(Image icon, Element element)
        {
            icon.sprite = UiSprites.ElementIcon(element);
            icon.color = UiTokens.ElementColor(element);
        }

        static void SetAlpha(Graphic graphic, float alpha)
        {
            Color c = graphic.color;
            c.a = Mathf.Clamp01(alpha);
            graphic.color = c;
        }

        static ComboTier? NextTier(ComboTier? current)
        {
            if (!current.HasValue)
            {
                return ComboTier.Normal;
            }

            if (current.Value == ComboTier.Ultimate)
            {
                return null;
            }

            return current.Value + 1;
        }

        static string TierName(ComboTier tier)
        {
            switch (tier)
            {
                case ComboTier.Enhanced: return "強化";
                case ComboTier.Ultimate: return "究極";
                default: return "通常";
            }
        }

        static string QualityLabel(VfxQuality quality)
        {
            switch (quality)
            {
                case VfxQuality.Low: return "低（Ally X 向け軽量）";
                case VfxQuality.Medium: return "中";
                default: return "高";
            }
        }
    }
}
