using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// The controls guide. Full (both devices) on the pause screen; compact (the device in use)
    /// at the left edge when toggled with F1 during play, so the combat view starts clean.
    /// Labels mirror GameInputActions' default bindings.
    /// </summary>
    public sealed class GuidePanel
    {
        static readonly string[,] Rows =
        {
            { "移動", "WASD", "左スティック" },
            { "視点", "マウス", "右スティック" },
            { "射撃", "左クリック", "RT" },
            { "リロード", "R", "X" },
            { "回避", "Ctrl", "B" },
            { "ジャンプ", "Space", "A" },
            { "走る", "Shift", "L3" },
            { "スキル", "Q・E・F", "LB・RB・Y" },
            { "カード選択（アイテム対象）", "1・2・3", "十字←→" },
            { "アイテム使用 / 切替", "G / T", "十字↑ / 十字↓" },
            { "NPC 方針切替", "V", "R3" },
            { "NPC 集中攻撃", "中クリック・X", "LT" },
            { "NPC デッキ編集", "Tab", "ポーズ中 Y" },
            { "性能表示", "F3", "View" },
            { "操作ガイド", "F1", "Start（ポーズ）" },
        };

        const string Footer =
            "コンボ（4 秒以内に同じ敵へ）：氷＋雷 / 火＋重力 / 火＋雷　　ダウンした味方の近くに立つと蘇生\n" +
            "開発用：F4 エフェクト品質 / F6 コンボ段階 / F7 練習デッキ / F8 UI プレビュー";

        const float RowHeight = 30f;

        public RectTransform Root;
        Text keyboardHeader;
        Text padHeader;
        Text[] keyboardCells;
        Text[] padCells;
        bool full;
        InputScheme shown = (InputScheme)(-1);

        public static int RowCount => Rows.GetLength(0);

        /// <summary>Full guide: centred, both device columns and the footer.</summary>
        public static GuidePanel CreateFull(Transform parent, Vector2 size)
        {
            var view = new GuidePanel { full = true };
            PanelView panel = UiWidgets.Panel("Guide", parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), size);
            view.Root = panel.Root;
            view.BuildTable(new Vector2(40f, -28f), new[] { 380f, 300f, 300f });
            Text footer = UiWidgets.Label("Footer", view.Root, UiTokens.Caption, UiTokens.TextSub, TextAnchor.UpperLeft, new Vector2(0f, 0f),
                new Vector2(40f + (size.x - 80f) * 0.5f, 52f), new Vector2(size.x - 80f, 56f));
            footer.text = Footer;
            return view;
        }

        /// <summary>Compact guide: the active device only, at the left edge (F1).</summary>
        public static GuidePanel CreateCompact(Transform parent)
        {
            var view = new GuidePanel { full = false };
            float height = 56f + RowCount * RowHeight;
            PanelView panel = UiWidgets.Panel("GuideCompact", parent, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(UiTokens.Margin, 90f),
                new Vector2(470f, height));
            view.Root = panel.Root;
            view.BuildTable(new Vector2(22f, -18f), new[] { 270f, 170f });
            return view;
        }

        void BuildTable(Vector2 origin, float[] widths)
        {
            var topLeft = new Vector2(0f, 1f);
            float x = origin.x;
            Text actionHeader = Header("操作", x, origin.y, widths[0]);
            actionHeader.color = UiTokens.TextSub;
            x += widths[0];
            keyboardHeader = Header("キーボード・マウス", x, origin.y, widths[1]);
            float padX = full ? x + widths[1] : x;
            if (full)
            {
                padHeader = Header("コントローラー", padX, origin.y, widths[2]);
            }

            int count = RowCount;
            keyboardCells = new Text[count];
            padCells = new Text[count];
            for (int i = 0; i < count; i++)
            {
                float y = origin.y - 38f - i * RowHeight;
                if (i % 2 == 0)
                {
                    Image band = HudKit.CreateImage("Band", Root, UiTokens.WithAlpha(UiTokens.Energy, 0.04f), topLeft, Vector2.zero,
                        new Vector2(widths[0] + widths[1] + (full ? widths[2] : 0f), RowHeight));
                    band.rectTransform.pivot = new Vector2(0f, 0.5f);
                    band.rectTransform.anchoredPosition = new Vector2(origin.x - 8f, y);
                }

                Cell(Rows[i, 0], origin.x, y, widths[0], UiTokens.TextSub, false);
                keyboardCells[i] = Cell(Rows[i, 1], origin.x + widths[0], y, widths[1], UiTokens.TextMain, true);
                padCells[i] = Cell(Rows[i, 2], padX, y, full ? widths[2] : widths[1], UiTokens.TextMain, true);
            }

            Refresh();
        }

        Text Header(string text, float x, float y, float width)
        {
            Text label = UiWidgets.Label("Header", Root, UiTokens.Body, UiTokens.Accent, TextAnchor.MiddleLeft, new Vector2(0f, 1f),
                new Vector2(x + width * 0.5f, y), new Vector2(width, 28f), true);
            label.text = text;
            return label;
        }

        Text Cell(string text, float x, float y, float width, Color color, bool bold)
        {
            Text label = UiWidgets.Label("Cell", Root, UiTokens.Body, color, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(x + width * 0.5f, y),
                new Vector2(width, RowHeight), bold);
            label.text = text;
            return label;
        }

        /// <summary>Highlights the device in use (full) or switches the compact column to it.</summary>
        public void Refresh()
        {
            if (shown == InputGlyphs.Active)
            {
                return;
            }

            shown = InputGlyphs.Active;
            bool pad = shown == InputScheme.Gamepad;
            if (full)
            {
                keyboardHeader.color = pad ? UiTokens.TextSub : UiTokens.Accent;
                padHeader.color = pad ? UiTokens.Accent : UiTokens.TextSub;
                for (int i = 0; i < keyboardCells.Length; i++)
                {
                    keyboardCells[i].color = pad ? UiTokens.TextSub : UiTokens.TextMain;
                    padCells[i].color = pad ? UiTokens.TextMain : UiTokens.TextSub;
                }

                return;
            }

            keyboardHeader.text = pad ? "コントローラー" : "キーボード・マウス";
            for (int i = 0; i < keyboardCells.Length; i++)
            {
                keyboardCells[i].enabled = !pad;
                padCells[i].enabled = pad;
            }
        }
    }
}
