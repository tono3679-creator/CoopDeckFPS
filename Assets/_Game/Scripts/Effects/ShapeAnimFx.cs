using System;
using UnityEngine;

namespace CoopDeck.Effects
{
    /// <summary>
    /// Animates an effect's mesh layers (a barrier bubble, a ground sigil, a vortex shell) where particles cannot:
    /// each track drives one shader float (through a property block, so the shared material stays untouched) and the
    /// layer's scale along curves in seconds. The intro curves run from spawn; <see cref="Release"/> (or the automatic
    /// release time) switches to the outro curves, which start from the moment of release.
    /// </summary>
    public class ShapeAnimFx : MonoBehaviour
    {
        [Serializable]
        public class Track
        {
            public Renderer renderer;
            public string property = "_Reveal";
            [Tooltip("Value over seconds after spawn (holds the last key).")]
            public AnimationCurve intro = AnimationCurve.Constant(0f, 1f, 1f);
            [Tooltip("Value over seconds after release.")]
            public AnimationCurve outro = AnimationCurve.Constant(0f, 1f, 1f);
            [Tooltip("Uniform scale over seconds after spawn; empty = keep the authored scale.")]
            public AnimationCurve scaleIntro = new AnimationCurve();
            public AnimationCurve scaleOutro = new AnimationCurve();
            [NonSerialized] public Vector3 baseScale;
            [NonSerialized] public int id;
        }

        [SerializeField] Track[] tracks = new Track[0];
        [Tooltip("Seconds after spawn to release on its own (0 = only when Release is called).")]
        [SerializeField, Min(0f)] float autoRelease;

        MaterialPropertyBlock block;
        float age;
        float releasedAt = -1f;
        bool prepared;

        public bool Released => releasedAt >= 0f;
        public float Age => age;

        public void Configure(Track[] layerTracks, float releaseAfter)
        {
            tracks = layerTracks ?? new Track[0];
            autoRelease = releaseAfter;
            prepared = false;
        }

        void Prepare()
        {
            if (prepared)
            {
                return;
            }

            prepared = true;
            block ??= new MaterialPropertyBlock();
            foreach (Track t in tracks)
            {
                if (t.renderer != null)
                {
                    t.baseScale = t.renderer.transform.localScale;
                    t.id = Shader.PropertyToID(t.property);
                }
            }
        }

        void OnEnable()
        {
            Prepare();
            age = 0f;
            releasedAt = -1f;
            Apply();
        }

        public void Release()
        {
            if (releasedAt < 0f)
            {
                releasedAt = age;
            }
        }

        void Update()
        {
            age += Time.deltaTime;
            if (autoRelease > 0f && releasedAt < 0f && age >= autoRelease)
            {
                Release();
            }

            Apply();
        }

        void Apply()
        {
            float sinceRelease = releasedAt >= 0f ? age - releasedAt : -1f;
            foreach (Track t in tracks)
            {
                if (t.renderer == null)
                {
                    continue;
                }

                float value = sinceRelease >= 0f ? t.outro.Evaluate(sinceRelease) : t.intro.Evaluate(age);
                t.renderer.GetPropertyBlock(block);
                block.SetFloat(t.id, value);
                t.renderer.SetPropertyBlock(block);

                AnimationCurve scale = sinceRelease >= 0f && t.scaleOutro.length > 0 ? t.scaleOutro : t.scaleIntro;
                if (scale.length > 0)
                {
                    float s = scale.Evaluate(sinceRelease >= 0f && t.scaleOutro.length > 0 ? sinceRelease : age);
                    t.renderer.transform.localScale = t.baseScale * s;
                }
            }
        }
    }
}
