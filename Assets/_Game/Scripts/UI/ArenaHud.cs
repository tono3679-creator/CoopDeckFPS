using CoopDeck.Enemies;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>
    /// Top-right objective box (compact): "目標" with a lamp and the wave number, the objective
    /// line and the current area, driven by the arena's <see cref="WaveSchedule"/>. Wave start /
    /// clear also go to the notification log. Area names are provisional (<see cref="WorldTerms"/>).
    /// </summary>
    public class ArenaHud : MonoBehaviour
    {
        [SerializeField] WaveDirector director;
        [Tooltip("Area shown outside the arena (provisional world name).")]
        [SerializeField] string areaName = WorldTerms.TrainingGround;
        [Tooltip("Area shown once the arena has started (provisional world name).")]
        [SerializeField] string arenaName = WorldTerms.TrainingArena;

        RectTransform root;
        PanelView panel;
        LampView lamp;
        Text areaText;
        Text objectiveText;
        Text waveText;
        int shownWave;
        WavePhase shownPhase = (WavePhase)(-1);
        int shownRemaining = -1;
        int shownCountdown = -1;
        float pop;

        public string AreaLine => areaText != null ? areaText.text : "";
        public string ObjectiveLine => objectiveText != null ? objectiveText.text : "";

        void Awake()
        {
            root = HudKit.CreateCanvas(transform, "Arena HUD Canvas", 10);
            Rect layout = HudLayout.Objective;
            panel = UiWidgets.Panel("Objective", root, Vector2.zero, Vector2.zero, Vector2.zero, layout.size, UiSprites.FrameStyle.Small, false,
                UiTokens.WithAlpha(UiTokens.Background, 0.62f), UiTokens.WithAlpha(UiTokens.FrameSteel, 0.55f));
            HudLayout.Place(panel.Root, layout);
            RectTransform r = panel.Root;
            var topLeft = new Vector2(0f, 1f);
            lamp = UiWidgets.Lamp("Lamp", r, topLeft, new Vector2(18f, -16f), new Vector2(14f, 7f));
            Text header = UiWidgets.Label("Header", r, UiTokens.Caption, UiTokens.Accent, TextAnchor.MiddleLeft, topLeft, new Vector2(84f, -16f),
                new Vector2(110f, 20f), true);
            header.text = "目標";
            waveText = UiWidgets.Label("Wave", r, UiTokens.Caption, UiTokens.TextMain, TextAnchor.MiddleRight, new Vector2(1f, 1f), new Vector2(-64f, -16f),
                new Vector2(110f, 20f), true);
            objectiveText = UiWidgets.Label("Objective", r, UiTokens.Body, UiTokens.TextMain, TextAnchor.MiddleLeft, topLeft, new Vector2(190f, -40f),
                new Vector2(356f, 22f), true);
            UiWidgets.FitText(objectiveText, UiTokens.Body);
            areaText = UiWidgets.Label("Area", r, UiTokens.Caption, UiTokens.TextSub, TextAnchor.MiddleLeft, topLeft, new Vector2(190f, -63f),
                new Vector2(356f, 20f));
        }

        void Start()
        {
            if (director == null)
            {
                director = FindAnyObjectByType<WaveDirector>();
            }

            areaText.text = areaName;
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            lamp.Tick(dt);
            if (director == null)
            {
                panel.Root.gameObject.SetActive(false);
                return;
            }

            panel.Root.gameObject.SetActive(true);
            WaveSchedule schedule = director.Schedule;
            if (schedule.WaveNumber == 0)
            {
                // Before the first wave: a quiet pointer to the arena.
                if (shownPhase != (WavePhase)(-2))
                {
                    shownPhase = (WavePhase)(-2);
                    areaText.text = areaName;
                    objectiveText.text = "北西の闘技区画（シアンの枠）に入る";
                    objectiveText.color = UiTokens.TextMain;
                    waveText.text = "";
                }

                lamp.Set(true, 0.8f);
                return;
            }

            if (schedule.WaveNumber != shownWave)
            {
                if (shownWave == 0)
                {
                    areaText.text = arenaName;
                }

                shownWave = schedule.WaveNumber;
                pop = 1f;
                waveText.text = "WAVE " + shownWave;
                lamp.Flash();
                HudNotifications.Post(HudNoticeKind.Objective, $"WAVE {shownWave} 開始 — 敵の反応 {schedule.RemainingInWave}");
            }

            pop = Mathf.MoveTowards(pop, 0f, dt * 2.5f);
            waveText.rectTransform.localScale = Vector3.one * (1f + 0.25f * pop * pop);

            int remaining = schedule.RemainingInWave;
            int countdown = Mathf.CeilToInt(schedule.IntermissionRemaining);
            if (schedule.Phase != shownPhase || remaining != shownRemaining || schedule.Phase == WavePhase.Intermission && countdown != shownCountdown)
            {
                if (schedule.Phase == WavePhase.Intermission && shownPhase != WavePhase.Intermission && shownPhase >= 0)
                {
                    HudNotifications.Post(HudNoticeKind.Success, $"WAVE {schedule.WaveNumber} 突破");
                }

                shownPhase = schedule.Phase;
                shownRemaining = remaining;
                shownCountdown = countdown;
                switch (schedule.Phase)
                {
                    case WavePhase.Intermission:
                        objectiveText.text = $"次のウェーブまで {countdown}";
                        objectiveText.color = UiTokens.Health;
                        break;
                    case WavePhase.Idle:
                        objectiveText.text = "闘技区画の外：出現を一時停止中";
                        objectiveText.color = UiTokens.TextSub;
                        break;
                    default:
                        objectiveText.text = $"敵を殲滅する　残り {remaining}";
                        objectiveText.color = UiTokens.TextMain;
                        break;
                }
            }

            lamp.OnColor = schedule.Phase == WavePhase.Intermission ? UiTokens.Health : UiTokens.Accent;
            lamp.Set(true, schedule.Phase == WavePhase.Idle ? 0.8f : 0f);
        }
    }
}
