using CoopDeck.Combat;
using CoopDeck.Player;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Feedback when the local player is hit: an edge vignette (red for HP loss, cyan when a
    /// shield absorbed it), arcs around the crosshair pointing at the attacker, and a "down"
    /// banner while respawning. No camera shake, per the FPS feel guidelines.
    /// </summary>
    public class DamageFeedbackHud : MonoBehaviour
    {
        const int IndicatorCount = 4;
        const float IndicatorLife = 1.3f;
        const float IndicatorRadius = 130f;

        static readonly Color HurtColor = new Color(1f, 0.12f, 0.15f, 1f);
        static readonly Color ShieldColor = new Color(0.45f, 0.9f, 1f, 1f);

        [SerializeField] FpsCharacter player;

        RectTransform root;
        Image vignette;
        float vignetteStrength;
        Color vignetteColor = HurtColor;
        Text downText;
        Revivable revivable;
        PlayerRespawn respawn;
        readonly Indicator[] indicators = new Indicator[IndicatorCount];
        int nextIndicator;

        sealed class Indicator
        {
            public RectTransform Pivot;
            public Image Arc;
            public Vector3 Source;
            public float Timer;
        }

        void Awake()
        {
            root = HudKit.CreateCanvas(transform, "Damage Feedback Canvas", 9, false); // the vignette covers the whole screen

            vignette = HudKit.CreateImage("Vignette", root, new Color(1f, 0f, 0f, 0f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RectTransform v = vignette.rectTransform;
            v.anchorMin = Vector2.zero;
            v.anchorMax = Vector2.one;
            v.sizeDelta = Vector2.zero;
            vignette.sprite = CreateVignetteSprite();

            for (int i = 0; i < IndicatorCount; i++)
            {
                RectTransform pivot = HudKit.CreateRect("DamageIndicator", root, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                Image arc = HudKit.CreateImage("Arc", pivot, new Color(1f, 0.2f, 0.2f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, IndicatorRadius), new Vector2(84f, 12f));
                HudKit.AddOutline(arc, 0.4f);
                indicators[i] = new Indicator { Pivot = pivot, Arc = arc };
            }

            downText = HudKit.CreateText("Down", root, UiTokens.Display, TextAnchor.MiddleCenter, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1200f, 70f));
            downText.fontStyle = FontStyle.Bold;
            downText.text = "ダウン！ まもなく復活します";
            downText.color = UiTokens.DangerText;
            HudKit.AddOutline(downText, 0.8f);
            downText.gameObject.SetActive(false);
        }

        void Start()
        {
            if (player == null)
            {
                player = FpsCharacter.FindLocalHuman();
            }
        }

        void OnEnable() => CombatEvents.DamageApplied += OnDamageApplied;

        void OnDisable() => CombatEvents.DamageApplied -= OnDamageApplied;

        void OnDamageApplied(DamageEvent e)
        {
            if (player == null || e.Target != player.Health)
            {
                return;
            }

            float total = e.Result.Damage + e.Result.Absorbed;
            float strength = Mathf.Clamp(total / Mathf.Max(1f, e.Target.Max) * 4f, 0.25f, 0.85f);
            vignetteStrength = Mathf.Max(vignetteStrength, strength);
            vignetteColor = e.Result.Damage <= 0f && e.Result.Absorbed > 0f ? ShieldColor : HurtColor;

            Vector3 source = e.Info.Point - e.Info.Direction * 5f;
            if (e.Info.Source is Object o && o != null)
            {
                source = e.Info.Source.Position;
            }

            Indicator indicator = indicators[nextIndicator];
            nextIndicator = (nextIndicator + 1) % indicators.Length;
            indicator.Source = source;
            indicator.Timer = IndicatorLife;
            indicator.Arc.color = vignetteColor;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            vignetteStrength = Mathf.MoveTowards(vignetteStrength, 0f, dt * 1.4f);
            Color c = vignetteColor;
            c.a = vignetteStrength;
            vignette.color = c;

            Transform view = player != null && player.Look != null && player.Look.ViewCamera != null
                ? player.Look.ViewCamera.transform
                : Camera.main != null ? Camera.main.transform : null;

            foreach (Indicator indicator in indicators)
            {
                indicator.Timer -= dt;
                bool visible = indicator.Timer > 0f && view != null && player != null;
                indicator.Arc.enabled = visible;
                if (!visible)
                {
                    continue;
                }

                // 0° = straight ahead (top of the crosshair), clockwise to the right.
                Vector3 forward = view.forward;
                forward.y = 0f;
                Vector3 toSource = indicator.Source - player.transform.position;
                toSource.y = 0f;
                float angle = Vector3.SignedAngle(forward, toSource, Vector3.up);
                indicator.Pivot.localRotation = Quaternion.Euler(0f, 0f, -angle);
                Color arc = indicator.Arc.color;
                arc.a = Mathf.Clamp01(indicator.Timer / IndicatorLife) * 0.95f;
                indicator.Arc.color = arc;
            }

            bool down = player != null && !player.IsAlive;
            downText.gameObject.SetActive(down);
            if (down)
            {
                UpdateDownText();
            }
        }

        string lastDownText;

        /// <summary>Downed: say whether an ally is coming (and the revive progress) or a respawn is near.</summary>
        void UpdateDownText()
        {
            if (revivable == null)
            {
                revivable = player.GetComponent<Revivable>();
                respawn = player.GetComponent<PlayerRespawn>();
            }

            string text;
            if (revivable != null && respawn != null && respawn.IsWaitingForRevive)
            {
                text = revivable.CurrentReviver != null
                    ? $"ダウン！ {revivable.CurrentReviver.DisplayName} が蘇生中… {revivable.Progress01 * 100f:0}%"
                    : $"ダウン！ 味方の救助を待っています（{revivable.BleedOutRemaining:0} 秒）";
            }
            else
            {
                text = "ダウン！ まもなく復活します";
            }

            if (text != lastDownText)
            {
                downText.text = text;
                lastDownText = text;
            }
        }

        static Sprite CreateVignetteSprite()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "DamageVignette", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.414f;
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.45f, 1f, d));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
