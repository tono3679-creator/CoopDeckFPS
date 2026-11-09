using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// The shared terminal button: framed plate, label, an amber lamp that lights on press and
    /// corner brackets while focused (mouse hover or controller navigation, so the focus never
    /// relies on colour alone). Add it with <see cref="Create"/>; wire <see cref="Button.onClick"/>
    /// to a real action — a button with nothing behind it must be created non-interactable.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class TerminalButton : MonoBehaviour, ISelectHandler, IDeselectHandler, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler, ISubmitHandler
    {
        public enum VisualState
        {
            Normal,
            Focused,
            Pressed,
            Disabled,
        }

        Button button;
        PanelView plate;
        LampView lamp;
        Image brackets;
        Text label;
        bool focused;
        bool pressed;
        float submitFlash;
        VisualState? forced;

        public Button Button => button;
        public Text Label => label;

        public static TerminalButton Create(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size, string text,
            bool interactable)
        {
            PanelView plate = UiWidgets.Panel(name, parent, anchor, pivot, position, size, UiSprites.FrameStyle.Small, false,
                UiTokens.WithAlpha(UiTokens.PanelDeep, 0.95f));
            plate.Fill.raycastTarget = true;
            var button = plate.Root.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = plate.Fill;
            button.interactable = interactable;
            var view = plate.Root.gameObject.AddComponent<TerminalButton>();
            view.button = button;
            view.plate = plate;
            view.lamp = UiWidgets.Lamp("Lamp", plate.Root, new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(18f, 9f));
            view.label = UiWidgets.Label("Label", plate.Root, UiTokens.Label, UiTokens.TextMain, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(10f, 0f), size - new Vector2(50f, 0f), true);
            view.label.text = text;
            view.brackets = UiWidgets.FocusBrackets("Focus", plate.Root, UiTokens.Accent, 6f);
            view.Apply();
            return view;
        }

        /// <summary>Shows a fixed state (style previews); null returns to live input.</summary>
        public void ForceState(VisualState? state)
        {
            forced = state;
            Apply();
        }

        public VisualState State
        {
            get
            {
                if (forced.HasValue) return forced.Value;
                if (button != null && !button.interactable) return VisualState.Disabled;
                if (pressed || submitFlash > 0f) return VisualState.Pressed;
                return focused ? VisualState.Focused : VisualState.Normal;
            }
        }

        void Update()
        {
            if (submitFlash > 0f)
            {
                submitFlash -= Time.unscaledDeltaTime;
                Apply();
            }

            lamp?.Tick(Time.unscaledDeltaTime);
        }

        void Apply()
        {
            if (plate == null)
            {
                return;
            }

            VisualState state = State;
            brackets.enabled = state == VisualState.Focused || state == VisualState.Pressed;
            lamp.Set(state == VisualState.Pressed || state == VisualState.Focused, 0f);
            lamp.OnColor = state == VisualState.Pressed ? Color.Lerp(UiTokens.Accent, Color.white, 0.35f) : UiTokens.Accent;
            plate.Frame.color = state == VisualState.Disabled ? UiTokens.WithAlpha(UiTokens.FrameSteel, 0.45f) :
                state == VisualState.Normal ? UiTokens.FrameSteel : UiTokens.Accent;
            plate.Fill.color = state == VisualState.Pressed ? UiTokens.WithAlpha(Color.Lerp(UiTokens.PanelDeep, UiTokens.Accent, 0.18f), 0.95f) :
                UiTokens.WithAlpha(UiTokens.PanelDeep, 0.95f);
            label.color = state == VisualState.Disabled ? UiTokens.TextMuted : UiTokens.TextMain;
        }

        public void OnSelect(BaseEventData eventData) { focused = true; Apply(); }
        public void OnDeselect(BaseEventData eventData) { focused = false; pressed = false; Apply(); }
        public void OnPointerEnter(PointerEventData eventData) { focused = true; Apply(); }
        public void OnPointerExit(PointerEventData eventData) { focused = false; pressed = false; Apply(); }
        public void OnPointerDown(PointerEventData eventData) { pressed = true; lamp.Flash(); Apply(); }
        public void OnPointerUp(PointerEventData eventData) { pressed = false; Apply(); }
        public void OnSubmit(BaseEventData eventData) { submitFlash = 0.15f; lamp.Flash(); Apply(); }
    }
}
