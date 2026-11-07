using CoopDeck.Enemies;
using UnityEngine;
using UnityEngine.UI;

namespace CoopDeck.UI
{
    /// <summary>Wave banner for the combat arena: wave number, enemies left, intermission countdown.</summary>
    public class ArenaHud : MonoBehaviour
    {
        [SerializeField] WaveDirector director;

        RectTransform root;
        Text waveText;
        Text detailText;
        int shownWave;
        float pop;

        void Awake()
        {
            root = HudKit.CreateCanvas(transform, "Arena HUD Canvas", 10);
            waveText = HudKit.CreateText("Wave", root, 34, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -112f), new Vector2(700f, 44f));
            HudKit.AddOutline(waveText, 0.7f);
            detailText = HudKit.CreateText("Detail", root, 22, TextAnchor.MiddleCenter, new Vector2(0.5f, 1f), new Vector2(0f, -146f), new Vector2(700f, 30f));
            HudKit.AddOutline(detailText, 0.6f);
        }

        void Start()
        {
            if (director == null)
            {
                director = FindAnyObjectByType<WaveDirector>();
            }
        }

        void LateUpdate()
        {
            if (director == null)
            {
                waveText.enabled = detailText.enabled = false;
                return;
            }

            WaveSchedule schedule = director.Schedule;
            if (schedule.WaveNumber == 0)
            {
                // Before the first wave: a quiet pointer to the arena.
                waveText.enabled = false;
                detailText.enabled = true;
                detailText.text = "北西のシアンの枠（アリーナ）に入ると敵が出現します";
                detailText.color = new Color(1f, 1f, 1f, 0.65f);
                return;
            }

            waveText.enabled = detailText.enabled = true;
            detailText.color = Color.white;

            if (schedule.WaveNumber != shownWave)
            {
                shownWave = schedule.WaveNumber;
                pop = 1f;
            }

            pop = Mathf.MoveTowards(pop, 0f, Time.unscaledDeltaTime * 2.5f);
            waveText.rectTransform.localScale = Vector3.one * (1f + 0.35f * pop * pop);

            switch (schedule.Phase)
            {
                case WavePhase.Intermission:
                    waveText.text = $"WAVE {schedule.WaveNumber} クリア！";
                    waveText.color = new Color(0.5f, 1f, 0.7f);
                    detailText.text = $"次のウェーブまで {Mathf.CeilToInt(schedule.IntermissionRemaining)}";
                    break;
                case WavePhase.Idle:
                    waveText.text = $"WAVE {schedule.WaveNumber}";
                    waveText.color = new Color(1f, 1f, 1f, 0.6f);
                    detailText.text = "アリーナの外：出現を一時停止中";
                    break;
                default:
                    waveText.text = $"WAVE {schedule.WaveNumber}";
                    waveText.color = new Color(1f, 0.85f, 0.4f);
                    detailText.text = $"残りの敵 {schedule.RemainingInWave}";
                    break;
            }
        }
    }
}
