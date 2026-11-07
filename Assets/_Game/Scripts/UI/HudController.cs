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
    /// Combat HUD built in code: dynamic crosshair, hit marker, damage numbers, ammo/reload,
    /// HP with shield and attack-buff readout, dash charges, pause notice, controls hint and the
    /// F3 performance overlay. The card hand lives in <see cref="DeckHud"/>.
    /// </summary>
    public class HudController : MonoBehaviour
    {
        const int DamageNumberPoolSize = 24;

        [SerializeField] FpsCharacter player;
        [SerializeField] FrameTimeMonitor frameMonitor;
        [SerializeField] bool showPerformanceOverlay = true;
        [SerializeField] bool showControlsHint = true;

        static readonly Color CrosshairColor = new Color(1f, 1f, 1f, 0.92f);
        static readonly Color HitColor = new Color(1f, 1f, 1f, 1f);
        static readonly Color HeadshotColor = new Color(1f, 0.85f, 0.2f, 1f);
        static readonly Color KillColor = new Color(1f, 0.25f, 0.3f, 1f);
        static readonly Color SkillHitColor = new Color(0.75f, 0.6f, 1f, 1f);
        static readonly Color CriticalColor = new Color(1f, 0.55f, 0.15f, 1f);
        static readonly Color ComboColor = new Color(1f, 0.5f, 0.95f, 1f);
        static readonly Color ShieldColor = new Color(0.55f, 0.9f, 1f, 1f);

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
        Image shieldFill;
        Text hpText;
        Text statusText;
        readonly Image[] dashPips = new Image[3];
        Text perfText;
        Text pausedText;
        Text hintText;

        DamageNumber[] damageNumbers;
        int nextDamageNumber;
        bool hintCollapsed;

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
            BuildCanvas();
        }

        void OnEnable()
        {
            CombatEvents.DamageApplied += OnDamageApplied;
        }

        void OnDisable()
        {
            CombatEvents.DamageApplied -= OnDamageApplied;
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
        }

        // ---------------------------------------------------------------- build

        void BuildCanvas()
        {
            root = HudKit.CreateCanvas(transform, "HUD Canvas", 10);

            BuildCrosshair();
            BuildHitMarker();
            BuildAmmo();
            BuildHealth();

            perfText = HudKit.CreateText("Perf", root, 20, TextAnchor.UpperLeft, new Vector2(0f, 1f), new Vector2(170f, -80f), new Vector2(320f, 130f));
            perfText.gameObject.SetActive(showPerformanceOverlay);

            pausedText = HudKit.CreateText("Paused", root, 40, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(1200f, 120f));
            pausedText.text = "一時停止中\nクリック / Esc / Start で再開　（パッド Y：NPC デッキ編集）";
            HudKit.AddOutline(pausedText);
            pausedText.gameObject.SetActive(false);

            hintText = HudKit.CreateText("Hint", root, 18, TextAnchor.UpperCenter, new Vector2(0.5f, 1f), new Vector2(0f, -52f), new Vector2(1700f, 110f));
            hintText.text =
                "WASD 移動 / Shift 走る / Ctrl 回避 / Space ジャンプ / 左クリック 射撃 / R リロード / Q・E・F スキル / 1・2・3 カード選択 / G アイテム使用 / T アイテム切替 / F1 ヒント / F3 性能\n" +
                "パッド: RT 射撃 / LB・RB・Y スキル / 十字← → カード選択 / 十字↑ アイテム使用 / 十字↓ アイテム切替 / A ジャンプ / B 回避 / X リロード / L3 走る\n" +
                "コンボ（4 秒以内）: 氷＋雷 / 火＋重力 / 火＋雷　F4 エフェクト品質 / F6 コンボ段階 / F7 練習デッキ\n" +
                "NPC 指示: V / R3 方針切替（自動→集中→コンボ優先）　中クリック・X / LT 照準の敵を集中攻撃　Tab（パッドはポーズ中 Y）NPC デッキ編集　ダウンした味方の近くに立つと蘇生";
            hintText.color = new Color(1f, 1f, 1f, 0.8f);
            HudKit.AddOutline(hintText, 0.5f);
            hintText.gameObject.SetActive(showControlsHint);

            damageNumbers = new DamageNumber[DamageNumberPoolSize];
            for (int i = 0; i < damageNumbers.Length; i++)
            {
                Text t = HudKit.CreateText("DamageNumber", root, 30, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200f, 50f));
                HudKit.AddOutline(t);
                t.gameObject.SetActive(false);
                damageNumbers[i] = new DamageNumber { Text = t, Rect = t.rectTransform };
            }
        }

        void BuildCrosshair()
        {
            HudKit.CreateImage("CrosshairDot", root, CrosshairColor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f));
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

        void BuildAmmo()
        {
            Image panel = HudKit.CreateImage("AmmoPanel", root, HudKit.PanelColor, new Vector2(1f, 0f), new Vector2(-160f, 75f), new Vector2(260f, 110f));
            // Count is right-aligned ending at x=+20, "/ 30" starts at x=+30: they can never overlap.
            ammoText = HudKit.CreateText("Ammo", panel.rectTransform, 56, TextAnchor.MiddleRight, new Vector2(0.5f, 0.5f), new Vector2(-60f, 12f), new Vector2(160f, 70f));
            ammoMaxText = HudKit.CreateText("AmmoMax", panel.rectTransform, 26, TextAnchor.MiddleLeft, new Vector2(0.5f, 0.5f), new Vector2(75f, 4f), new Vector2(90f, 40f));
            reloadLabel = HudKit.CreateText("Reload", panel.rectTransform, 18, TextAnchor.MiddleCenter, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(240f, 24f));
            Image reloadBack = HudKit.CreateImage("ReloadBack", panel.rectTransform, new Color(1f, 1f, 1f, 0.15f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(220f, 6f));
            reloadFill = HudKit.CreateImage("ReloadFill", reloadBack.rectTransform, new Color(0.45f, 0.95f, 1f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(220f, 6f));
            reloadFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        }

        void BuildHealth()
        {
            Image panel = HudKit.CreateImage("HealthPanel", root, HudKit.PanelColor, new Vector2(0f, 0f), new Vector2(200f, 80f), new Vector2(340f, 110f));
            Image back = HudKit.CreateImage("HpBack", panel.rectTransform, new Color(1f, 1f, 1f, 0.12f), new Vector2(0.5f, 0.5f), new Vector2(0f, 14f), new Vector2(300f, 22f));
            hpFill = HudKit.CreateImage("HpFill", back.rectTransform, new Color(0.35f, 1f, 0.55f, 1f), new Vector2(0f, 0.5f), Vector2.zero, new Vector2(300f, 22f));
            hpFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            shieldFill = HudKit.CreateImage("ShieldFill", back.rectTransform, ShieldColor, new Vector2(0f, 1f), new Vector2(0f, 3f), new Vector2(300f, 6f));
            shieldFill.rectTransform.pivot = new Vector2(0f, 0.5f);
            hpText = HudKit.CreateText("HpText", panel.rectTransform, 20, TextAnchor.MiddleLeft, new Vector2(0f, 0.5f), new Vector2(170f, 14f), new Vector2(300f, 26f));
            HudKit.AddOutline(hpText, 0.5f);
            statusText = HudKit.CreateText("Status", panel.rectTransform, 17, TextAnchor.MiddleLeft, new Vector2(0f, 1f), new Vector2(170f, 14f), new Vector2(300f, 24f));
            statusText.color = new Color(1f, 0.92f, 0.55f);

            for (int i = 0; i < dashPips.Length; i++)
            {
                dashPips[i] = HudKit.CreateImage("DashPip", panel.rectTransform, Color.white, new Vector2(0f, 0f), new Vector2(34f + i * 30f, 20f), new Vector2(24f, 8f));
            }

            Text dashLabel = HudKit.CreateText("DashLabel", panel.rectTransform, 15, TextAnchor.MiddleLeft, new Vector2(0f, 0f), new Vector2(165f, 20f), new Vector2(120f, 20f));
            dashLabel.text = "回避";
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
            n.Text.color = killed ? KillColor : colorOverride ?? (headshot ? HeadshotColor : skill ? SkillHitColor : Color.white);
            n.Text.gameObject.SetActive(true);
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

            if (Keyboard.current != null && Keyboard.current.f1Key.wasPressedThisFrame)
            {
                hintCollapsed = !hintCollapsed;
                hintText.gameObject.SetActive(showControlsHint && !hintCollapsed);
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
            UpdateDamageNumbers(dt);

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
            CombatStatus status = player != null ? player.Status : null;
            float shield = status != null ? status.Model.Shield : 0f;

            if (health != null)
            {
                hpFill.rectTransform.localScale = new Vector3(health.Normalized, 1f, 1f);
                string shieldText = shield > 0f ? $"  +シールド {Mathf.CeilToInt(shield)}" : "";
                hpText.text = health.IsAlive
                    ? $"HP {Mathf.CeilToInt(health.Current)} / {Mathf.CeilToInt(health.Max)}{shieldText}"
                    : "ダウン… まもなく復活";
                shieldFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(shield / health.Max), 1f, 1f);
            }

            string buffText = "";
            if (status != null && status.Model.DamageBuff > 0f)
            {
                buffText = $"攻撃力 +{status.Model.DamageBuff * 100f:0}%  ({status.Model.DamageBuffTimeRemaining:0.0}s)";
            }

            if (status != null && shield > 0f)
            {
                buffText += (buffText.Length > 0 ? "   " : "") + $"シールド {status.Model.ShieldTimeRemaining:0.0}s";
            }

            statusText.text = buffText;

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
                $"Tier {PerformanceSettings.Tier} / Quality {quality} / VFX {PerformanceSettings.Vfx}{(PerformanceBootstrap.Uncapped ? " / uncapped" : "")}\n" +
                $"{Screen.width}x{Screen.height}  vSync {QualitySettings.vSyncCount}";
        }
    }
}
