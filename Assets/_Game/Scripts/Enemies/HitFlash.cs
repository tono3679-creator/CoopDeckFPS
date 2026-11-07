using CoopDeck.Combat;
using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>
    /// Briefly tints renderers toward white when the owner takes damage (hit confirmation), and
    /// frosts them icy blue while the Ice element is on the owner (frozen read at a glance).
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly Color FrostColor = new Color(0.62f, 0.88f, 1f);
        const float FrostStrength = 0.65f;

        [SerializeField] Renderer[] renderers;
        [SerializeField] Color flashColor = Color.white;
        [SerializeField, Min(0.01f)] float duration = 0.09f;

        Health health;
        CombatStatus status;
        Color[] baseColors;
        MaterialPropertyBlock block;
        float remaining;
        float frost;
        bool applied;

        /// <summary>0..1 how frosted the body currently looks.</summary>
        public float Frost => frost;

        void Awake()
        {
            health = GetComponentInParent<Health>();
            status = GetComponentInParent<CombatStatus>();
            if (renderers == null || renderers.Length == 0)
            {
                renderers = GetComponentsInChildren<Renderer>(true);
            }

            block = new MaterialPropertyBlock();
            baseColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                Material mat = renderers[i].sharedMaterial;
                baseColors[i] = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
            }
        }

        void OnEnable()
        {
            if (health != null)
            {
                health.Damaged += OnDamaged;
            }
        }

        void OnDisable()
        {
            if (health != null)
            {
                health.Damaged -= OnDamaged;
            }

            ClearFlash();
        }

        void OnDamaged(DamageInfo info, DamageResult result)
        {
            remaining = duration;
        }

        void Update()
        {
            // Frozen look: the body turns icy blue while the Ice element sits on it.
            float frostTarget = status != null && status.Elements.Has(Element.Ice) ? FrostStrength : 0f;
            frost = Mathf.MoveTowards(frost, frostTarget, Time.deltaTime * 6f);

            if (remaining > 0f)
            {
                remaining = Mathf.Max(0f, remaining - Time.deltaTime);
            }

            if (remaining <= 0f && frost <= 0.001f)
            {
                if (applied)
                {
                    ClearFlash();
                }

                return;
            }

            float t = duration > 0f ? remaining / duration : 0f;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                Color tinted = Color.Lerp(baseColors[i], FrostColor, frost);
                block.SetColor(BaseColorId, Color.Lerp(tinted, flashColor, t * 0.85f));
                renderers[i].SetPropertyBlock(block);
            }

            applied = true;
        }

        void ClearFlash()
        {
            remaining = 0f;
            applied = false;
            if (renderers == null)
            {
                return;
            }

            foreach (Renderer r in renderers)
            {
                if (r != null)
                {
                    // Removing the block puts the renderer back on the SRP Batcher path.
                    r.SetPropertyBlock(null);
                }
            }
        }
    }
}
