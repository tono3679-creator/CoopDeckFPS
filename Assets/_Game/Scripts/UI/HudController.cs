using System;
using CoopDeck.Combat;
using CoopDeck.Core;
using CoopDeck.Player;
using CoopDeck.Weapons;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Phase 1 HUD, built in code so it needs no prefab: dynamic crosshair, hit marker, damage
    /// numbers, ammo/reload, HP, dash charges, skill-slot placeholders, pause notice and an
    /// F3 performance overlay. Phase 5 replaces the look; the data hooks stay.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        const int DamageNumberPoolSize = 24;
        static readonly string[] SkillKeyLabels = { "Q", "E", "F" };

        [SerializeField] FpsCharacter player;
        [SerializeField] FrameTimeMonitor frameMonitor;
        [SerializeField] bool showPerformanceOverlay = true;
        [SerializeField] bool showControlsHint = true;

        static readonly Color CrosshairColor = new Color(1f, 1f, 1f, 0.92f);
        static readonly Color HitColor = new Color(1f, 1f, 1f, 1f);
        static readonly Color HeadshotColor = new Color(1f, 0.85f, 0.2f, 1f);
        static readonly Color KillColor = new Color(1f, 0.25f, 0.3f, 1f);
        static readonly Color PanelColor = new Color(0.08f, 0.06f, 0.16f, 0.65f);

        Font font;
        RectTransform root;
        Camera viewCamera;

        readonly RectTransform[] crosshairLines = new RectTransform[4];
        float crosshairGap = 8f;
        readonly Image[] hitMarkerLines = new Image[4];
        float hitMarkerTimer;
        float hitMarkerScale = 1f;
        Color hitMarkerColor = Color.white;

        Text ammoText;
        Text ammoMaxText;
        Text reloadLabel;
        Image reloadFill;
        Image hpFill;
        Text hpText;
        readonly Image[] dashPips = new Image[3];
        readonly Image[] skillSlots = new Image[CharacterCommands.SkillSlotCount];
        readonly float[] skillFlash = new float[CharacterCommands.SkillSlotCount];
        Text toastText;
        float toastTimer;
        Text perfText;
        Text pausedText;
        Text hintText;

        DamageNumber[] damageNumbers;
        int nextDamageNumber;

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

        void Awake()
        {
            font = CreateFont();
            BuildCanvas();
        }

        void OnEnable()
        {
            CombatEvents.DamageApplied += OnDamageApplied;
            BindPlayer();
        }

        void OnDisable()
        {
            CombatEvents.DamageApplied -= OnDamageApplied;
            UnbindPlayer();
        }

        void Start()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<FpsCharacter>();
                BindPlayer();
            }

            if (frameMonitor == null)
            {
                frameMonitor = GetComponent<FrameTimeMonitor>();
            }
        }

        bool bound;

        void BindPlayer()
        {
            if (bound || player == null)
            {
                return;
            }

            player.SkillSlotRequested += OnSkillRequested;
            player.ItemUseRequested += OnItemUse;
            player.ItemCycleRequested += OnItemCycle;
            bound = true;
        }

        void UnbindPlayer()
        {
            if (!bound || player == null)
            {
                return;
            }

            player.SkillSlotRequested -= OnSkillRequested;
            player.ItemUseRequested -= OnItemUse;
            player.ItemCycleRequested -= OnItemCycle;
            bound = false;
        }

        static Font CreateFont()
        {
            // OS fonts with Japanese glyphs first (Windows), then Unity's built-in font.
            try
            {
                Font osFont = Font.CreateDynamicFontFromOSFont(new[] { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic", "Arial" }, 32);
                if (osFont != null)
                {
                    return osFont;
                }
            }
            catch (Exception)
            {
                // Fall through to the built-in font.
            }

            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        // ---------------------------------------------------------------- build

        void BuildCanvas()
        {
            var canvasGo = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            root = (RectTransform)canvasGo.transform;

            BuildCrosshair();
            BuildHitMarker();
            BuildAmmo();
            BuildHealth();
            BuildSkillSlots();

            toastText = CreateText("Toast", root, 26, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 250f), new Vector2(900f, 40f));
            toastText.color = new Color(1f, 1f, 1f, 0f);

            perfText = CreateText("Perf", root, 20, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(170f, -80f), new Vector2(320f, 130f));
            perfText.gameObject.SetActive(showPerformanceOverlay);

            pausedText = CreateText("Paused", root, 40, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(1200f, 120f));
            pausedText.text = "一時停止中\nクリック / Esc / Start で再開";
            pausedText.gameObject.SetActive(false);

            hintText = CreateText("Hint", root, 18, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(1700f, 60f));
            hintText.text = "WASD 移動 / Shift 走る / Ctrl 回避 / Space ジャンプ / 左クリック 射撃 / R リロード / Q・E・F スキル枠 / G アイテム / Esc 一時停止 / F3 性能表示\n" +
                            "ゲームパッド: 左スティック 移動 / 右スティック 視点 / RT 射撃 / A ジャンプ / B 回避 / X リロード / L3 走る / LB・RB・Y スキル枠";
            hintText.color = new Color(1f, 1f, 1f, 0.75f);
            hintText.gameObject.SetActive(showControlsHint);

            damageNumbers = new DamageNumber[DamageNumberPoolSize];
            for (int i = 0; i < damageNumbers.Length; i++)
            {
                Text t = CreateText("DamageNumber", root, 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 50f));
                t.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.6f);
                t.gameObject.SetActive(false);
                damageNumbers[i] = new DamageNumber { Text = t, Rect = t.rectTransform };
            }
        }

        void BuildCrosshair()
        {
            Image dot = CreateImage("CrosshairDot", root, CrosshairColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f));
            dot.raycastTarget = false;
            for (int i = 0; i < 4; i++)
            {
                bool vertical = i % 2 == 0;
                Image line = CreateImage("CrosshairLine", root, CrosshairColor, new Vector2(0.5f, 0.5f), Vector2.zero,
                    vertical ? new Vector2(3f, 12f) : new Vector2(12f, 3f));
                line.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.5f);
                crosshairLines[i] = line.rectTransform;
            }
        }

        void BuildHitMarker()
        {
            for (int i = 0; i < 4; i++)
            {
                Image line = CreateImage("HitMarker", root, new Color(1f, 1f, 1f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(3f, 14f));
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f + 90f * i);
                hitMarkerLines[i] = line;
            }
        }

        void BuildAmmo()
        {
            Image panel = CreateImage("AmmoPanel", root, PanelColor, new Vector2(1f, 0f), new Vector2(-170f, 90f), new Vector2(260f, 110f));
            // Count is right-aligned ending at x=+20, "/ 30" starts at x=+30: they can never overlap.
            ammoText = CreateText("Ammo", panel.rectTransform, 56, TextAnchor.MiddleRight, new Vector2(0.5f, 0.5f), new Vector2(-60f, 12f), new Vector2(160f, 70f));
            ammoMaxText = CreateText("AmmoMax", panel.rectTransform, 26, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(75f, 4f), new Vector2(90f, 40f));
            reloadLabel = CreateText("Reload", panel.rectTransform, 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(240f, 24f));
            Image reloadBack = CreateImage("ReloadBack", panel.rectTransform, new Color(1f, 1f, 1f, 0.15f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(220f, 6f));
            reloadFill = CreateImage("ReloadFill", reloadBack.rectTransform, new Color(0.45f, 0.95f, 1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(220f, 6f));
            reloadFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

        void BuildHealth()
        {
            Image panel = CreateImage("HealthPanel", root, PanelColor, new Vector2(0f, 0f), new Vector2(210f, 80f), new Vector2(360f, 90f));
            Image back = CreateImage("HpBack", panel.rectTransform, new Color(1f, 1f, 1f, 0.12f), new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(320f, 22f));
            hpFill = CreateImage("HpFill", back.rectTransform, new Color(0.35f, 1f, 0.55f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(320f, 22f));
            hpFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            hpText = CreateText("HpText", panel.rectTransform, 20, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(180f, 10f), new Vector2(320f, 26f));

            for (int i = 0; i < dashPips.Length; i++)
            {
                dashPips[i] = CreateImage("DashPip", panel.rectTransform, Color.white, new Vector2(0f, 0f), new Vector2(34f + i * 30f, 18f), new Vector2(24f, 8f));
            }

            Text dashLabel = CreateText("DashLabel", panel.rectTransform, 14, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(170f, 18f), new Vector2(120f, 20f));
            dashLabel.text = "回避";
        }

        void BuildSkillSlots()
        {
            for (int i = 0; i < skillSlots.Length; i++)
            {
                float x = (i - 1) * 120f;
                Image slot = CreateImage("SkillSlot", root, PanelColor, new Vector2(0.5f, 0f), new Vector2(x, 90f), new Vector2(104f, 104f));
                Text key = CreateText("Key", slot.rectTransform, 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(100f, 50f));
                key.text = SkillKeyLabels[i];
                Text label = CreateText("Label", slot.rectTransform, 14, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(100f, 20f));
                label.text = "スキル枠";
                label.color = new Color(1f, 1f, 1f, 0.6f);
                skillSlots[i] = slot;
            }
        }

        Text CreateText(string name, Transform parent, int size, TextAnchor anchor, Vector2 anchorPoint, Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchorPoint;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            var text = go.GetComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.alignment = anchor;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        static Image CreateImage(string name, Transform parent, Color color, Vector2 anchorPoint, Vector2 position, Vector2 dimensions)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = anchorPoint;
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
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
            hitMarkerTimer = 0.14f;
            hitMarkerScale = killed ? 1.6f : headshot ? 1.3f : 1f;
            hitMarkerColor = killed ? KillColor : headshot ? HeadshotColor : HitColor;

            SpawnDamageNumber(e.Info.Point, e.Result.Damage, headshot, killed);
        }

        void SpawnDamageNumber(Vector3 world, float amount, bool headshot, bool killed)
        {
            DamageNumber n = damageNumbers[nextDamageNumber];
            nextDamageNumber = (nextDamageNumber + 1) % damageNumbers.Length;
            n.World = world + UnityEngine.Random.insideUnitSphere * 0.15f;
            n.Age = 0f;
            n.Life = killed ? 0.9f : 0.65f;
            n.Rise = killed ? 70f : 50f;
            n.Active = true;
            n.Text.text = Mathf.RoundToInt(amount).ToString();
            n.Text.fontSize = headshot ? 38 : 30;
            n.Text.color = killed ? KillColor : headshot ? HeadshotColor : Color.white;
            n.Text.gameObject.SetActive(true);
        }

        void OnSkillRequested(int slot)
        {
            if (slot >= 0 && slot < skillFlash.Length)
            {
                skillFlash[slot] = 1f;
            }

            ShowToast($"スキル枠 {slot + 1}: デッキシステムは Phase 3 で実装します");
        }

        void OnItemUse() => ShowToast("アイテム使用: チャージポーション等は Phase 3 で実装します");

        void OnItemCycle() => ShowToast("アイテム切替: Phase 3 で実装します");

        void ShowToast(string message)
        {
            toastText.text = message;
            toastTimer = 1.6f;
        }

        // ---------------------------------------------------------------- update

        void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f3Key.wasPressedThisFrame ||
                Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame)
            {
                showPerformanceOverlay = !showPerformanceOverlay;
                perfText.gameObject.SetActive(showPerformanceOverlay);
            }
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
            UpdateWeaponPanel();
            UpdateHealthPanel();
            UpdateSkillSlots(dt);
            UpdateDamageNumbers(dt);
            UpdateToast(dt);

            pausedText.gameObject.SetActive(Cursor.lockState != CursorLockMode.Locked);

            if (showPerformanceOverlay)
            {
                UpdatePerfText();
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

        void UpdateWeaponPanel()
        {
            HitscanWeapon weapon = player != null ? player.Weapon : null;
            if (weapon == null || weapon.State == null)
            {
                ammoText.text = "--";
                ammoMaxText.text = "";
                reloadLabel.text = "";
                reloadFill.rectTransform.localScale = new Vector3(0f, 1f, 1f);
                return;
            }

            WeaponState state = weapon.State;
            ammoText.text = state.Ammo.ToString();
            ammoMaxText.text = "/ " + state.MagazineSize;
            bool low = state.Ammo <= Mathf.CeilToInt(state.MagazineSize * 0.25f);
            ammoText.color = low ? KillColor : Color.white;

            if (state.IsReloading)
            {
                reloadLabel.text = "リロード中";
                reloadFill.rectTransform.localScale = new Vector3(state.ReloadProgress, 1f, 1f);
            }
            else
            {
                reloadLabel.text = weapon.Definition != null ? weapon.Definition.displayName : "";
                reloadFill.rectTransform.localScale = new Vector3(0f, 1f, 1f);
            }
        }

        void UpdateHealthPanel()
        {
            Health health = player != null ? player.Health : null;
            if (health != null)
            {
                hpFill.rectTransform.localScale = new Vector3(health.Normalized, 1f, 1f);
                hpText.text = $"HP {Mathf.CeilToInt(health.Current)} / {Mathf.CeilToInt(health.Max)}";
            }

            FpsMotor motor = player != null ? player.Motor : null;
            int maxCharges = motor != null && motor.Profile != null ? motor.Profile.dashCharges : 0;
            float charge = motor != null ? motor.DashCharge : 0f;
            for (int i = 0; i < dashPips.Length; i++)
            {
                bool exists = i < maxCharges;
                dashPips[i].gameObject.SetActive(exists);
                if (!exists)
                {
                    continue;
                }

                float fill = Mathf.Clamp01(charge - i);
                dashPips[i].color = fill >= 1f
                    ? new Color(0.45f, 0.95f, 1f, 1f)
                    : new Color(1f, 1f, 1f, 0.15f + 0.35f * fill);
            }
        }

        void UpdateSkillSlots(float dt)
        {
            for (int i = 0; i < skillSlots.Length; i++)
            {
                skillFlash[i] = Mathf.MoveTowards(skillFlash[i], 0f, dt * 3f);
                skillSlots[i].color = Color.Lerp(PanelColor, new Color(0.55f, 0.35f, 1f, 0.9f), skillFlash[i]);
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

        void UpdateToast(float dt)
        {
            toastTimer -= dt;
            Color c = toastText.color;
            c.a = Mathf.Clamp01(toastTimer / 0.4f);
            toastText.color = c;
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
                $"Tier {PerformanceSettings.Tier} / Quality {quality}{(PerformanceBootstrap.Uncapped ? " / uncapped" : "")}\n" +
                $"{Screen.width}x{Screen.height}  vSync {QualitySettings.vSyncCount}";
        }
    }
}
