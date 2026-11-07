using CoopDeck.Cards;
using CoopDeck.Deck;
using CoopDeck.Items;
using CoopDeck.Skills;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// The card hand at the bottom of the screen: one card per slot with name, icon, element,
    /// gauge, cost, uses left, cooldown and key labels; draw / discard pile counts; the gauge-item
    /// panel; short discard and draw animations. Reads <see cref="DeckController"/> and
    /// <see cref="DeckState"/> only — no gameplay logic lives here.
    /// Layout (1920×1080 reference): cards between x 592 and 1328, so the crosshair, HP panel and
    /// ammo panel stay clear.
    /// </summary>
    public class DeckHud : MonoBehaviour
    {
        const float CardWidth = 236f;
        const float CardHeight = 156f;
        const float CardSpacing = 250f;
        const float CardY = 102f;
        const float DiscardDuration = 0.22f;
        const float DrawDuration = 0.28f;

        static readonly string[] KeyboardKeys = { "Q", "E", "F" };
        static readonly string[] GamepadKeys = { "LB", "RB", "Y" };
        static readonly Color SelectedFrameColor = new Color(1f, 0.85f, 0.35f, 0.95f);
        static readonly Color DepletedColor = new Color(1f, 0.35f, 0.35f, 1f);

        [SerializeField] DeckController deckController;

        RectTransform root;
        CardWidget[] widgets = new CardWidget[0];
        Text drawCountText;
        Text discardCountText;
        Text noticeText;
        float noticeTimer;
        RectTransform itemPanel;
        Text itemTitle;
        Text[] itemRows = new Text[0];
        DeckState boundDeck;
        GaugeItemInventory boundItems;

        enum AnimState
        {
            Idle,
            Discarding,
            Drawing,
        }

        sealed class CardWidget
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Frame;
            public Image Background;
            public Image Stripe;
            public Image Icon;
            public Text Name;
            public Text ElementLabel;
            public Text KeyLabel;
            public Text PadLabel;
            public Text GaugeLabel;
            public Text CostLabel;
            public Text UsesLabel;
            public Text CooldownLabel;
            public Image GaugeBack;
            public Image GaugeFill;
            public Image CooldownShade;
            public Text SelectTag;
            public Text EmptyLabel;

            public CardRuntime Shown;
            public CardRuntime Pending;
            public bool HasPending;
            public AnimState State;
            public float Time;
            public float Delay;
        }

        public void Bind(DeckController controller)
        {
            Unbind();
            deckController = controller;
            if (isActiveAndEnabled)
            {
                BindController();
            }
        }

        void Awake()
        {
            root = HudKit.CreateCanvas(transform, "Deck HUD Canvas", 11);
            BuildPiles();
            BuildItemPanel();
            noticeText = HudKit.CreateText("Notice", root, 24, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 214f), new Vector2(900f, 34f));
            HudKit.AddOutline(noticeText, 0.7f);
            noticeText.color = new Color(1f, 1f, 1f, 0f);
        }

        void Start()
        {
            if (deckController == null)
            {
                deckController = FindAnyObjectByType<DeckController>();
            }

            BindController();
        }

        void OnEnable()
        {
            if (deckController != null && boundDeck == null)
            {
                BindController();
            }
        }

        void OnDisable() => Unbind();

        void BindController()
        {
            if (deckController == null)
            {
                return;
            }

            deckController.DeckRebuilt -= OnDeckRebuilt;
            deckController.DeckRebuilt += OnDeckRebuilt;
            deckController.Notice -= ShowNotice;
            deckController.Notice += ShowNotice;
            BindDeck();
        }

        void Unbind()
        {
            UnbindDeck();
            if (deckController != null)
            {
                deckController.DeckRebuilt -= OnDeckRebuilt;
                deckController.Notice -= ShowNotice;
            }
        }

        void OnDeckRebuilt()
        {
            UnbindDeck();
            BindDeck();
        }

        void BindDeck()
        {
            DeckState deck = deckController != null ? deckController.Deck : null;
            if (deck == null || deck == boundDeck)
            {
                return;
            }

            boundDeck = deck;
            boundDeck.CardDrawn += OnCardDrawn;
            boundDeck.CardDepleted += OnCardDepleted;
            boundDeck.Reshuffled += OnReshuffled;
            boundItems = deckController.Items;
            BuildCards(deck.HandSize);
            BuildItemRows();

            // Deal the opening hand with a short stagger.
            for (int slot = 0; slot < widgets.Length; slot++)
            {
                widgets[slot].Shown = null;
                BeginDraw(widgets[slot], deck.GetHandCard(slot), slot * 0.08f);
            }
        }

        void UnbindDeck()
        {
            if (boundDeck != null)
            {
                boundDeck.CardDrawn -= OnCardDrawn;
                boundDeck.CardDepleted -= OnCardDepleted;
                boundDeck.Reshuffled -= OnReshuffled;
            }

            boundDeck = null;
            boundItems = null;
        }

        // ---------------------------------------------------------------- deck events

        void OnCardDepleted(int slot, CardRuntime card)
        {
            if (slot < 0 || slot >= widgets.Length)
            {
                return;
            }

            CardWidget widget = widgets[slot];
            widget.Shown = card;
            widget.HasPending = false;
            widget.State = AnimState.Discarding;
            widget.Time = 0f;
            widget.Delay = 0f;
        }

        void OnCardDrawn(int slot, CardRuntime card)
        {
            if (slot < 0 || slot >= widgets.Length)
            {
                return;
            }

            CardWidget widget = widgets[slot];
            if (widget.State == AnimState.Discarding)
            {
                widget.Pending = card;
                widget.HasPending = true;
            }
            else
            {
                BeginDraw(widget, card, 0f);
            }
        }

        void OnReshuffled(int count)
        {
            ShowNotice($"捨て札 {count} 枚をシャッフルして山札に戻しました");
        }

        void ShowNotice(string message)
        {
            noticeText.text = message;
            noticeTimer = 1.6f;
        }

        void BeginDraw(CardWidget widget, CardRuntime card, float delay)
        {
            widget.Shown = card;
            widget.HasPending = false;
            widget.State = AnimState.Drawing;
            widget.Time = 0f;
            widget.Delay = delay;
        }

        // ---------------------------------------------------------------- build

        void BuildPiles()
        {
            drawCountText = BuildPile("DrawPile", -CardSpacing - CardWidth * 0.5f - 72f, "山札");
            discardCountText = BuildPile("DiscardPile", CardSpacing + CardWidth * 0.5f + 72f, "捨て札");
        }

        Text BuildPile(string name, float x, string label)
        {
            Image panel = HudKit.CreateImage(name, root, HudKit.PanelColor, new Vector2(0.5f, 0f), new Vector2(x, CardY - 20f), new Vector2(104f, 92f));
            Text title = HudKit.CreateText("Label", panel.rectTransform, 17, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(100f, 24f));
            title.text = label;
            title.color = new Color(1f, 1f, 1f, 0.75f);
            Text count = HudKit.CreateText("Count", panel.rectTransform, 38, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(100f, 50f));
            HudKit.AddOutline(count, 0.5f);
            return count;
        }

        void BuildItemPanel()
        {
            Image panel = HudKit.CreateImage("ItemPanel", root, HudKit.PanelColor, new Vector2(1f, 0f), new Vector2(-170f, 196f), new Vector2(320f, 96f));
            itemPanel = panel.rectTransform;
            itemTitle = HudKit.CreateText("Title", itemPanel, 16, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(160f, -14f), new Vector2(300f, 22f));
            itemTitle.text = "アイテム   G / 十字↑ 使用   T / 十字↓ 切替";
            itemTitle.color = new Color(1f, 1f, 1f, 0.7f);
        }

        void BuildItemRows()
        {
            foreach (Text row in itemRows)
            {
                if (row != null)
                {
                    Destroy(row.gameObject);
                }
            }

            int count = boundItems != null ? boundItems.ItemTypeCount : 0;
            itemRows = new Text[count];
            for (int i = 0; i < count; i++)
            {
                itemRows[i] = HudKit.CreateText("ItemRow", itemPanel, 19, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(160f, -40f - i * 26f), new Vector2(300f, 26f));
                HudKit.AddOutline(itemRows[i], 0.4f);
            }
        }

        void BuildCards(int count)
        {
            foreach (CardWidget old in widgets)
            {
                if (old?.Rect != null)
                {
                    Destroy(old.Rect.gameObject);
                }
            }

            widgets = new CardWidget[count];
            for (int i = 0; i < count; i++)
            {
                float x = (i - (count - 1) * 0.5f) * CardSpacing;
                widgets[i] = BuildCard(i, x);
            }
        }

        CardWidget BuildCard(int slot, float x)
        {
            var w = new CardWidget();
            w.Rect = HudKit.CreateRect($"Card{slot + 1}", root, new Vector2(0.5f, 0f), new Vector2(x, CardY), new Vector2(CardWidth, CardHeight));
            w.Group = w.Rect.gameObject.AddComponent<CanvasGroup>();
            w.Group.interactable = false;
            w.Group.blocksRaycasts = false;

            Vector2 center = new Vector2(0.5f, 0.5f);
            w.Frame = HudKit.CreateImage("SelectFrame", w.Rect, SelectedFrameColor, center, Vector2.zero, new Vector2(CardWidth + 8f, CardHeight + 8f));
            w.Background = HudKit.CreateImage("Background", w.Rect, new Color(0.1f, 0.08f, 0.2f, 0.9f), center, Vector2.zero, new Vector2(CardWidth, CardHeight));
            w.Stripe = HudKit.CreateImage("ElementStripe", w.Rect, Color.white, new Vector2(0.5f, 1f), new Vector2(0f, -3f), new Vector2(CardWidth, 6f));

            w.Icon = HudKit.CreateImage("Icon", w.Rect, Color.white, center, new Vector2(-80f, 34f), new Vector2(60f, 60f));
            w.Name = HudKit.CreateText("Name", w.Rect, 22, TextAnchor.MiddleLeft, center, new Vector2(12f, 46f), new Vector2(116f, 28f));
            // Long names ("グラビティスパイク") shrink to fit instead of running under the key badge.
            w.Name.horizontalOverflow = HorizontalWrapMode.Wrap;
            w.Name.verticalOverflow = VerticalWrapMode.Truncate;
            w.Name.resizeTextForBestFit = true;
            w.Name.resizeTextMinSize = 12;
            w.Name.resizeTextMaxSize = 22;
            HudKit.AddOutline(w.Name, 0.4f);
            w.ElementLabel = HudKit.CreateText("Element", w.Rect, 16, TextAnchor.MiddleLeft, center, new Vector2(18f, 22f), new Vector2(128f, 22f));

            Image keyBack = HudKit.CreateImage("KeyBack", w.Rect, new Color(0f, 0f, 0f, 0.45f), center, new Vector2(94f, 42f), new Vector2(40f, 50f));
            w.KeyLabel = HudKit.CreateText("Key", keyBack.rectTransform, 28, TextAnchor.MiddleCenter, center, new Vector2(0f, 7f), new Vector2(40f, 32f));
            w.KeyLabel.text = slot < KeyboardKeys.Length ? KeyboardKeys[slot] : (slot + 1).ToString();
            w.PadLabel = HudKit.CreateText("Pad", keyBack.rectTransform, 13, TextAnchor.MiddleCenter, center, new Vector2(0f, -15f), new Vector2(40f, 16f));
            w.PadLabel.text = slot < GamepadKeys.Length ? GamepadKeys[slot] : "";
            w.PadLabel.color = new Color(1f, 1f, 1f, 0.7f);

            w.GaugeLabel = HudKit.CreateText("Gauge", w.Rect, 19, TextAnchor.MiddleLeft, center, new Vector2(-50f, -10f), new Vector2(110f, 24f));
            w.CostLabel = HudKit.CreateText("Cost", w.Rect, 16, TextAnchor.MiddleRight, center, new Vector2(52f, -10f), new Vector2(110f, 24f));
            w.GaugeBack = HudKit.CreateImage("GaugeBack", w.Rect, new Color(1f, 1f, 1f, 0.14f), center, new Vector2(0f, -34f), new Vector2(212f, 16f));
            w.GaugeFill = HudKit.CreateImage("GaugeFill", w.GaugeBack.rectTransform, Color.white, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(212f, 16f));
            w.GaugeFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            w.UsesLabel = HudKit.CreateText("Uses", w.Rect, 15, TextAnchor.MiddleLeft, center, new Vector2(-50f, -58f), new Vector2(110f, 20f));
            w.CooldownLabel = HudKit.CreateText("CooldownSmall", w.Rect, 15, TextAnchor.MiddleRight, center, new Vector2(52f, -58f), new Vector2(110f, 20f));

            w.CooldownShade = HudKit.CreateImage("CooldownShade", w.Rect, new Color(0f, 0f, 0f, 0.55f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(CardWidth, CardHeight));
            w.CooldownShade.rectTransform.pivot = new Vector2(0.5f, 1f);

            w.SelectTag = HudKit.CreateText("SelectTag", w.Rect, 15, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, 14f), new Vector2(200f, 22f));
            w.SelectTag.text = "▼ アイテム対象（1/2/3・十字←→）";
            w.SelectTag.color = SelectedFrameColor;
            HudKit.AddOutline(w.SelectTag, 0.6f);

            w.EmptyLabel = HudKit.CreateText("Empty", w.Rect, 20, TextAnchor.MiddleCenter, center, Vector2.zero, new Vector2(CardWidth, 40f));
            w.EmptyLabel.text = "（空き枠）";
            w.EmptyLabel.color = new Color(1f, 1f, 1f, 0.5f);
            return w;
        }

        // ---------------------------------------------------------------- update

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (boundDeck == null && deckController != null && deckController.Deck != null)
            {
                BindDeck();
            }

            if (boundDeck != null)
            {
                drawCountText.text = boundDeck.DrawPileCount.ToString();
                discardCountText.text = boundDeck.DiscardPileCount.ToString();
            }

            int selected = deckController != null ? deckController.SelectedSlot : -1;
            for (int i = 0; i < widgets.Length; i++)
            {
                Animate(widgets[i], dt);
                Refresh(widgets[i], i == selected);
            }

            UpdateItems();

            noticeTimer -= dt;
            Color c = noticeText.color;
            c.a = Mathf.Clamp01(noticeTimer / 0.4f);
            noticeText.color = c;
        }

        void Animate(CardWidget w, float dt)
        {
            float offsetY = 0f;
            float alpha = 1f;
            float scale = 1f;
            float tilt = 0f;

            if (w.State == AnimState.Discarding)
            {
                w.Time += dt;
                float t = Mathf.Clamp01(w.Time / DiscardDuration);
                offsetY = -70f * t * t;
                alpha = 1f - t;
                scale = 1f - 0.12f * t;
                tilt = 10f * t;
                if (t >= 1f)
                {
                    if (w.HasPending)
                    {
                        BeginDraw(w, w.Pending, 0.04f);
                    }
                    else
                    {
                        w.Shown = null;
                        w.State = AnimState.Idle;
                    }
                }
            }
            else if (w.State == AnimState.Drawing)
            {
                if (w.Delay > 0f)
                {
                    w.Delay -= dt;
                    alpha = 0f;
                }
                else
                {
                    w.Time += dt;
                    float t = Mathf.Clamp01(w.Time / DrawDuration);
                    float ease = 1f - (1f - t) * (1f - t);
                    offsetY = -60f * (1f - ease);
                    alpha = t;
                    scale = 1.08f - 0.08f * ease;
                    if (t >= 1f)
                    {
                        w.State = AnimState.Idle;
                    }
                }
            }

            w.Rect.anchoredPosition = new Vector2(w.Rect.anchoredPosition.x, CardY + offsetY);
            w.Rect.localScale = Vector3.one * scale;
            w.Rect.localRotation = Quaternion.Euler(0f, 0f, tilt);
            w.Group.alpha = alpha;
        }

        void Refresh(CardWidget w, bool selected)
        {
            CardRuntime card = w.Shown;
            bool hasCard = card != null;
            w.EmptyLabel.gameObject.SetActive(!hasCard);
            w.Frame.enabled = selected;
            w.SelectTag.enabled = selected && hasCard;

            SetCardContentActive(w, hasCard);
            if (!hasCard)
            {
                w.CooldownShade.enabled = false;
                return;
            }

            CardDefinition def = card.Definition;
            Color elementColor = ElementStyle.ColorOf(def.element);
            w.Stripe.color = elementColor;
            w.Icon.sprite = def.icon != null ? def.icon : HudKit.WhiteSprite;
            w.Icon.color = def.icon != null ? Color.white : elementColor;
            w.Name.text = def.displayName;
            w.ElementLabel.text = $"{ElementStyle.DisplayName(def.element)}属性";
            w.ElementLabel.color = elementColor;

            bool depletedView = w.State == AnimState.Discarding;
            float gauge = depletedView ? 0f : card.Gauge;
            w.GaugeLabel.text = $"{Mathf.CeilToInt(gauge)} / {Mathf.CeilToInt(card.MaxGauge)}";
            w.CostLabel.text = $"消費 {Mathf.CeilToInt(card.GaugeCost)}";
            w.GaugeFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(gauge / card.MaxGauge), 1f, 1f);
            w.GaugeFill.color = depletedView ? DepletedColor : elementColor;

            int uses = depletedView ? 0 : card.RemainingUses;
            w.UsesLabel.text = depletedView ? "使い切り → 捨て札" : uses == 1 ? "ラスト1回" : $"あと {uses} 回";
            w.UsesLabel.color = depletedView ? DepletedColor : uses == 1 ? new Color(1f, 0.65f, 0.3f) : new Color(1f, 1f, 1f, 0.75f);

            float cooldownFraction = card.Cooldown > 0f ? card.CooldownRemaining / card.Cooldown : 0f;
            bool coolingDown = !depletedView && card.CooldownRemaining > 0f;
            w.CooldownShade.enabled = coolingDown;
            w.CooldownShade.rectTransform.localScale = new Vector3(1f, Mathf.Clamp01(cooldownFraction), 1f);
            w.CooldownLabel.text = coolingDown ? $"CD {card.CooldownRemaining:0.0}s" : $"CD {card.Cooldown:0.#}s";
            w.CooldownLabel.color = coolingDown ? new Color(0.7f, 0.85f, 1f) : new Color(1f, 1f, 1f, 0.55f);
        }

        static void SetCardContentActive(CardWidget w, bool active)
        {
            w.Background.enabled = active;
            w.Stripe.enabled = active;
            w.Icon.enabled = active;
            w.Name.enabled = active;
            w.ElementLabel.enabled = active;
            w.GaugeLabel.enabled = active;
            w.CostLabel.enabled = active;
            w.GaugeBack.enabled = active;
            w.GaugeFill.enabled = active;
            w.UsesLabel.enabled = active;
            w.CooldownLabel.enabled = active;
        }

        void UpdateItems()
        {
            if (boundItems == null)
            {
                return;
            }

            for (int i = 0; i < itemRows.Length && i < boundItems.ItemTypeCount; i++)
            {
                GaugeItemDefinition item = boundItems.GetDefinition(i);
                int count = boundItems.GetCount(i);
                bool selected = i == boundItems.SelectedIndex;
                string target = item.target == GaugeItemTarget.SelectedCard ? "選択カード" : "手札すべて";
                itemRows[i].text = $"{(selected ? "▶" : "　")} {item.displayName} ×{count}   {target} +{item.amount:0}";
                Color color = selected ? new Color(1f, 0.9f, 0.45f) : new Color(1f, 1f, 1f, 0.7f);
                if (count == 0)
                {
                    color.a *= 0.5f;
                }

                itemRows[i].color = color;
            }
        }
    }
}
