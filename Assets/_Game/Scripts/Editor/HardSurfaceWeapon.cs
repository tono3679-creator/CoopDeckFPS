using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// Shared Unity side of a weapon rebuilt with ArtSource/Tools/rebuild_weapon_hq.py (Docs/WEAPON_VISUAL_QUALITY.md):
    /// the linear data maps, the CoopDeck/Weapon/HardSurfaceToon materials (third person and a thin-outline first-person
    /// one) and the LOD switch. Used by <see cref="FireRifleAssetFactory"/> and LightningSMGAssetFactory.
    /// </summary>
    public static class HardSurfaceWeapon
    {
        public const string ShaderPath = GamePaths.Root + "/Art/Weapons/Shaders/WPN_HardSurfaceToon.shader";

        /// <summary>The texture paths of one weapon.</summary>
        public sealed class Maps
        {
            public string BaseColor;
            public string Normal;
            public string Emission;
            /// <summary>R white paint, G coloured paint, B dark polymer, A bare metal; the rest is the accent (linear).</summary>
            public string MaterialMask;
            /// <summary>R convex edges, G ambient occlusion, B concave creases (linear).</summary>
            public string Surface;
        }

        /// <summary>
        /// Per material class: x smoothness, y metallic, z specular strength, w edge highlight. The coloured paint and
        /// the accent are whatever hue ranges the mask was baked with (Fire Rifle: red paint and gold; Lightning SMG:
        /// yellow-orange paint and the cyan energy glass).
        /// </summary>
        public sealed class Look
        {
            public Vector4 WhitePaint = new Vector4(0.5f, 0f, 0.5f, 0.8f);
            public Vector4 ColouredPaint = new Vector4(0.55f, 0f, 0.55f, 0.85f);
            public Vector4 Polymer = new Vector4(0.42f, 0f, 0.35f, 0.8f);
            public Vector4 Metal = new Vector4(0.7f, 0.9f, 1f, 1f);
            public Vector4 Accent = new Vector4(0.62f, 0.55f, 0.7f, 0.7f);
            public Color EmissionColor = new Color(2f, 2f, 2f, 1f);
        }

        /// <summary>The outline and the camera-relative highlight of one material (first person or third person).</summary>
        public struct View
        {
            public float OutlineWidth;
            public float OutlineMinPixels;
            public float OutlineMaxPixels;
            public float Highlight;

            // First person: the 0.7 mm outline is about one pixel at 1080p, which broke up into dots; keep 1.4 to 2.5 px,
            // and add a camera-relative highlight so the gloss reads whatever the sun does.
            public static View FirstPerson => new View { OutlineWidth = 0.0007f, OutlineMinPixels = 1.4f, OutlineMaxPixels = 2.5f, Highlight = 0.6f };
            public static View ThirdPerson => new View { OutlineWidth = 0.003f, OutlineMinPixels = 1f, OutlineMaxPixels = 4f, Highlight = 0f };
        }

        /// <summary>Linear data maps (surface map, material mask): no sRGB, high-quality compression (BC7).</summary>
        public static void ConfigureDataTexture(string path, int maxSize, bool alpha)
        {
            if (!(AssetImporter.GetAtPath(path) is TextureImporter importer))
            {
                throw new FileNotFoundException(path + " is missing: run ArtSource/Tools/rebuild_weapon_hq.py (Docs/WEAPON_VISUAL_QUALITY.md)");
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = alpha ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = maxSize;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }

        /// <summary>
        /// The material at <paramref name="path"/> on the hard-surface shader with the weapon's maps. Tuned values are kept
        /// on reruns; moving a material onto the shader (first run, or an old CoopDeck/Character/Toon material) applies the
        /// starting look once. Shader, textures and features are enforced every time.
        /// </summary>
        public static Material EnsureMaterial(string path, Maps maps, Look look, View view)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                throw new FileNotFoundException(ShaderPath + " is missing");
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool fresh = material == null;
            if (fresh)
            {
                EditorUtil.EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }

            if (fresh || material.shader != shader)
            {
                material.shader = shader;
                ApplyStartingLook(material, look, view);
            }

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maps.BaseColor));
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maps.Emission));
            material.SetFloat("_UseEmission", 1f);
            material.EnableKeyword("_EMISSION");
            material.SetFloat("_OutlineUV3Normals", 1f);
            material.EnableKeyword("_OUTLINE_UV3_NORMALS");
            material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maps.Normal));
            material.SetFloat("_UseNormalMap", 1f);
            material.EnableKeyword("_NORMALMAP");
            material.SetTexture("_MaterialMask", AssetDatabase.LoadAssetAtPath<Texture2D>(maps.MaterialMask));
            material.SetTexture("_SurfaceMap", AssetDatabase.LoadAssetAtPath<Texture2D>(maps.Surface));
            material.SetFloat("_UseSurfaceMaps", 1f);
            material.EnableKeyword("_SURFACE_MAPS");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>
        /// The starting look: the toon's two tones with a gentle gradient on the lit side, per-material gloss, edge
        /// highlights and panel-gap occlusion from the sculpt, and an outline kept between a minimum and maximum pixel
        /// width. Values from the comparison captures (Docs/WEAPON_VISUAL_QUALITY.md).
        /// </summary>
        static void ApplyStartingLook(Material m, Look look, View view)
        {
            m.SetFloat("_ShadeThreshold", 0f);
            m.SetFloat("_ShadeSoftness", 0.12f);
            m.SetFloat("_AmbientStrength", 0.6f);
            m.SetFloat("_MaxLightIntensity", 1.2f);
            m.SetFloat("_DiffuseWrap", 0.55f);
            m.SetFloat("_RimStrength", 0.18f);
            m.SetFloat("_BumpScale", 1f);
            m.SetVector("_WhitePaintParams", look.WhitePaint);
            m.SetVector("_RedPaintParams", look.ColouredPaint);
            m.SetVector("_PolymerParams", look.Polymer);
            m.SetVector("_MetalParams", look.Metal);
            m.SetVector("_AccentParams", look.Accent);
            m.SetFloat("_EdgeLift", 0.35f);
            m.SetColor("_EdgeTint", new Color(1f, 0.93f, 0.85f, 1f));
            m.SetFloat("_OcclusionStrength", 1f);
            m.SetFloat("_CreaseDarkening", 0.5f);
            m.SetFloat("_SpecularThreshold", 0.35f);
            m.SetFloat("_SpecularSoftness", 0.25f);
            m.SetFloat("_PaintFresnel", 0.35f);
            m.SetFloat("_PaintReflectance", 0.08f);
            m.SetVector("_ViewLightDirection", new Vector4(-0.35f, 0.8f, 0.45f, 0f));
            m.SetFloat("_ViewLightPower", 10f);
            m.SetFloat("_ViewLightSpecular", view.Highlight);
            m.SetFloat("_OutlineWidth", view.OutlineWidth);
            m.SetFloat("_OutlineMinPixels", view.OutlineMinPixels);
            m.SetFloat("_OutlineMaxPixels", view.OutlineMaxPixels);
            m.SetColor("_EmissionColor", look.EmissionColor);
        }

        /// <summary>LOD0 while the weapon fills more than <paramref name="lod0"/> of the screen height (first person always does).</summary>
        public static void SetLodHeights(GameObject instance, float lod0 = 0.12f, float lod1 = 0.004f)
        {
            if (!instance.TryGetComponent(out LODGroup group))
            {
                return;
            }

            LOD[] lods = group.GetLODs();
            if (lods.Length == 2)
            {
                lods[0].screenRelativeTransitionHeight = lod0;
                lods[1].screenRelativeTransitionHeight = lod1;
                group.SetLODs(lods);
            }
        }
    }
}
