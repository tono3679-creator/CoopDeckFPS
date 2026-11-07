using CoopDeck.Combos;
using CoopDeck.Core;
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
        Text noticeText;
        float bannerAge = float.PositiveInfinity;
        float noticeAge = float.PositiveInfinity;
        ComboDefinition lastCombo;
        int repeatCount;

        public string LastShownName => nameText != null ? nameText.text : "";

        public void Configure(DeckController deckController)
        {
            deck = deckController;
        }

        void Awake()
        {
            root = HudKit.CreateCanvas(transform, "Combo HUD Canvas", 12);
            banner = HudKit.CreateRect("Banner", root, new Vector2(0.5f, 0.5f), new Vector2(0f, 205f), new Vector2(900f, 90f));
            nameText = HudKit.CreateText("ComboName", banner, 44, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(900f, 56f));
            HudKit.AddOutline(nameText, 0.8f);
            detailText = HudKit.CreateText("ComboDetail", banner, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -26f), new Vector2(900f, 30f));
            HudKit.AddOutline(detailText, 0.6f);
            noticeText = HudKit.CreateText("DebugNotice", root, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1000f, 30f));
            HudKit.AddOutline(noticeText, 0.6f);
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
                deck = FindAnyObjectByType<DeckController>();
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
            if (report.IsCoop)
            {
                detail += $"　協力 ×{report.Trigger.DamageMultiplier:0.0#}";
            }

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
                float pop = bannerAge < PopTime ? Mathf.Lerp(1.3f, 1f, bannerAge / PopTime) : 1f;
                banner.localScale = Vector3.one * pop;
                float alpha = bannerAge < PopTime + HoldTime ? 1f : 1f - (bannerAge - PopTime - HoldTime) / FadeTime;
                SetAlpha(nameText, alpha);
                SetAlpha(detailText, alpha * 0.9f);
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
