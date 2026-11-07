using System.Collections.Generic;
using System.Text;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Deck;
using CoopDeck.NPC;
using CoopDeck.Skills;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Test UI for the NPC ally's deck: owned cards, the current deck list, element balance, add /
    /// remove, reset to the starting 8 and two presets. Opens with Tab (or Y while paused on a
    /// gamepad); edits only apply out of combat (the <see cref="DeckLoadoutController"/> refuses
    /// them during a fight). While open it holds the <see cref="InputGate"/> so the player does not
    /// move or shoot. Mouse, keyboard (arrows / Enter / R / 1–2) and gamepad (D-pad / A / X / Y / B).
    /// </summary>
    public class NpcDeckEditorHud : MonoBehaviour
    {
        const int MaxRows = 14;
        const float RowHeight = 30f;

        [SerializeField] DeckLoadoutController loadout;
        [SerializeField] NpcController npc;

        sealed class Row
        {
            public RectTransform Rect;
            public Image Back;
            public Text Label;
        }

        RectTransform root;
        RectTransform window;
        Text titleText;
        Text stateText;
        Text ownedHeader;
        Text deckHeader;
        Text balanceText;
        Text styleText;
        Text messageText;
        Text helpText;
        readonly Row[] ownedRows = new Row[MaxRows];
        readonly Row[] deckRows = new Row[MaxRows];
        readonly List<Row> buttons = new List<Row>();
        readonly Image[] balanceBars = new Image[5];
        readonly List<CardDefinition> ownedList = new List<CardDefinition>();
        readonly StringBuilder builder = new StringBuilder(256);

        int column;
        int row;
        bool releaseGateNextLate;
        float messageAge = float.PositiveInfinity;
        bool dirty = true;

        public bool IsOpen { get; private set; }
        public DeckLoadoutController Loadout => loadout;
        public string LastMessage => messageText != null ? messageText.text : "";

        public void Configure(DeckLoadoutController controller, NpcController ally)
        {
            loadout = controller;
            npc = ally;
        }

        void Awake()
        {
            Build();
            window.gameObject.SetActive(false);
        }

        void Start()
        {
            if (npc == null)
            {
                npc = FindAnyObjectByType<NpcController>();
            }

            if (loadout == null && npc != null)
            {
                loadout = npc.GetComponent<DeckLoadoutController>();
            }
        }

        void OnDisable()
        {
            InputGate.Release(this);
        }

        // ---------------------------------------------------------------- open / close

        public void Open()
        {
            if (loadout == null || IsOpen)
            {
                return;
            }

            IsOpen = true;
            window.gameObject.SetActive(true);
            InputGate.Block(this);
            column = 0;
            row = 0;
            dirty = true;
            if (loadout.IsLocked)
            {
                ShowMessage("戦闘中はカードを交換できません（表示のみ）");
            }
        }

        public void Close()
        {
            if (!IsOpen)
            {
                return;
            }

            IsOpen = false;
            window.gameObject.SetActive(false);
            releaseGateNextLate = true; // after every Update this frame, so the closing key is not read by the game
        }

        void LateUpdate()
        {
            if (releaseGateNextLate)
            {
                releaseGateNextLate = false;
                InputGate.Release(this);
            }

            if (messageText.gameObject.activeSelf)
            {
                messageAge += Time.unscaledDeltaTime;
                if (messageAge > 2.6f)
                {
                    messageText.gameObject.SetActive(false);
                }
            }
        }

        // ---------------------------------------------------------------- actions (public for tests)

        public LoadoutEditResult AddOwned(int index)
        {
            if (index < 0 || index >= ownedList.Count)
            {
                return LoadoutEditResult.InvalidIndex;
            }

            CardDefinition card = ownedList[index];
            LoadoutEditResult result = loadout.TryAdd(card);
            Report(result, $"{card.displayName} を追加しました");
            return result;
        }

        public LoadoutEditResult RemoveAt(int index)
        {
            CardDefinition card = index >= 0 && index < loadout.Loadout.Count ? loadout.Loadout.Cards[index] : null;
            LoadoutEditResult result = loadout.TryRemoveAt(index);
            Report(result, card != null ? $"{card.displayName} を外しました" : "");
            return result;
        }

        public LoadoutEditResult ResetDeck()
        {
            LoadoutEditResult result = loadout.ResetToDefault();
            Report(result, "初期デッキ（8枚）に戻しました");
            return result;
        }

        public LoadoutEditResult ApplyPreset(int index)
        {
            LoadoutEditResult result = loadout.ApplyPreset(index);
            string name = index >= 0 && index < loadout.Presets.Count && loadout.Presets[index] != null ? PresetName(loadout.Presets[index]) : "";
            Report(result, $"プリセット「{name}」を適用しました");
            return result;
        }

        void Report(LoadoutEditResult result, string ok)
        {
            dirty = true;
            switch (result)
            {
                case LoadoutEditResult.Ok: ShowMessage(ok + "（戦闘スタイルに反映）"); break;
                case LoadoutEditResult.Locked: ShowMessage("戦闘中はカードを交換できません"); break;
                case LoadoutEditResult.DeckFull: ShowMessage($"デッキは最大 {loadout.Loadout.MaxSize} 枚です"); break;
                case LoadoutEditResult.BelowMinimum: ShowMessage($"デッキは最低 {loadout.Loadout.MinSize} 枚必要です"); break;
                case LoadoutEditResult.NoCopiesLeft: ShowMessage("所持枚数をすべて使っています"); break;
                default: ShowMessage("操作できません"); break;
            }
        }

        void ShowMessage(string message)
        {
            messageText.text = message;
            messageAge = 0f;
            messageText.gameObject.SetActive(true);
        }

        static string PresetName(DeckDefinition deck)
        {
            switch (deck.deckId)
            {
                case "deck.npc_volt": return "雷特化（初期）";
                case "deck.npc_blaze": return "火・重力";
                case "deck.npc_guardian": return "支援";
                default: return deck.name;
            }
        }

        // ---------------------------------------------------------------- input

        void Update()
        {
            if (loadout == null)
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Gamepad pad = Gamepad.current;
            bool toggle = keyboard != null && keyboard.tabKey.wasPressedThisFrame;
            // Gamepad: pause (Start) first, then Y opens the editor; B / Start closes it.
            bool pausedByGame = Cursor.lockState != CursorLockMode.Locked && !InputGate.IsBlocked;
            if (!IsOpen && pad != null && pad.buttonNorth.wasPressedThisFrame && pausedByGame)
            {
                toggle = true;
            }

            if (!IsOpen)
            {
                if (toggle)
                {
                    Open();
                }

                return;
            }

            bool close = toggle || keyboard != null && keyboard.escapeKey.wasPressedThisFrame ||
                         pad != null && (pad.buttonEast.wasPressedThisFrame || pad.startButton.wasPressedThisFrame);
            if (close)
            {
                Close();
                return;
            }

            int columns = 3;
            if (Pressed(keyboard?.upArrowKey, keyboard?.wKey, pad?.dpad.up)) row--;
            if (Pressed(keyboard?.downArrowKey, keyboard?.sKey, pad?.dpad.down)) row++;
            if (Pressed(keyboard?.leftArrowKey, keyboard?.aKey, pad?.dpad.left)) { column = (column + columns - 1) % columns; row = 0; }
            if (Pressed(keyboard?.rightArrowKey, keyboard?.dKey, pad?.dpad.right)) { column = (column + 1) % columns; row = 0; }
            ClampSelection();

            if (Pressed(keyboard?.enterKey, keyboard?.spaceKey, pad?.buttonSouth))
            {
                Activate(column, row);
            }

            if (Pressed(keyboard?.rKey, null, pad?.buttonWest))
            {
                ResetDeck();
            }

            if (Pressed(keyboard?.digit1Key, null, pad?.buttonNorth))
            {
                ApplyPreset(PresetIndexAfterDefault(0));
            }

            if (Pressed(keyboard?.digit2Key, null, null))
            {
                ApplyPreset(PresetIndexAfterDefault(1));
            }

            HandleMouse();
            Refresh();
        }

        /// <summary>Presets list the starting deck first; keys 1 / 2 pick the others.</summary>
        int PresetIndexAfterDefault(int n)
        {
            int found = -1;
            for (int i = 0; i < loadout.Presets.Count; i++)
            {
                DeckDefinition preset = loadout.Presets[i];
                if (preset == null || preset == loadout.DeckController.StartingDeck)
                {
                    continue;
                }

                if (++found == n)
                {
                    return i;
                }
            }

            return -1;
        }

        static bool Pressed(UnityEngine.InputSystem.Controls.ButtonControl a, UnityEngine.InputSystem.Controls.ButtonControl b,
            UnityEngine.InputSystem.Controls.ButtonControl c) =>
            a != null && a.wasPressedThisFrame || b != null && b.wasPressedThisFrame || c != null && c.wasPressedThisFrame;

        void ClampSelection()
        {
            int count = column == 0 ? ownedList.Count : column == 1 ? loadout.Loadout.Count : buttons.Count;
            row = count == 0 ? 0 : (row % count + count) % count;
        }

        void Activate(int col, int index)
        {
            switch (col)
            {
                case 0: AddOwned(index); break;
                case 1: RemoveAt(index); break;
                default: PressButton(index); break;
            }

            ClampSelection();
        }

        void PressButton(int index)
        {
            switch (index)
            {
                case 0: ResetDeck(); break;
                case 1: ApplyPreset(PresetIndexAfterDefault(0)); break;
                case 2: ApplyPreset(PresetIndexAfterDefault(1)); break;
                default: Close(); break;
            }
        }

        void HandleMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 pointer = mouse.position.ReadValue();
            bool click = mouse.leftButton.wasPressedThisFrame;
            for (int i = 0; i < MaxRows; i++)
            {
                if (ownedRows[i].Rect.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(ownedRows[i].Rect, pointer, null))
                {
                    column = 0;
                    row = i;
                    if (click) Activate(0, i);
                    return;
                }

                if (deckRows[i].Rect.gameObject.activeSelf && RectTransformUtility.RectangleContainsScreenPoint(deckRows[i].Rect, pointer, null))
                {
                    column = 1;
                    row = i;
                    if (click) Activate(1, i);
                    return;
                }
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                if (RectTransformUtility.RectangleContainsScreenPoint(buttons[i].Rect, pointer, null))
                {
                    column = 2;
                    row = i;
                    if (click) Activate(2, i);
                    return;
                }
            }
        }

        // ---------------------------------------------------------------- view

        void Build()
        {
            root = HudKit.CreateCanvas(transform, "NPC Deck Editor Canvas", 30);
            Image back = HudKit.CreateImage("Window", root, new Color(0.05f, 0.04f, 0.12f, 0.94f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1240f, 760f));
            window = back.rectTransform;
            Vector2 top = new Vector2(0.5f, 1f);
            titleText = HudKit.CreateText("Title", window, 32, TextAnchor.MiddleCenter, top, new Vector2(0f, -36f), new Vector2(1200f, 44f));
            HudKit.AddOutline(titleText, 0.6f);
            stateText = HudKit.CreateText("State", window, 20, TextAnchor.MiddleCenter, top, new Vector2(0f, -72f), new Vector2(1200f, 26f));
            ownedHeader = HudKit.CreateText("OwnedHeader", window, 22, TextAnchor.MiddleLeft, top, new Vector2(-300f, -110f), new Vector2(560f, 28f));
            deckHeader = HudKit.CreateText("DeckHeader", window, 22, TextAnchor.MiddleLeft, top, new Vector2(300f, -110f), new Vector2(560f, 28f));
            for (int i = 0; i < MaxRows; i++)
            {
                ownedRows[i] = CreateRow(new Vector2(-300f, -146f - i * RowHeight), 560f);
                deckRows[i] = CreateRow(new Vector2(300f, -146f - i * RowHeight), 560f);
            }

            balanceText = HudKit.CreateText("Balance", window, 18, TextAnchor.MiddleLeft, top, new Vector2(-40f, -582f), new Vector2(1120f, 26f));
            balanceText.supportRichText = true;
            for (int i = 0; i < balanceBars.Length; i++)
            {
                var element = (Element)i;
                balanceBars[i] = HudKit.CreateImage($"Bar{i}", window, ElementStyle.ColorOf(element), new Vector2(0f, 1f), new Vector2(80f, -606f), new Vector2(0f, 10f));
                balanceBars[i].rectTransform.pivot = new Vector2(0f, 0.5f);
            }

            styleText = HudKit.CreateText("Style", window, 18, TextAnchor.MiddleLeft, top, new Vector2(-40f, -632f), new Vector2(1120f, 26f));
            styleText.color = new Color(1f, 0.92f, 0.6f);

            string[] labels = { "初期化（R / X）", "プリセット：火・重力（1 / Y）", "プリセット：支援（2）", "閉じる（Tab / B）" };
            for (int i = 0; i < labels.Length; i++)
            {
                Row button = CreateRow(new Vector2(-450f + i * 300f, -680f), 280f);
                button.Label.alignment = TextAnchor.MiddleCenter;
                button.Label.text = labels[i];
                buttons.Add(button);
            }

            messageText = HudKit.CreateText("Message", window, 20, TextAnchor.MiddleCenter, top, new Vector2(0f, -716f), new Vector2(1200f, 26f));
            messageText.color = new Color(1f, 0.85f, 0.4f);
            messageText.gameObject.SetActive(false);
            helpText = HudKit.CreateText("Help", window, 15, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(1200f, 22f));
            helpText.text = "↑↓ / 十字キー：選択　←→：列の切替　Enter / A：所持カードを追加・デッキのカードを外す　クリックでも操作できます　編集は戦闘していない時だけ反映";
            helpText.color = new Color(1f, 1f, 1f, 0.7f);
        }

        Row CreateRow(Vector2 position, float width)
        {
            Image back = HudKit.CreateImage("Row", window, new Color(1f, 1f, 1f, 0.06f), new Vector2(0.5f, 1f), position, new Vector2(width, RowHeight - 4f));
            Text label = HudKit.CreateText("Label", back.rectTransform, 18, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(8f, 0f), new Vector2(width - 16f, RowHeight - 4f));
            label.supportRichText = true;
            return new Row { Rect = back.rectTransform, Back = back, Label = label };
        }

        void Refresh()
        {
            DeckLoadout deck = loadout.Loadout;
            bool locked = loadout.IsLocked;
            string ally = npc != null ? npc.DisplayName : "NPC";
            titleText.text = $"NPC デッキ編集 — {ally}";
            stateText.text = locked ? "<color=#FF7070>戦闘中：カードの交換はできません（表示のみ）</color>" : "非戦闘中：編集するとすぐ NPC のデッキに反映されます";
            stateText.supportRichText = true;

            if (dirty)
            {
                dirty = false;
                ownedList.Clear();
                foreach (CardDefinition card in loadout.Collection.Definitions)
                {
                    ownedList.Add(card);
                }

                ownedHeader.text = $"所持カード（{ownedList.Count} 種）";
                deckHeader.text = $"デッキ　{deck.Count} 枚（{deck.MinSize}〜{deck.MaxSize} 枚・手札 {deck.HandSize} 枠）";
                for (int i = 0; i < MaxRows; i++)
                {
                    SetRow(ownedRows[i], i < ownedList.Count ? OwnedLine(ownedList[i], deck) : null);
                    SetRow(deckRows[i], i < deck.Count ? DeckLine(deck.Cards[i]) : null);
                }

                UpdateBalance(deck);
            }

            for (int i = 0; i < MaxRows; i++)
            {
                Highlight(ownedRows[i], column == 0 && row == i);
                Highlight(deckRows[i], column == 1 && row == i);
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                Highlight(buttons[i], column == 2 && row == i);
            }
        }

        static void SetRow(Row r, string text)
        {
            r.Rect.gameObject.SetActive(text != null);
            if (text != null)
            {
                r.Label.text = text;
            }
        }

        static void Highlight(Row r, bool selected)
        {
            r.Back.color = selected ? new Color(1f, 0.85f, 0.35f, 0.32f) : new Color(1f, 1f, 1f, 0.06f);
        }

        static string Hex(Element element) => ColorUtility.ToHtmlStringRGB(ElementStyle.ColorOf(element));

        string OwnedLine(CardDefinition card, DeckLoadout deck)
        {
            int inDeck = deck.CopiesInDeck(card);
            int owned = loadout.Collection.CountOf(card);
            return $"<color=#{Hex(card.element)}>[{ElementStyle.DisplayName(card.element)}]</color> {card.displayName}　所持 {owned}・使用 {inDeck}　<color=#9EE6FF>＋追加</color>";
        }

        static string DeckLine(CardDefinition card) =>
            $"<color=#{Hex(card.element)}>[{ElementStyle.DisplayName(card.element)}]</color> {card.displayName}　消費 {card.gaugeCost:0}　<color=#FF9E9E>－外す</color>";

        void UpdateBalance(DeckLoadout deck)
        {
            int[] counts = deck.ElementCounts();
            int total = Mathf.Max(1, deck.Count);
            builder.Clear();
            builder.Append("属性の偏り：");
            float x = 80f;
            Element[] order = { Element.Fire, Element.Ice, Element.Lightning, Element.Gravity, Element.None };
            foreach (Element element in order)
            {
                int n = counts[(int)element];
                builder.Append("<color=#").Append(Hex(element)).Append('>').Append(ElementStyle.DisplayName(element)).Append(' ')
                    .Append(n).Append("枚(").Append(Mathf.RoundToInt(100f * n / total)).Append("%)</color>　");
                Image bar = balanceBars[(int)element];
                float width = 1080f * n / total;
                bar.rectTransform.anchoredPosition = new Vector2(x, -606f);
                bar.rectTransform.sizeDelta = new Vector2(width, 10f);
                x += width;
            }

            balanceText.text = builder.ToString();
            styleText.text = "戦い方の傾向：" + StyleHint(counts, deck);
        }

        /// <summary>What the NPC will tend to do with this deck (the AI reads the same cards).</summary>
        static string StyleHint(int[] counts, DeckLoadout deck)
        {
            int support = 0;
            foreach (CardDefinition card in deck.Cards)
            {
                if (card.HasEffect(SkillEffectKind.Heal) || card.HasEffect(SkillEffectKind.Shield))
                {
                    support++;
                }
            }

            Element dominant = deck.DominantElement();
            string lead;
            switch (dominant)
            {
                case Element.Lightning: lead = "雷で追撃役（凍結・炎上した敵に超伝導 / オーバーロード）"; break;
                case Element.Fire: lead = "火で起点役（雷・重力の味方がオーバーロード / 灼熱特異点）"; break;
                case Element.Ice: lead = "氷で足止めと起点（雷の味方が超伝導）"; break;
                case Element.Gravity: lead = "重力で引き寄せ（火の味方が灼熱特異点）"; break;
                default: lead = "属性なし中心"; break;
            }

            if (support >= 3)
            {
                lead += "・回復 / シールド多めの支援型";
            }

            return lead;
        }
    }
}
