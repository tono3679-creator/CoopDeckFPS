using CoopDeck.Cards;
using CoopDeck.Deck;
using CoopDeck.Items;
using CoopDeck.Player;
using CoopDeck.Skills;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// The skill hand, compact (Docs/UI_DESIGN.md §5): small slots at the bottom right (icon,
    /// uses, gauge, cooldown, key for the active device), the support-item slot, draw / discard
    /// counts, a short toast with the name and description of each card that enters the hand,
    /// and the deck notice line. A used-up card slides out toward the discard side and the new one
    /// slides in. Reads <see cref="DeckController"/> and <see cref="DeckState"/> events only.
    /// </summary>
    public class DeckHud : MonoBehaviour
    {
        const float DiscardDuration = 0.22f;
        const float DrawDuration = 0.26f;
        const float ToastHold = 2.2f;
        const float ToastFade = 0.4f;

        [SerializeField] DeckController deckController;

        RectTransform root;
        SlotWidget[] widgets = new SlotWidget[0];
        Text pilesText;
        Text noticeText;
        float noticeTimer;
        ItemSlot itemSlot;
        DeckState boundDeck;
        GaugeItemInventory boundItems;
        InputScheme shownScheme = (InputScheme)(-1);
        int lastDraw = -1;
        int lastDiscard = -1;
        int lastItemIndex = -1;
        bool dealing;

        RectTransform toast;
        CanvasGroup toastGroup;
        Image toastIcon;
        Text toastName;
        Text toastDescription;
        float toastAge = float.PositiveInfinity;

        enum AnimState
        {
            Idle,
            Discarding,
            Drawing,
        }

        sealed class SlotWidget
        {
            public SkillSlotView View;
            public Vector2 Home;
            public CardRuntime Shown;
            public CardRuntime Pending;
            public bool HasPending;
            public AnimState State;
            public float Time;
            public float Delay;
        }

        sealed class ItemSlot
        {
            public RectTransform Root;
            public Image Icon;
            public Text Count;
            public Text Key;
            public Image[] Dots = new Image[0];
            public LampView Lamp;
            public int LastCount = -1;
        }

        public int SlotCount => widgets.Length;

        /// <summary>Name of the card a slot currently shows (tests, debugging).</summary>
        public string SlotCardName(int slot) => slot >= 0 && slot < widgets.Length && widgets[slot].Shown != null ? widgets[slot].Shown.Definition.displayName : "";

        /// <summary>The draw / discard counts line (tests, debugging).</summary>
        public string PilesLine => pilesText != null ? pilesText.text : "";

        /// <summary>The name in the draw toast (tests, debugging).</summary>
        public string ToastName => toastName != null ? toastName.text : "";

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
            pilesText = UiWidgets.Label("Piles", root, UiTokens.Caption, UiTokens.TextSub, TextAnchor.LowerRight, Vector2.zero, Vector2.zero, HudLayout.Piles.size,
                false, false);
            HudLayout.Place(pilesText.rectTransform, HudLayout.Piles);
            pilesText.supportRichText = true;
            pilesText.lineSpacing = 1.15f;
            HudKit.AddOutline(pilesText, 0.85f);
            BuildItemSlot();
            BuildToast();
            noticeText = UiWidgets.Label("Notice", root, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleCenter, Vector2.zero, Vector2.zero,
                HudLayout.DeckNotice.size, true, false);
            HudLayout.Place(noticeText.rectTransform, HudLayout.DeckNotice);
            HudKit.AddOutline(noticeText, 0.85f);
            noticeText.color = UiTokens.WithAlpha(UiTokens.TextMain, 0f);
        }

        void Start()
        {
            if (deckController == null)
            {
                FpsCharacter human = FpsCharacter.FindLocalHuman();
                deckController = human != null ? human.GetComponent<DeckController>() : FindAnyObjectByType<DeckController>();
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
            boundDeck.CardUsed += OnCardUsed;
            boundDeck.Reshuffled += OnReshuffled;
            boundItems = deckController.Items;
            BuildSlots(deck.HandSize);
            BuildItemDots();
            lastItemIndex = boundItems != null ? boundItems.SelectedIndex : -1;

            // Deal the opening hand with a short stagger (no toast for the opening hand).
            dealing = true;
            for (int slot = 0; slot < widgets.Length; slot++)
            {
                widgets[slot].Shown = null;
                BeginDraw(widgets[slot], deck.GetHandCard(slot), slot * 0.07f);
            }

            dealing = false;
        }

        void UnbindDeck()
        {
            if (boundDeck != null)
            {
                boundDeck.CardDrawn -= OnCardDrawn;
                boundDeck.CardDepleted -= OnCardDepleted;
                boundDeck.CardUsed -= OnCardUsed;
                boundDeck.Reshuffled -= OnReshuffled;
            }

            boundDeck = null;
            boundItems = null;
        }

        // ---------------------------------------------------------------- deck events

        void OnCardUsed(CardUseResult result)
        {
            if (result.Slot >= 0 && result.Slot < widgets.Length)
            {
                widgets[result.Slot].View.FlashUse();
            }
        }

        void OnCardDepleted(int slot, CardRuntime card)
        {
            if (slot < 0 || slot >= widgets.Length)
            {
                return;
            }

            SlotWidget widget = widgets[slot];
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

            SlotWidget widget = widgets[slot];
            if (widget.State == AnimState.Discarding)
            {
                widget.Pending = card;
                widget.HasPending = true;
            }
            else
            {
                BeginDraw(widget, card, 0f);
            }

            ShowToast(card);
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

        void ShowToast(CardRuntime card)
        {
            if (card == null || dealing)
            {
                return;
            }

            CardDefinition def = card.Definition;
            toastIcon.sprite = def.icon != null ? def.icon : UiSprites.ElementIcon(def.element);
            toastIcon.color = def.icon != null ? Color.white : UiTokens.ElementColor(def.element);
            toastName.text = def.displayName;
            toastName.color = UiTokens.ElementText(def.element);
            toastDescription.text = string.IsNullOrEmpty(def.description) ? $"{ElementStyle.DisplayName(def.element)}属性・消費 {card.GaugeCost:0}" : def.description;
            toastAge = 0f;
            toast.gameObject.SetActive(true);
        }

        void BeginDraw(SlotWidget widget, CardRuntime card, float delay)
        {
            widget.Shown = card;
            widget.HasPending = false;
            widget.State = AnimState.Drawing;
            widget.Time = 0f;
            widget.Delay = delay;
        }

        // ---------------------------------------------------------------- build

        void BuildSlots(int count)
        {
            foreach (SlotWidget old in widgets)
            {
                if (old?.View?.Root != null)
                {
                    Destroy(old.View.Root.gameObject);
                }
            }

            widgets = new SlotWidget[count];
            for (int i = 0; i < count; i++)
            {
                var widget = new SlotWidget { View = SkillSlotView.Create($"Slot{i + 1}", root) };
                HudLayout.Place(widget.View.Root, HudLayout.SkillSlot(i, count));
                widget.Home = widget.View.Root.anchoredPosition;
                widgets[i] = widget;
            }

            shownScheme = (InputScheme)(-1);
        }

        void BuildItemSlot()
        {
            Rect layout = HudLayout.ItemSlot;
            var slot = new ItemSlot { Root = HudKit.CreateRect("ItemSlot", root, Vector2.zero, Vector2.zero, Vector2.zero, layout.size) };
            HudLayout.Place(slot.Root, layout);
            RectTransform square = HudKit.CreateRect("Square", slot.Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero,
                new Vector2(HudLayout.SlotSize, HudLayout.SlotSize));
            Image fill = UiWidgets.SlicedImage("Fill", square, UiSprites.Fill(UiSprites.FrameStyle.Small), UiTokens.WithAlpha(UiTokens.PanelDeep, 0.88f));
            HudKit.Stretch(fill.rectTransform);
            slot.Icon = UiWidgets.Icon("Icon", square, UiSprites.CrossIcon, UiTokens.Health, new Vector2(0.5f, 0.5f), new Vector2(0f, 4f), 38f);
            Image frame = UiWidgets.SlicedImage("Frame", square, UiSprites.Frame(UiSprites.FrameStyle.Small), UiTokens.FrameSteel);
            HudKit.Stretch(frame.rectTransform);
            slot.Lamp = UiWidgets.Lamp("Lamp", square, new Vector2(0f, 1f), new Vector2(14f, -10f), new Vector2(14f, 7f));
            slot.Count = UiWidgets.Label("Count", square, UiTokens.Body, UiTokens.TextMain, TextAnchor.LowerRight, new Vector2(1f, 0f), new Vector2(-22f, 14f),
                new Vector2(40f, 22f), true, false);
            HudKit.AddOutline(slot.Count, 0.9f);
            slot.Key = UiWidgets.KeyBadge("Key", slot.Root, new Vector2(0.5f, 0f), new Vector2(0f, HudLayout.KeyRow * 0.5f),
                new Vector2(HudLayout.SlotSize - 12f, HudLayout.KeyRow - 2f), out _);
            slot.Key.fontSize = UiTokens.Caption;
            slot.Key.resizeTextMaxSize = UiTokens.Caption;
            itemSlot = slot;
        }

        void BuildItemDots()
        {
            foreach (Image dot in itemSlot.Dots)
            {
                if (dot != null)
                {
                    Destroy(dot.gameObject);
                }
            }

            int count = boundItems != null ? boundItems.ItemTypeCount : 0;
            itemSlot.Dots = new Image[count];
            Transform square = itemSlot.Root.Find("Square");
            for (int i = 0; i < count; i++)
            {
                itemSlot.Dots[i] = UiWidgets.Icon("ItemDot", square, UiSprites.Diamond, UiTokens.TextSub, new Vector2(1f, 1f), new Vector2(-10f - (count - 1 - i) * 10f, -10f), 8f);
            }
        }

        void BuildToast()
        {
            Rect layout = HudLayout.DrawToast;
            PanelView panel = UiWidgets.Panel("DrawToast", root, Vector2.zero, Vector2.zero, Vector2.zero, layout.size, UiSprites.FrameStyle.Small, false,
                UiTokens.WithAlpha(UiTokens.Background, 0.72f), UiTokens.WithAlpha(UiTokens.FrameSteel, 0.6f));
            HudLayout.Place(panel.Root, layout);
            toast = panel.Root;
            toastGroup = toast.gameObject.AddComponent<CanvasGroup>();
            toastGroup.interactable = false;
            toastGroup.blocksRaycasts = false;
            toastIcon = UiWidgets.Icon("Icon", toast, HudKit.WhiteSprite, Color.white, new Vector2(0f, 0.5f), new Vector2(30f, 0f), 38f);
            toastName = UiWidgets.Label("Name", toast, UiTokens.Title, UiTokens.TextMain, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(270f, -16f),
                new Vector2(450f, 24f), true);
            toastDescription = UiWidgets.Label("Description", toast, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleLeft, new Vector2(0f, 0f),
                new Vector2(270f, 14f), new Vector2(450f, 20f));
            toastDescription.horizontalOverflow = HorizontalWrapMode.Wrap;
            toastDescription.verticalOverflow = VerticalWrapMode.Truncate;
            toast.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- update

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            InputGlyphs.Poll();
            if (boundDeck == null && deckController != null && deckController.Deck != null)
            {
                BindDeck();
            }

            if (boundDeck != null && (boundDeck.DrawPileCount != lastDraw || boundDeck.DiscardPileCount != lastDiscard))
            {
                lastDraw = boundDeck.DrawPileCount;
                lastDiscard = boundDeck.DiscardPileCount;
                string main = ColorUtility.ToHtmlStringRGB(UiTokens.TextMain);
                pilesText.text = $"山札 <color=#{main}><b>{lastDraw}</b></color>\n捨て札 <color=#{main}><b>{lastDiscard}</b></color>";
            }

            shownScheme = InputGlyphs.Active;
            int selected = deckController != null ? deckController.SelectedSlot : -1;
            for (int i = 0; i < widgets.Length; i++)
            {
                SlotWidget w = widgets[i];
                Animate(w, dt);
                w.View.Apply(CardViewData.From(w.Shown, InputGlyphs.SkillLabel(i, shownScheme), i == selected, w.State == AnimState.Discarding));
                w.View.Tick(dt);
            }

            UpdateItems(dt);
            UpdateToast(dt);

            noticeTimer -= dt;
            noticeText.color = UiTokens.WithAlpha(UiTokens.TextMain, Mathf.Clamp01(noticeTimer / 0.4f));
        }

        void Animate(SlotWidget w, float dt)
        {
            Vector2 offset = Vector2.zero;
            float alpha = 1f;
            float scale = 1f;

            if (w.State == AnimState.Discarding)
            {
                w.Time += dt;
                float t = Mathf.Clamp01(w.Time / DiscardDuration);
                float ease = t * t;
                offset = new Vector2(0f, -40f * ease);
                alpha = 1f - t;
                scale = 1f - 0.12f * t;
                if (t >= 1f)
                {
                    if (w.HasPending)
                    {
                        BeginDraw(w, w.Pending, 0.03f);
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
                    float ease = 1f - (1f - t) * (1f - t) * (1f - t);
                    offset = new Vector2(-46f * (1f - ease), 0f);
                    alpha = Mathf.Clamp01(t * 1.6f);
                    scale = 1.06f - 0.06f * ease;
                    if (t >= 1f)
                    {
                        w.State = AnimState.Idle;
                    }
                }
            }

            RectTransform rect = w.View.Root;
            rect.anchoredPosition = w.Home + offset;
            rect.localScale = Vector3.one * scale;
            w.View.Group.alpha = alpha;
        }

        void UpdateItems(float dt)
        {
            itemSlot.Key.text = InputGlyphs.Label(HudAction.UseItem);
            if (boundItems == null || boundItems.ItemTypeCount == 0)
            {
                itemSlot.Root.gameObject.SetActive(false);
                return;
            }

            itemSlot.Root.gameObject.SetActive(true);
            int index = boundItems.SelectedIndex;
            GaugeItemDefinition item = boundItems.Selected;
            int count = boundItems.GetCount(index);
            if (index != lastItemIndex)
            {
                if (lastItemIndex >= 0 && item != null)
                {
                    string target = item.target == GaugeItemTarget.SelectedCard ? "選択カード" : "手札すべて";
                    ShowNotice($"{item.displayName}（{target} +{item.amount:0}）　{InputGlyphs.Label(HudAction.CycleItem)} で切替");
                }

                lastItemIndex = index;
                itemSlot.LastCount = -1;
                if (item != null)
                {
                    itemSlot.Icon.sprite = item.icon != null ? item.icon : item.target == GaugeItemTarget.SelectedCard ? UiSprites.CrossIcon : UiSprites.Diamond;
                    itemSlot.Icon.color = item.icon != null ? Color.white : item.target == GaugeItemTarget.SelectedCard ? UiTokens.Health : UiTokens.Energy;
                }
            }

            if (count != itemSlot.LastCount)
            {
                if (itemSlot.LastCount > count)
                {
                    itemSlot.Lamp.Flash();
                }

                itemSlot.LastCount = count;
                itemSlot.Count.text = "×" + count;
                itemSlot.Count.color = count == 0 ? UiTokens.TextMuted : UiTokens.TextMain;
                itemSlot.Icon.color = UiTokens.WithAlpha(itemSlot.Icon.color, count == 0 ? 0.35f : 1f);
            }

            for (int i = 0; i < itemSlot.Dots.Length; i++)
            {
                itemSlot.Dots[i].color = i == index ? UiTokens.Accent : UiTokens.WithAlpha(UiTokens.TextSub, 0.6f);
            }

            itemSlot.Lamp.Tick(dt);
            itemSlot.Lamp.Set(count > 0);
        }

        void UpdateToast(float dt)
        {
            if (!toast.gameObject.activeSelf)
            {
                return;
            }

            toastAge += dt;
            float alpha = toastAge < 0.15f ? toastAge / 0.15f : toastAge > ToastHold ? 1f - (toastAge - ToastHold) / ToastFade : 1f;
            toastGroup.alpha = Mathf.Clamp01(alpha);
            toast.localScale = Vector3.one * (toastAge < 0.15f ? Mathf.Lerp(0.96f, 1f, toastAge / 0.15f) : 1f);
            if (toastAge > ToastHold + ToastFade)
            {
                toast.gameObject.SetActive(false);
            }
        }
    }
}
