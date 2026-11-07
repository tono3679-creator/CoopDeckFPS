using System;
using System.Collections;
using System.IO;
using CoopDeck.Combat;
using CoopDeck.Combos;
using CoopDeck.Core;
using CoopDeck.Enemies;
using CoopDeck.Player;
using CoopDeck.Skills;
using UnityEngine;

namespace CoopDeck.UI.Diagnostics
{
    /// <summary>
    /// Unattended performance capture for builds.
    ///
    ///   CoopDeckFPS.exe -perfrun 30 [-perfout C:\path\result.json] [-perfidle] [-perfarena] [-perfpractice]
    ///                   [-vfx low|medium|high] [-tier handheld] [-uncapped]
    ///
    /// Waits 3 s, measures for N seconds while the autopilot shoots (unless -perfidle), writes a
    /// JSON result and a screenshot next to it, then quits. -perfarena starts the player inside the
    /// combat arena so enemy waves (AI, NavMesh, projectiles) are part of the measurement; the
    /// player cannot die during that run so the fight keeps going. -perfpractice switches to the
    /// combo practice deck so element combos happen often; the JSON counts them.
    /// </summary>
    public class AutoPerfRunner : MonoBehaviour
    {
        const float WarmupSeconds = 3f;

        [Serializable]
        class PerfResult
        {
            public string timestampUtc;
            public string scene;
            public string tier;
            public string tierReason;
            public string quality;
            public string resolution;
            public bool uncapped;
            public int vSyncCount;
            public int targetFrameRate;
            public bool autopilot;
            public bool arena;
            public int waveReached;
            public int maxEnemiesAlive;
            public bool practiceDeck;
            public int combosTriggered;
            public string vfx;
            public float seconds;
            public int frames;
            public float averageFps;
            public float onePercentLowFps;
            public float averageMs;
            public float worstMs;
            public string gpu;
            public string deviceModel;
            public string cpu;
            public string unityVersion;
            public string screenshot;
        }

        float duration;
        string outputPath;
        bool autopilot;
        bool arena;
        WaveDirector director;
        bool measuring;
        int maxEnemiesAlive;
        bool practice;
        int combos;
        FrameTimeMonitor monitor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void MaybeStart()
        {
            string[] args = Environment.GetCommandLineArgs();
            string value = PerformanceBootstrap.GetArgValue(args, "-perfrun");
            if (string.IsNullOrEmpty(value) || !float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float seconds))
            {
                return;
            }

            var go = new GameObject("[AutoPerfRunner]");
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<AutoPerfRunner>();
            runner.duration = Mathf.Max(5f, seconds);
            runner.outputPath = PerformanceBootstrap.GetArgValue(args, "-perfout");
            runner.autopilot = Array.IndexOf(args, "-perfidle") < 0;
            runner.arena = Array.IndexOf(args, "-perfarena") >= 0;
            runner.practice = Array.IndexOf(args, "-perfpractice") >= 0;
        }

        void Update()
        {
            if (measuring && director != null)
            {
                maxEnemiesAlive = Mathf.Max(maxEnemiesAlive, director.AliveEnemies.Count);
            }
        }

        void CountCombo(ComboReport report)
        {
            if (measuring)
            {
                combos++;
            }
        }

        void OnDestroy()
        {
            ComboEvents.Triggered -= CountCombo;
        }

        /// <summary>Moves the player into the arena (facing the spawn side) so the waves start.</summary>
        void EnterArena(FpsCharacter character)
        {
            director = FindAnyObjectByType<WaveDirector>();
            if (director == null || character == null)
            {
                Debug.LogWarning("[AutoPerfRunner] -perfarena: no WaveDirector or player in the scene");
                return;
            }

            Vector3 center = director.transform.position;
            Vector3 start = center + new Vector3(0f, 0.3f, -director.ZoneBounds.extents.z * 0.6f);
            if (character.Motor != null)
            {
                character.Motor.Teleport(start);
            }

            if (character.Look != null)
            {
                character.Look.SetRotation(0f, 0f); // +Z: the spawn points are on the far side
            }

            if (character.Health != null)
            {
                character.Health.SetMaxHealth(100000f, true);
            }
        }

