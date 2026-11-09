using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Placeholder combo sounds synthesised in code (sine tones, filtered noise, envelopes) and saved
    /// as 16-bit mono WAV, so the combo sound slots work without any third-party audio. Replace the
    /// files (same names) with final sounds later; existing files are kept unless overwritten.
    /// </summary>
    public static class ProceduralAudioFactory
    {
        public const string Folder = GamePaths.Root + "/Audio/SFX";
        const int SampleRate = 44100;

        public enum ComboSound
        {
            Superconductor,
            Singularity,
            Overload,
        }

        /// <summary>The Thunderhop discharge's two beats: the first strike's burst and the chained enemy's zap.</summary>
        public enum DischargeSound
        {
            Burst,
            Chain,
        }

        /// <summary>
        /// Burst ("ドン"): a sharp crack on a short low thump with a little crackle, ~0.3 s. Chain ("バチッ"): a higher,
        /// brighter electric zap, ~0.2 s. Both start at once (no lead-in) so they land on the impact frame.
        /// </summary>
        public static AudioClip EnsureDischargeSound(DischargeSound kind, bool overwrite = false)
        {
            return Ensure($"SFX_Discharge_{kind}", overwrite, () => Normalize(kind == DischargeSound.Burst ? DischargeBurst() : DischargeZap(), kind == DischargeSound.Burst ? 0.9f : 0.8f));
        }

        static float[] DischargeBurst()
        {
            var random = new System.Random(4021);
            float[] s = new float[(int)(SampleRate * 0.32f)];
            float lp = 0f;
            float hp = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                lp += (noise - lp) * 0.12f;
                hp = noise - lp;
                float crack = hp * Env(t, 0.0005f, 0.03f) * 1.1f;
                float thump = Mathf.Sin(2f * Mathf.PI * (110f - 160f * t) * t) * Env(t, 0.002f, 0.13f) * 0.9f;
                float body = lp * Env(t, 0.002f, 0.09f) * 1.6f;
                float crackle = hp * Env(t, 0.004f, 0.16f) * (random.NextDouble() < 0.18 ? 0.8f : 0.05f);
                s[i] = crack + thump + body + crackle;
            }

            return s;
        }

        static float[] DischargeZap()
        {
            var random = new System.Random(5113);
            float[] s = new float[(int)(SampleRate * 0.2f)];
            float lp = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float noise = (float)random.NextDouble() * 2f - 1f;
                lp += (noise - lp) * 0.35f;
                float hiss = (noise - lp) * Env(t, 0.0005f, 0.05f) * (random.NextDouble() < 0.3 ? 1f : 0.2f);
                float buzz = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 140f * t)) * 0.25f + 1f;
                float tone = Mathf.Sin(2f * Mathf.PI * (2600f - 3000f * t) * t) * buzz * Env(t, 0.001f, 0.07f) * 0.45f;
                float tone2 = Mathf.Sin(2f * Mathf.PI * 3900f * t) * Env(t, 0.001f, 0.04f) * 0.2f;
                s[i] = hiss * 0.9f + tone + tone2;
            }

            return s;
        }

        public static AudioClip EnsureComboSound(ComboSound kind, bool coop, bool overwrite = false)
        {
            string name = $"SFX_Combo_{kind}{(coop ? "_Coop" : "")}";
            return Ensure(name, overwrite, () =>
            {
                float[] samples = Base(kind);
                if (coop)
                {
                    samples = Mix(samples, CoopShimmer(), 0.55f, 0.06f);
                }

                return Normalize(samples, 0.85f);
            });
        }

        static AudioClip Ensure(string name, bool overwrite, Func<float[]> synth)
        {
            EditorUtil.EnsureFolder(Folder);
            string path = $"{Folder}/{name}.wav";
            if (overwrite || !File.Exists(path))
            {
                WriteWav(path, synth());
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(path) is AudioImporter importer)
                {
                    importer.forceToMono = true;
                    AudioImporterSampleSettings settings = importer.defaultSampleSettings;
                    settings.loadType = AudioClipLoadType.DecompressOnLoad; // short, played often: no decode hitch
                    settings.compressionFormat = AudioCompressionFormat.Vorbis;
                    settings.quality = 0.6f;
                    importer.defaultSampleSettings = settings;
                    importer.SaveAndReimport();
                }
            }

            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }

        // ---------------------------------------------------------------- synthesis

        static float[] Base(ComboSound kind)
        {
            var random = new System.Random(kind.GetHashCode() * 7 + 11);
            switch (kind)
            {
                case ComboSound.Superconductor:
                {
                    // Electric crackle + falling crystal tone.
                    float[] s = new float[(int)(SampleRate * 0.8f)];
                    float lp = 0f;
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = i / (float)SampleRate;
                        float noise = (float)random.NextDouble() * 2f - 1f;
                        lp += (noise - lp) * 0.55f;
                        float crackle = (noise - lp) * Env(t, 0.002f, 0.18f) * (random.NextDouble() < 0.35 ? 1f : 0.25f);
                        float tone = Mathf.Sin(2f * Mathf.PI * (1320f - 380f * t) * t) * Env(t, 0.005f, 0.55f) * 0.5f;
                        float tone2 = Mathf.Sin(2f * Mathf.PI * 1980f * t) * Env(t, 0.003f, 0.25f) * 0.25f;
                        s[i] = crackle * 0.9f + tone + tone2;
                    }

                    return s;
                }

                case ComboSound.Singularity:
                {
                    // Rising suction then a deep blast.
                    float[] s = new float[(int)(SampleRate * 1.6f)];
                    float lp = 0f;
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = i / (float)SampleRate;
                        float noise = (float)random.NextDouble() * 2f - 1f;
                        lp += (noise - lp) * 0.06f;
                        float suck = lp * Mathf.Clamp01(t / 0.85f) * (t < 0.9f ? 1f : Mathf.Exp(-(t - 0.9f) * 30f)) * 2.5f;
                        float hum = Mathf.Sin(2f * Mathf.PI * (60f + 140f * Mathf.Clamp01(t / 0.9f)) * t) * (t < 0.9f ? 0.35f : 0f);
                        float bt = t - 0.9f;
                        float boom = bt > 0f ? (Mathf.Sin(2f * Mathf.PI * (70f - 30f * bt) * bt) * 0.9f + lp * 1.4f) * Env(bt, 0.004f, 0.55f) : 0f;
                        s[i] = suck + hum + boom;
                    }

                    return s;
                }

                default:
                {
                    // Overload: sharp crack and a rumbling explosion.
                    float[] s = new float[(int)(SampleRate * 1.1f)];
                    float lp = 0f;
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = i / (float)SampleRate;
                        float noise = (float)random.NextDouble() * 2f - 1f;
                        lp += (noise - lp) * 0.09f;
                        float crack = noise * Env(t, 0.001f, 0.06f) * 0.8f;
                        float body = lp * Env(t, 0.004f, 0.7f) * 2.2f;
                        float sub = Mathf.Sin(2f * Mathf.PI * (85f - 40f * t) * t) * Env(t, 0.004f, 0.5f) * 0.8f;
                        s[i] = crack + body + sub;
                    }

                    return s;
                }
            }
        }

        /// <summary>Bright rising arpeggio (C6 E6 G6 C7) with a shimmer: the co-op signature.</summary>
        static float[] CoopShimmer()
        {
            float[] notes = { 1046.5f, 1318.5f, 1568f, 2093f };
            float[] s = new float[(int)(SampleRate * 1.1f)];
            for (int n = 0; n < notes.Length; n++)
            {
                float start = n * 0.07f;
                for (int i = 0; i < s.Length; i++)
                {
                    float t = i / (float)SampleRate - start;
                    if (t < 0f)
                    {
                        continue;
                    }

                    float vibrato = 1f + 0.004f * Mathf.Sin(2f * Mathf.PI * 6f * t);
                    float tone = Mathf.Sin(2f * Mathf.PI * notes[n] * vibrato * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * notes[n] * 2f * t);
                    s[i] += tone * Env(t, 0.004f, 0.6f) * 0.3f;
                }
            }

            return s;
        }

        /// <summary>Attack-decay envelope: linear rise, exponential fall.</summary>
        static float Env(float t, float attack, float decay)
        {
            if (t < 0f)
            {
                return 0f;
            }

            return t < attack ? t / attack : Mathf.Exp(-(t - attack) / Mathf.Max(0.001f, decay) * 3f);
        }

        static float[] Mix(float[] a, float[] b, float bGain, float bDelay)
        {
            int offset = (int)(bDelay * SampleRate);
            float[] result = new float[Mathf.Max(a.Length, b.Length + offset)];
            for (int i = 0; i < a.Length; i++)
            {
                result[i] += a[i];
            }

            for (int i = 0; i < b.Length; i++)
            {
                result[i + offset] += b[i] * bGain;
            }

            return result;
        }

        static float[] Normalize(float[] samples, float peak)
        {
            float max = 0.0001f;
            foreach (float v in samples)
            {
                max = Mathf.Max(max, Mathf.Abs(v));
            }

            float gain = peak / max;
            int fade = Mathf.Min(samples.Length, SampleRate / 50);
            for (int i = 0; i < samples.Length; i++)
            {
                float tail = i >= samples.Length - fade ? (samples.Length - i) / (float)fade : 1f;
                samples[i] = Mathf.Clamp(samples[i] * gain * tail, -1f, 1f);
            }

            return samples;
        }

        static void WriteWav(string path, float[] samples)
        {
            using (var stream = new FileStream(path, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                int dataBytes = samples.Length * 2;
                writer.Write(new[] { 'R', 'I', 'F', 'F' });
                writer.Write(36 + dataBytes);
                writer.Write(new[] { 'W', 'A', 'V', 'E' });
                writer.Write(new[] { 'f', 'm', 't', ' ' });
                writer.Write(16);
                writer.Write((short)1); // PCM
                writer.Write((short)1); // mono
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { 'd', 'a', 't', 'a' });
                writer.Write(dataBytes);
                foreach (float v in samples)
                {
                    writer.Write((short)Mathf.RoundToInt(v * 32767f));
                }
            }
        }
    }
}
