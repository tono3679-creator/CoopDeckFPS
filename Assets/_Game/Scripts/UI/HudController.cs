using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Combat HUD built in code (Docs/UI_DESIGN.md §5): dynamic crosshair, hit marker, damage
    /// numbers, the vitals panel (bottom left), the weapon panel (bottom right), the combat
    /// notification feed (top centre), the F1 controls guide, the pause screen with the full
    /// guide, and the F3 performance overlay. The card hand lives in <see cref="DeckHud"/>.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        const int DamageNumberPoolSize = 24;

        [SerializeField] FpsCharacter player;
        [SerializeField] FrameTimeMonitor frameMonitor;
        [SerializeField] bool showPerformanceOverlay = true;
        [SerializeField] bool showControlsHint = true;
        [Tooltip("Fallback element for the vitals mark when the player has no PlayerCharacterSwap (the character definition wins).")]
        [SerializeField] Element characterElement = Element.Fire;

        static readonly Color CrosshairColor = UiTokens.WithAlpha(UiTokens.TextMain, 0.92f);
        static readonly Color HitColor = UiTokens.TextMain;
        static readonly Color HeadshotColor = UiTokens.Accent;
        static readonly Color KillColor = UiTokens.Danger;
        static readonly Color SkillHitColor = Palette.Hex(0xC9B6FF);
        static readonly Color CriticalColor = Palette.Hex(0xFF9A3C);
        static readonly Color ComboColor = Palette.Hex(0xFF8FE0);

        RectTransform root;
        Camera viewCamera;

        readonly RectTransform[] crosshairLines = new RectTransform[4];
        float crosshairGap = 8f;
        readonly Image[] hitMarkerLines = new Image[4];
        float hitMarkerTimer;
        float hitMarkerScale = 1f;
        Color hitMarkerColor = Color.white;

        VitalsPanel vitals;
        WeaponPanel weapon;
        NotificationFeedView feed;
        GuidePanel compactGuide;
        GuidePanel fullGuide;
        RectTransform pauseOverlay;
        Text guideChipText;
        Text perfText;
        RectTransform perfPanel;
        InputScheme chipScheme = (InputScheme)(-1);

        DamageNumber[] damageNumbers;
        int nextDamageNumber;
        bool guideOpen;
        Revivable playerRevivable;
        RectTransform gallery;

        class DamageNumber
        {
            public Text Text;
            public RectTransform Rect;
            public Vector3 World;
            public float Age;
            public float Life;
            public float Rise;
            public bool Active;
        }

        /// <summary>Newest combat notice shown (tests, debugging).</summary>
        public string NewestNotice => feed != null ? feed.Newest : "";

        /// <summary>The element on the vitals mark (from the character in use).</summary>
        public Element ShownElement { get; private set; }

        /// <summary>The element of the character a player object uses, or <paramref name="fallback"/> without a character definition.</summary>
        public static Element CharacterElement(GameObject playerObject, Element fallback)
        {
            CoopDeck.Characters.PlayerCharacterSwap swap = playerObject != null ? playerObject.GetComponent<CoopDeck.Characters.PlayerCharacterSwap>() : null;
            CoopDeck.Characters.CharacterDefinition definition = swap != null ? swap.Current : null;
            return definition != null ? definition.Element : fallback;
        }

        void Awake()
        {
            BuildCanvas();
        }

        void OnEnable()
        {
            CombatEvents.DamageApplied += OnDamageApplied;
        }

        void OnDisable()
        {
            CombatEvents.DamageApplied -= OnDamageApplied;
            if (playerRevivable != null)
            {
                playerRevivable.RevivedByAlly -= OnPlayerRevived;
            }
        }

        void OnDestroy()
        {
            feed?.Dispose();
        }

        void Start()
        {
            if (player == null)
            {
                player = FpsCharacter.FindLocalHuman();
            }

            if (frameMonitor == null)
            {
                frameMonitor = GetComponent<FrameTimeMonitor>();
            }

            // The character in use (fox / rabbit) decides the element mark; it is chosen in Awake.
            ShownElement = CharacterElement(player != null ? player.gameObject : null, characterElement);
            vitals.SetElement(ShownElement);

            playerRevivable = player != null ? player.GetComponent<Revivable>() : null;
            if (playerRevivable != null)
            {
                playerRevivable.RevivedByAlly += OnPlayerRevived;
            }
        }

        void OnPlayerRevived(Revivable revivable, ICombatActor reviver)
        {
            string name = reviver != null && !(reviver is Object o && o == null) ? reviver.DisplayName : "味方";
            HudNotifications.Post(HudNoticeKind.Revive, $"{name} があなたを蘇生しました");
        }

        // ---------------------------------------------------------------- build

        void BuildCanvas()
        {
            root = HudKit.CreateCanvas(transform, "HUD Canvas", 10);

            BuildCrosshair();
            BuildHitMarker();
            vitals = VitalsPanel.Create(root, characterElement, "あなた");
            ShownElement = characterElement;
            weapon = WeaponPanel.Create(root);
            feed = NotificationFeedView.Create(root);
            BuildPerf();
            BuildGuide();
            BuildPause();

            damageNumbers = new DamageNumber[DamageNumberPoolSize];
            for (int i = 0; i < damageNumbers.Length; i++)
            {
                Text t = HudKit.CreateText("DamageNumber", root, 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 50f));
                t.fontStyle = FontStyle.Bold;
                HudKit.AddOutline(t, 0.75f);
                t.gameObject.SetActive(false);
                damageNumbers[i] = new DamageNumber { Text = t, Rect = t.rectTransform };
            }
        }

        void BuildCrosshair()
        {
            Image dot = HudKit.CreateImage("CrosshairDot", root, CrosshairColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f));
            HudKit.AddOutline(dot, 0.45f);
            for (int i = 0; i < 4; i++)
            {
                bool vertical = i % 2 == 0;
                Image line = HudKit.CreateImage("CrosshairLine", root, CrosshairColor, new Vector2(0.5f, 0.5f), Vector2.zero,
                    vertical ? new Vector2(3f, 12f) : new Vector2(12f, 3f));
                HudKit.AddOutline(line, 0.5f);
                crosshairLines[i] = line.rectTransform;
            }
        }

        void BuildHitMarker()
        {
            for (int i = 0; i < 4; i++)
            {
                Image line = HudKit.CreateImage("HitMarker", root, new Color(1f, 1f, 1f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 14f));
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i);
                hitMarkerLines[i] = line;
            }
        }

        void BuildPerf()
        {
            Rect layout = HudLayout.Perf;
            perfPanel = HudKit.CreateRect("Perf", root, Vector2.zero, Vector2.zero, Vector2.zero, layout.size);
            HudLayout.Place(perfPanel, layout);
            perfText = UiWidgets.Label("Text", perfPanel, 14, UiTokens.TextSub, TextAnchor.UpperLeft, new Vector2(0.5f, 0.5f), Vector2.zero, layout.size, false, false);
            HudKit.AddOutline(perfText, 0.85f);
            perfPanel.gameObject.SetActive(showPerformanceOverlay);
        }

        void BuildGuide()
        {
            Rect layout = HudLayout.GuideChip;
            RectTransform chip = HudKit.CreateRect("GuideChip", root, Vector2.zero, Vector2.zero, Vector2.zero, layout.size);
            HudLayout.Place(chip, layout);
            guideChipText = UiWidgets.Label("Text", chip, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f),
                Vector2.zero, layout.size, false, false);
            HudKit.AddOutline(guideChipText, 0.85f);
            guideChipText.supportRichText = true;
            chip.gameObject.SetActive(showControlsHint);
            compactGuide = GuidePanel.CreateCompact(root);
            compactGuide.Root.gameObject.SetActive(false);
        }

        void BuildPause()
        {
            // Own canvas above the hand / ally HUD (11–12), below the NPC deck editor (30) and the UI preview (40).
            RectTransform full = HudKit.CreateCanvas(transform, "Pause Canvas", 25, false);
            pauseOverlay = full;
            Image dim = HudKit.CreateImage("Dim", full, UiTokens.WithAlpha(UiTokens.Background, 0.72f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            HudKit.Stretch(dim.rectTransform);

            RectTransform frame = HudKit.CreateSafeFrame(full);
            var size = new Vector2(1060f, 600f);
            fullGuide = GuidePanel.CreateFull(frame, size);
            RectTransform title = HudKit.CreateRect("Title", frame, new Vector2(0.5f, 0.5f), new Vector2(0f, size.y * 0.5f + 20f), new Vector2(size.x, 90f));
            LampView lamp = UiWidgets.Lamp("Lamp", title, new Vector2(0.5f, 0.5f), new Vector2(-170f, 22f), new Vector2(34f, 14f));
            lamp.Set(true);
            Text heading = UiWidgets.Label("Heading", title, UiTokens.Display, UiTokens.TextMain, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(20f, 22f), new Vector2(400f, 56f), true);
            heading.text = "一時停止中";
            Text resume = UiWidgets.Label("Resume", title, UiTokens.Body, UiTokens.TextSub, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -22f), new Vector2(size.x, 28f));
            resume.text = "クリック / Esc / Start で再開　　パッド Y：NPC デッキ編集";
            full.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- events

        void OnDamageApplied(DamageEvent e)
        {
            if (player == null || !ReferenceEquals(e.Info.Source, player))
            {
                return;
            }

            bool killed = e.Result.Killed;
            bool headshot = e.Result.IsHeadshot;
            bool critical = (e.Result.Flags & DamageFlags.Critical) != 0;
            bool combo = e.Info.Kind == DamageKind.Combo;
            bool skill = e.Info.Kind == DamageKind.Skill || e.Info.Kind == DamageKind.Status || combo;
            if ((e.Info.Flags & DamageFlags.WeaponProc) != 0)
            {
                // A weapon ability's discharge (帯電連射): big number in its element colour, no hit marker.
                SpawnDamageNumber(e.Info.Point, e.Result.Damage, true, killed, true, false, Color.Lerp(UiTokens.ElementColor(e.Info.Element), Color.white, 0.2f));
                return;
            }

            if (combo)
            {
                SpawnDamageNumber(e.Info.Point, e.Result.Damage, true, killed, true, false, ComboColor);
                return;
            }

            if (e.Info.Kind != DamageKind.Status)
            {
                hitMarkerTimer = 0.14f;
                hitMarkerScale = killed ? 1.6f : headshot || critical ? 1.3f : 1f;
                hitMarkerColor = killed ? KillColor : headshot ? HeadshotColor : critical ? CriticalColor : HitColor;
            }

            SpawnDamageNumber(e.Info.Point, e.Result.Damage, headshot || critical, killed, skill, e.Info.Kind == DamageKind.Status,
                critical ? CriticalColor : (Color?)null);
        }

        void SpawnDamageNumber(Vector3 world, float amount, bool headshot, bool killed, bool skill, bool overTime, Color? colorOverride = null)
        {
            if (amount < 0.5f)
            {
                return;
            }

            DamageNumber n = damageNumbers[nextDamageNumber];
            nextDamageNumber = (nextDamageNumber + 1) % damageNumbers.Length;
            n.World = world + Random.insideUnitSphere * 0.15f;
            n.Age = 0f;
            n.Life = killed ? 0.9f : 0.65f;
            n.Rise = killed ? 70f : 50f;
            n.Active = true;
            n.Text.text = Mathf.RoundToInt(amount).ToString();
            n.Text.fontSize = headshot || skill && !overTime ? 38 : overTime ? 24 : 30;
            n.Text.color = killed ? KillColor : colorOverride ?? (headshot ? HeadshotColor : skill ? SkillHitColor : HitColor);
            n.Text.gameObject.SetActive(true);
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            InputGlyphs.Poll();
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame ||
                Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame)
            {
                showPerformanceOverlay = !showPerformanceOverlay;
                perfPanel.gameObject.SetActive(showPerformanceOverlay);
            }

            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame && showControlsHint)
            {
                guideOpen = !guideOpen;
                compactGuide.Root.gameObject.SetActive(guideOpen);
            }

            if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame && UiStyleGallery.Available)
            {
                ToggleGallery();
            }
        }

        /// <summary>F8: the read-only UI preview sheet (Editor / development builds).</summary>
        public void ToggleGallery()
        {
            if (gallery == null)
            {
                var samples = new System.Collections.Generic.List<CoopDeck.Cards.CardDefinition>();
                CoopDeck.Skills.DeckController deck = player != null ? player.GetComponent<CoopDeck.Skills.DeckController>() : null;
                if (deck != null && deck.Deck != null)
                {
                    foreach (CoopDeck.Deck.CardRuntime card in deck.Deck.AllCards())
                    {
                        if (card.IsActive && !samples.Contains(card.Definition) && samples.Count < 5)
                        {
                            samples.Add(card.Definition);
                        }
                    }
                }

                gallery = UiStyleGallery.Build(transform, samples);
                return;
            }

            gallery.gameObject.SetActive(!gallery.gameObject.activeSelf);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            if (viewCamera == null && player != null && player.Look != null)
            {
                viewCamera = player.Look.ViewCamera;
            }

            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            UpdateCrosshair(dt);
            UpdateHitMarker(dt);
            vitals.Update(player, dt);
            weapon.Update(player, dt);
            feed.Tick(dt);
            UpdateDamageNumbers(dt);
            UpdateGuide();

            bool paused = Cursor.lockState != CursorLockMode.Locked && !InputGate.IsBlocked;
            if (pauseOverlay.gameObject.activeSelf != paused)
            {
                pauseOverlay.gameObject.SetActive(paused);
            }

            if (paused)
            {
                fullGuide.Refresh();
            }

            if (showPerformanceOverlay)
            {
                UpdatePerfText();
            }
        }

        void UpdateGuide()
        {
            if (chipScheme != InputGlyphs.Active)
            {
                chipScheme = InputGlyphs.Active;
                guideChipText.text = chipScheme == InputScheme.Gamepad
                    ? "<b>Start</b>  操作ガイド（ポーズ）"
                    : "<b>F1</b>  操作ガイド";
            }

            if (guideOpen)
            {
                compactGuide.Refresh();
            }
        }

        void UpdateCrosshair(float dt)
        {
            float spread = player != null && player.Weapon != null ? player.Weapon.CurrentSpread : 0.5f;
            float fov = viewCamera != null ? viewCamera.fieldOfView : 60f;
            float canvasPerScreenPixel = root.rect.height / Mathf.Max(1f, Screen.height);
            float pixelsPerDegree = Screen.height / Mathf.Max(1f, fov) * canvasPerScreenPixel;
            float targetGap = 6f + spread * pixelsPerDegree;
            crosshairGap = Mathf.Lerp(crosshairGap, targetGap, 1f - Mathf.Exp(-25f * dt));

            float offset = crosshairGap + 6f;
            crosshairLines[0].anchoredPosition = new Vector2(0f, offset);
            crosshairLines[1].anchoredPosition = new Vector2(offset, 0f);
            crosshairLines[2].anchoredPosition = new Vector2(0f, -offset);
            crosshairLines[3].anchoredPosition = new Vector2(-offset, 0f);
        }

        void UpdateHitMarker(float dt)
        {
            hitMarkerTimer -= dt;
            float alpha = Mathf.Clamp01(hitMarkerTimer / 0.14f);
            for (int i = 0; i < hitMarkerLines.Length; i++)
            {
                Image line = hitMarkerLines[i];
                Color c = hitMarkerColor;
                c.a = alpha;
                line.color = c;
                float angle = (45f + 90f * i) * Mathf.Deg2Rad;
                float distance = (14f + (1f - alpha) * 6f) * hitMarkerScale;
                line.rectTransform.anchoredPosition = new Vector2(-Mathf.Sin(angle), Mathf.Cos(angle)) * distance;
                line.rectTransform.sizeDelta = new Vector2(3f, 12f * hitMarkerScale);
            }
        }

        void UpdateDamageNumbers(float dt)
        {
            foreach (DamageNumber n in damageNumbers)
            {
                if (!n.Active)
                {
                    continue;
                }

                n.Age += dt;
                if (n.Age >= n.Life || viewCamera == null)
                {
                    n.Active = false;
                    n.Text.gameObject.SetActive(false);
                    continue;
                }

                Vector3 screen = viewCamera.WorldToScreenPoint(n.World);
                if (screen.z <= 0f)
                {
                    n.Text.enabled = false;
                    continue;
                }

                n.Text.enabled = true;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out Vector2 local);
                float t = n.Age / n.Life;
                n.Rect.anchoredPosition = local + new Vector2(0f, n.Rise * Mathf.Sqrt(t));
                float pop = t < 0.15f ? Mathf.Lerp(1.4f, 1f, t / 0.15f) : 1f;
                n.Rect.localScale = Vector3.one * pop;
                Color c = n.Text.color;
                c.a = t < 0.6f ? 1f : Mathf.InverseLerp(1f, 0.6f, t);
                n.Text.color = c;
            }
        }

        void UpdatePerfText()
        {
            string quality = QualitySettings.names.Length > 0 ? QualitySettings.names[QualitySettings.GetQualityLevel()] : "?";
            if (frameMonitor == null)
            {
                perfText.text = $"Tier {PerformanceSettings.Tier} / Quality {quality}";
                return;
            }

            perfText.text =
                $"FPS {frameMonitor.AverageFps:0}  (1% low {frameMonitor.OnePercentLowFps:0})\n" +
                $"{frameMonitor.AverageMs:0.0} ms  (worst {frameMonitor.WorstMs:0.0})\n" +
                $"Tier {PerformanceSettings.Tier} / {quality} / VFX {PerformanceSettings.Vfx}{(PerformanceBootstrap.Uncapped ? " / uncapped" : "")}\n" +
                $"{Screen.width}x{Screen.height}  vSync {QualitySettings.vSyncCount}";
        }
    }
}