        IEnumerator Start()
        {
            monitor = gameObject.AddComponent<FrameTimeMonitor>();
            monitor.AutoRecompute = false;
            monitor.SetCapacity(Mathf.CeilToInt(duration * 250f));

            FpsCharacter player = FindAnyObjectByType<FpsCharacter>();
            if (arena)
            {
                yield return null; // let the scene's Start() methods run first
                EnterArena(player);
            }

            if (practice && player != null && player.TryGetComponent(out DeckController deckController))
            {
                yield return null; // the deck is built in the controller's Awake/Start
                deckController.SetPracticeDeck(true);
            }

            ComboEvents.Triggered += CountCombo;

            if (autopilot && player != null)
            {
                FpsLook look = player.Look;
                var pilot = new AutopilotCommandSource(look != null ? () => look.Pitch : null);
                if (practice)
                {
                    // Combo stress: skills only, every 0.8 s, so enemies live long enough for
                    // element pairs to land within the 4 s window (the rifle kills them in ~1 s).
                    pilot.SkillInterval = 0.8f;
                    pilot.FireWeapon = false;
                }
                if (director != null && look != null)
                {
                    // Fight the waves: track the nearest enemy's chest instead of sweeping.
                    pilot.AimReader = look.GetAimRay;
                    pilot.TargetProvider = () =>
                    {
                        ICombatActor enemy = CombatantRegistry.FindNearestHostile(TeamId.Players, player.Position, 60f);
                        return enemy != null ? enemy.Position + Vector3.up * 1.1f : (Vector3?)null;
                    };
                }

                player.CommandSource = pilot;
            }

            yield return new WaitForSecondsRealtime(WarmupSeconds);
            monitor.ResetStats();
            measuring = true;

            float half = duration * 0.5f;
            yield return new WaitForSecondsRealtime(half);

            if (string.IsNullOrEmpty(outputPath))
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                outputPath = Path.Combine(Application.persistentDataPath, "PerfCaptures", $"perf_{stamp}.json");
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string screenshotPath = Path.ChangeExtension(Path.GetFullPath(outputPath), ".png");
            ScreenCapture.CaptureScreenshot(screenshotPath);

            yield return new WaitForSecondsRealtime(duration - half);
            monitor.Recompute();

            int level = QualitySettings.GetQualityLevel();
            var result = new PerfResult
            {
                timestampUtc = DateTime.UtcNow.ToString("o"),
                scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
                tier = PerformanceSettings.Tier.ToString(),
                tierReason = PerformanceBootstrap.DetectionReason,
                quality = level >= 0 && level < QualitySettings.names.Length ? QualitySettings.names[level] : "?",
                resolution = $"{Screen.width}x{Screen.height}",
                uncapped = PerformanceBootstrap.Uncapped,
                vSyncCount = QualitySettings.vSyncCount,
                targetFrameRate = Application.targetFrameRate,
                autopilot = autopilot,
                arena = arena && director != null,
                waveReached = director != null ? director.Schedule.WaveNumber : 0,
                maxEnemiesAlive = maxEnemiesAlive,
                practiceDeck = practice,
                combosTriggered = combos,
                vfx = PerformanceSettings.Vfx.ToString(),
                seconds = duration,
                frames = monitor.SampleCount,
                averageFps = monitor.AverageFps,
                onePercentLowFps = monitor.OnePercentLowFps,
                averageMs = monitor.AverageMs,
                worstMs = monitor.WorstMs,
                gpu = SystemInfo.graphicsDeviceName,
                deviceModel = SystemInfo.deviceModel,
                cpu = SystemInfo.processorType,
                unityVersion = Application.unityVersion,
                screenshot = screenshotPath,
            };

            File.WriteAllText(Path.GetFullPath(outputPath), JsonUtility.ToJson(result, true));
            Debug.Log($"[AutoPerfRunner] avg {result.averageFps:0.0} fps, 1% low {result.onePercentLowFps:0.0} fps, worst {result.worstMs:0.0} ms, " +
                      $"arena {result.arena} (wave {result.waveReached}, max {result.maxEnemiesAlive} enemies), combos {result.combosTriggered}, vfx {result.vfx} -> {outputPath}");

            yield return null;
            Application.Quit();
        }
    }
}
