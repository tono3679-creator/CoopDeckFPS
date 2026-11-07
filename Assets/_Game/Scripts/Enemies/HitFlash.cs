using CoopDeck.Combat;
using UnityEngine;

namespace CoopDeck.Enemies
{
    /// <summary>Briefly tints renderers toward white when the owner takes damage (hit confirmation).</summary>
    public class HitFlash : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] Renderer[] renderers;
        [SerializeField] Color flashColor = Color.white;
        [SerializeField, Min(0.01f)] float duration = 0.09f;

        Health health;
        Color[] baseColors;
        MaterialPropertyBlock block;
        float remaining;

        void Awake()
        {
            health = GetComponentInParent<Health>();
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
            if (remaining <= 0f)
            {
                return;
            }

            remaining -= Time.deltaTime;
            if (remaining <= 0f)
            {
                ClearFlash();
                return;
            }

            float t = remaining / duration;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                {
                    continue;
                }

                block.SetColor(BaseColorId, Color.Lerp(baseColors[i], flashColor, t * 0.85f));
                renderers[i].SetPropertyBlock(block);
            }
        }

        void ClearFlash()
        {
            remaining = 0f;
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
