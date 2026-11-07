using UnityEngine;

namespace CoopDeck.Combos
{
    /// <summary>
    /// Plays each combo's sound (the co-op sound for co-op combos) from a small pool of audio
    /// sources at the combo position. Listens to <see cref="ComboEvents.Triggered"/> only, so sounds
    /// can be swapped on the <see cref="ComboDefinition"/> without touching combo rules.
    /// </summary>
    public class ComboAudio : MonoBehaviour
    {
        [SerializeField, Range(1, 8)] int voices = 4;
        [SerializeField, Range(0f, 1f)] float volume = 0.8f;
        [Tooltip("0 = heard the same everywhere (2D), 1 = fully positional.")]
        [SerializeField, Range(0f, 1f)] float spatialBlend = 0.35f;
        [Tooltip("Same sound again within this time is skipped (area skills completing a combo on a whole group).")]
        [SerializeField, Min(0f)] float repeatGuard = 0.12f;

        AudioSource[] sources;
        int next;
        AudioClip lastClip;
        float lastTime = float.NegativeInfinity;

        public int PlayedCount { get; private set; }

        void Awake()
        {
            sources = new AudioSource[Mathf.Max(1, voices)];
            for (int i = 0; i < sources.Length; i++)
            {
                var go = new GameObject($"ComboVoice{i}");
                go.transform.SetParent(transform, false);
                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = spatialBlend;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 4f;
                source.maxDistance = 60f;
                source.dopplerLevel = 0f;
                sources[i] = source;
            }
        }

        void OnEnable()
        {
            ComboEvents.Triggered += OnTriggered;
        }

        void OnDisable()
        {
            ComboEvents.Triggered -= OnTriggered;
        }

        void OnTriggered(ComboReport report)
        {
            if (report.Definition == null)
            {
                return;
            }

            AudioClip clip = report.IsCoop && report.Definition.coopSound != null ? report.Definition.coopSound : report.Definition.sound;
            if (clip == null || clip == lastClip && Time.time - lastTime < repeatGuard)
            {
                return;
            }

            lastClip = clip;
            lastTime = Time.time;
            AudioSource source = sources[next];
            next = (next + 1) % sources.Length;
            source.transform.position = report.Position;
            source.clip = clip;
            source.volume = volume * (report.IsCoop ? 1f : 0.85f);
            source.Play();
            PlayedCount++;
        }
    }
}
