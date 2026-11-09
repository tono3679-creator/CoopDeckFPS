using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CoopDeck.Cards;
using CoopDeck.Core;
using CoopDeck.Effects;
using CoopDeck.Skills;
using UnityEditor;
using UnityEngine;
using static CoopDeck.EditorTools.VfxKit;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// A dedicated effect for every starter card at the fireball's quality bar (Docs/WEAPON_SKILL_VFX.md), replacing the
    /// shared per-element beam / burst / area / aura prefabs. Fire and gravity use the fireball's graphs in their
    /// palette tones, lightning the lightning graphs, and three new graphs (ElementVfx.hlsl) draw what those cannot:
    /// a barrier skin, flat sigils (heal circle, reticle, strike warning, vortex) and faceted ice.
    /// Each card's prefabs live in Prefabs/Skills/&lt;Card&gt;/ and are assigned to the card here (the fireball keeps its own).
    /// Layout conventions (from <c>SkillCaster</c>): area effects stand upright at the ground point and are sized by
    /// <see cref="RadiusScaledFx"/> (authored for 1 m); impacts appear at the target's chest; auras are parented to the
    /// ally at its feet; beams go from the casting hand to the hit.
    /// </summary>
    public static class SkillVfxFactory
    {
        const string Root_ = GamePaths.Root;
        public const string ShaderFolder = Root_ + "/Art/VFX/Skills/Shaders";
        public const string MaterialFolder = Root_ + "/Art/VFX/Skills/Materials";
        public const string MeshFolder = Root_ + "/Art/VFX/Skills/Meshes";
        public const string PrefabFolder = Root_ + "/Prefabs/Skills";
        public const string HlslPath = ShaderFolder + "/ElementVfx.hlsl";
        const string CardFolder = Root_ + "/ScriptableObjects/Cards";

        /// <summary>The mark's colour: harm, not an element (Palette.Danger).</summary>
        static readonly ElementTones DangerTones = new ElementTones(Palette.Hex(0xFFE3D9), Palette.Danger, Palette.Hex(0x8A1A10));

        /// <summary>The void at a singularity's heart: near black with the gravity violet at its edge.</summary>
        static readonly ElementTones VoidTones = new ElementTones(Palette.Gravity.Main, Palette.Gravity.Deep, Palette.Hex(0x14072B));

        // ================================================================== entry

        public static Dictionary<string, Shader> EnsureShaders()
        {
            AssetDatabase.ImportAsset(HlslPath, ImportAssetOptions.ForceSynchronousImport);
            string hlsl = AssetDatabase.AssetPathToGUID(HlslPath);
            if (string.IsNullOrEmpty(hlsl))
            {
                throw new InvalidOperationException(HlslPath + " is missing");
            }

            const ShaderGraphWriter.Input uv = ShaderGraphWriter.Input.UV0;
            const ShaderGraphWriter.Input color = ShaderGraphWriter.Input.VertexColor;
            const ShaderGraphWriter.Input posOS = ShaderGraphWriter.Input.PositionOS;
            const ShaderGraphWriter.Input posWS = ShaderGraphWriter.Input.PositionWS;
            const ShaderGraphWriter.Input normalWS = ShaderGraphWriter.Input.NormalWS;
            const ShaderGraphWriter.Input view = ShaderGraphWriter.Input.ViewDirWS;
            var shaders = new Dictionary<string, Shader>
            {
                ["SG_FX_ElementShell"] = WriteGraph("SG_FX_ElementShell", ShaderGraphWriter.Blend.Additive, ShaderGraphWriter.Faces.Both,
                    w => w.FragmentFunction(hlsl, "ElementShell", ("PositionOS", posOS), ("PositionWS", posWS), ("NormalWS", normalWS), ("ViewDirWS", view),
                        ("UV", uv), ("VertexColor", color))),
                ["SG_FX_ElementSigil"] = WriteGraph("SG_FX_ElementSigil", ShaderGraphWriter.Blend.Additive, ShaderGraphWriter.Faces.Both,
                    w => w.FragmentFunction(hlsl, "ElementSigil", ("PositionWS", posWS), ("UV", uv), ("VertexColor", color))),
                ["SG_FX_ElementCrystal"] = WriteGraph("SG_FX_ElementCrystal", ShaderGraphWriter.Blend.Alpha, ShaderGraphWriter.Faces.Front,
                    w => w.FragmentFunction(hlsl, "ElementCrystal", ("PositionOS", posOS), ("PositionWS", posWS), ("ViewDirWS", view), ("UV", uv),
                        ("VertexColor", color))),
            };
            return shaders;
        }

        static Shader WriteGraph(string name, ShaderGraphWriter.Blend blend, ShaderGraphWriter.Faces faces, Action<ShaderGraphWriter> functions)
        {
            var w = new ShaderGraphWriter(name) { BlendMode = blend, RenderFace = faces };
            ElementTones n = Palette.Neutral;
            w.ColorProperty("Core Color", "_CoreColor", n.Core);
            w.ColorProperty("Hot Color", "_HotColor", Color.Lerp(n.Main, n.Core, 0.55f));
            w.ColorProperty("Mid Color", "_MidColor", n.Main);
            w.ColorProperty("Deep Color", "_DeepColor", n.Deep);
            w.FloatProperty("Intensity", "_Intensity", 1f, 0f, 6f);
            w.FloatProperty("Core Boost", "_CoreBoost", 2.5f, 1f, 8f);
            w.FloatProperty("Mode", "_Mode", 0f, 0f, 3f);
            w.FloatProperty("Opacity", "_Opacity", 1f, 0f, 1f);
            w.FloatProperty("Dissolve", "_Dissolve", 0f, 0f, 1.2f);
            w.FloatProperty("Reveal", "_Reveal", 1f, 0f, 1f);
            w.FloatProperty("Spin", "_Spin", 0.6f, -12f, 12f);
            w.FloatProperty("Segments", "_Segments", 12f, 1f, 48f);
            w.FloatProperty("Noise Scale", "_NoiseScale", 3f, 0.1f, 20f);
            w.FloatProperty("Flow Speed", "_FlowSpeed", 2f, 0f, 20f);
            w.FloatProperty("Rim Power", "_RimPower", 2.5f, 0.2f, 8f);
            w.FloatProperty("Edge Softness", "_EdgeSoftness", 0.1f, 0.01f, 0.5f);
            w.FloatProperty("Seed", "_Seed", 0f, 0f, 100f, slider: false);
            w.FloatProperty("Near Fade", "_NearFade", 0f, 0f, 4f);
            w.FloatProperty("Age Fade", "_AgeFade", 1f, 0f, 2f);
            w.Begin();
            functions(w);

            EditorUtil.EnsureFolder(ShaderFolder);
            string path = $"{ShaderFolder}/{name}.shadergraph";
            string text = w.Build();
            if (!File.Exists(path) || File.ReadAllText(path) != text)
            {
                File.WriteAllText(path, text);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            }

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null)
            {
                throw new InvalidOperationException(path + " did not import as a shader");
            }

            return shader;
        }

        static Material ElemMat(string name, Shader shader, ElementTones tones, Action<Material> configure)
        {
            Material m = LoadOrCreate(MaterialFolder, name, shader);
            m.SetColor("_CoreColor", tones.Core);
            m.SetColor("_HotColor", Color.Lerp(tones.Main, tones.Core, 0.55f));
            m.SetColor("_MidColor", tones.Main);
            m.SetColor("_DeepColor", tones.Deep);
            Set(m, ("_Intensity", 1f), ("_CoreBoost", 2.5f), ("_Mode", 0f), ("_Opacity", 1f), ("_Dissolve", 0f), ("_Reveal", 1f), ("_Spin", 0.6f),
                ("_Segments", 12f), ("_NoiseScale", 3f), ("_FlowSpeed", 2f), ("_RimPower", 2.5f), ("_EdgeSoftness", 0.1f), ("_Seed", 0f),
                ("_NearFade", 0f), ("_AgeFade", 1f));
            configure?.Invoke(m);
            m.enableInstancing = false;
            m.SetFloat("_QueueControl", 1f);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Builds every card's effects and assigns them (bootstrap, after the cards, the fireball and the lightning).</summary>
        public static void EnsureAll()
        {
            Dictionary<string, Shader> s = EnsureShaders();
            var k = new Kit(s);
            // At the palm, 0.4 m from the eye: materials without the near fade the world-space effects use.
            GameObject fireHand = HandBurst("FX_Cast_Fire", k.Hand("Fire", Palette.Fire), Palette.Fire.Main);
            GameObject iceHand = HandBurst("FX_Cast_Ice", k.Hand("Ice", Palette.Ice), Palette.Ice.Main);
            GameObject boltHand = HandBurst("FX_Cast_Lightning", k.Hand("Lightning", Palette.Lightning), Palette.Lightning.Main);
            GameObject gravityHand = HandBurst("FX_Cast_Gravity", k.Hand("Gravity", Palette.Gravity), Palette.Gravity.Main);
            GameObject supportHand = HandBurst("FX_Cast_Support", k.Hand("Support", Palette.Neutral), Palette.Neutral.Main);
            GameObject markHand = HandBurst("FX_Cast_Mark", k.Hand("Mark", DangerTones), Palette.Danger);
            Assign("CD_FlameVortex", FlameVortex(k), fireHand);
            Assign("CD_Incendiary", Incendiary(k), fireHand);
            Assign("CD_MeteorStrike", Meteor(k), fireHand);
            Assign("CD_IceLance", IceLance(k), iceHand);
            Assign("CD_FrostNova", FrostNova(k), iceHand);
            Assign("CD_ChainLightning", ChainLightning(k), boltHand);
            Assign("CD_ThunderCharge", ThunderCharge(k), boltHand);
            Assign("CD_GravitySpike", GravitySpike(k), gravityHand);
            Assign("CD_GravityCollapse", GravityCollapse(k), gravityHand);
            Assign("CD_Shield", Shield(k), supportHand);
            Assign("CD_AreaHeal", AreaHeal(k), supportHand);
            Assign("CD_MarkTarget", Mark(k), markHand);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The prefabs one card plays; null clears the slot (no stray generic effect).</summary>
        public sealed class CardFx
        {
            public GameObject Beam, Impact, Area, Aura, Telegraph;
            public bool AttachImpact;
        }

        static void Assign(string cardFile, CardFx fx, GameObject cast = null)
        {
            var card = AssetDatabase.LoadAssetAtPath<CardDefinition>($"{CardFolder}/{cardFile}.asset");
            if (card == null)
            {
                Debug.LogWarning($"[CoopDeck] {cardFile} missing: its effects were built but not assigned.");
                return;
            }

            // Every area and warning here is built standing on the ground: the caster drops it onto the ground under the hit.
            MarkGrounded(fx.Area);
            MarkGrounded(fx.Telegraph);
            card.beamFx = fx.Beam;
            card.impactFx = fx.Impact;
            card.areaFx = fx.Area;
            card.auraFx = fx.Aura;
            card.telegraphFx = fx.Telegraph;
            card.attachImpactFx = fx.AttachImpact;
            if (!card.projectile.IsProjectile)
            {
                card.castFx = cast; // the burst at the casting palm (the fireball keeps its own charge)
            }

            EditorUtility.SetDirty(card);
        }

        /// <summary>Adds <see cref="GroundedFx"/> to a saved prefab's root (built standing on the ground at its origin).</summary>
        static void MarkGrounded(GameObject prefab, float maxDrop = 3f)
        {
            if (prefab == null)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(prefab);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                GroundedFx grounded = root.GetComponent<GroundedFx>();
                if (grounded == null)
                {
                    grounded = root.AddComponent<GroundedFx>();
                }

                grounded.Configure(maxDrop);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ================================================================== shared materials and meshes

        /// <summary>Materials and meshes the cards share.</summary>
        sealed class Kit
        {
            public readonly Dictionary<string, Shader> S;
            public readonly Mesh Sphere, Billow, Chunk, Ring, Ground, Shell, QuadXY, Shard, Spike, SpikeZ, Spear, Tube;

            public Kit(Dictionary<string, Shader> shaders)
            {
                S = shaders;
                Sphere = FireMesh("M_Fireball_Core");          // r 0.5, smooth
                Billow = FireMesh("M_Fireball_Billow");
                Chunk = FireMesh("M_Fireball_EmberChunk");
                Ring = FireMesh("M_Fireball_ShockwaveRing");   // XZ, uv.x around, uv.y inner→outer
                Ground = FireMesh("M_Fireball_ScorchQuad");    // XZ unit quad
                Shell = FireMesh("M_Fireball_FlameShell");     // +Z flight, uv.y front→tail
                QuadXY = WeaponVfxFactory.QuadXY;
                Shard = SaveMesh(MeshFolder, "M_Vfx_Shard", () => Bipyramid(6, 0.2f, 0.62f, 0.34f));
                Spike = SaveMesh(MeshFolder, "M_Vfx_Spike", () => Bipyramid(5, 0.17f, 1f, 0.06f));
                SpikeZ = SaveMesh(MeshFolder, "M_Vfx_SpikeZ", () => Rotate(Bipyramid(5, 0.15f, 1f, 0.1f), Quaternion.Euler(90f, 0f, 0f)));
                Spear = SaveMesh(MeshFolder, "M_Vfx_Spear", () => Rotate(Bipyramid(6, 0.07f, 0.55f, 0.95f), Quaternion.Euler(90f, 0f, 0f)));
                Tube = SaveMesh(MeshFolder, "M_Vfx_Tube", () => Tube_(28, 8));
            }

            // fire
            public Material FireCore => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Core", "SG_FX_FireGlow", Palette.Fire, m => Set(m, ("_Mode", 1f),
                ("_AgeHeat", 0.6f), ("_EdgeSoftness", 0.26f), ("_Intensity", 1.2f), ("_CoreBoost", 3.38f), ("_HeatBias", 0.1f), ("_NearFade", 1.2f)));
            public Material FireSpark => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Spark", "SG_FX_FireGlow", Palette.Fire, m => Set(m, ("_Mode", 1f),
                ("_AgeHeat", 1f), ("_EdgeSoftness", 0.195f), ("_Intensity", 1.1f), ("_CoreBoost", 3.12f), ("_NearFade", 0.6f)));
            public Material FireBolt => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Bolt", "SG_FX_FireGlow", Palette.Fire, m => Set(m, ("_Mode", 2f),
                ("_TrailFlip", 1f), ("_NoiseScale", 1.4f), ("_FlowSpeed", 12f), ("_EdgeSoftness", 0.078f), ("_Intensity", 1.1f), ("_CoreBoost", 3.38f), ("_HeatBias", 0.3f)));
            public Material FireTrail => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Trail", "SG_FX_FireParticle", Palette.Fire, m => Set(m, ("_Mode", 2f),
                ("_TrailFlip", 1f), ("_NoiseScale", 1.6f), ("_FlowSpeed", 6f), ("_EdgeSoftness", 0.065f), ("_CoreBoost", 2.21f), ("_NearFade", 0.8f), ("_Bands", 0.45f)));
            public Material Flame => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Flame", "SG_FX_FireParticle", Palette.Fire, m => Set(m, ("_Mode", 0f),
                ("_NoiseScale", 2.2f), ("_FlowSpeed", 3.2f), ("_AgeHeat", 0.85f), ("_EdgeSoftness", 0.058f), ("_CoreBoost", 2.47f), ("_NearFade", 1.2f),
                ("_HeatBias", 0.18f), ("_Intensity", 1.2f), ("_Bands", 0.45f)));
            public Material Billow_ => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Billow", "SG_FX_FlameVolume", Palette.Fire, m => Set(m, ("_TailFade", 0f),
                ("_Displace", 0.18f), ("_NoiseScale", 1.6f), ("_FlowSpeed", 1.6f), ("_Swirl", 1.2f), ("_RimPower", 0.9f), ("_HeatBias", 0.24f),
                ("_EdgeSoftness", 0.065f), ("_CoreBoost", 2.6f), ("_Intensity", 1.2f), ("_AgeDissolve", 1.1f), ("_NearFade", 1.4f), ("_Bands", 0.45f)));
            public Material Vortex => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Vortex", "SG_FX_FlameVolume", Palette.Fire, m => Set(m, ("_TailFade", 1f),
                ("_Displace", 0.22f), ("_NoiseScale", 1.4f), ("_FlowSpeed", -3.2f), ("_Swirl", 6f), ("_RimPower", 1.3f), ("_HeatBias", 0.46f),
                ("_EdgeSoftness", 0.052f), ("_CoreBoost", 3.2f), ("_Intensity", 1.45f), ("_Opacity", 0.95f), ("_AgeDissolve", 0f), ("_NearFade", 1.4f), ("_Bands", 0.45f)));
            public Material BlastCore => ToneMat(MaterialFolder, "M_FX_Skill_Fire_BlastCore", "SG_FX_FlameVolume", Palette.Fire, m => Set(m, ("_TailFade", 0f),
                ("_Displace", 0.2f), ("_NoiseScale", 2.2f), ("_FlowSpeed", 2.4f), ("_Swirl", 2f), ("_RimPower", 0.7f), ("_HeatBias", 0.26f),
                ("_EdgeSoftness", 0.065f), ("_CoreBoost", 3.12f), ("_AgeDissolve", 1.25f), ("_Bands", 0.45f), ("_NearFade", 1.4f)));
            public Material MeteorCore => ToneMat(MaterialFolder, "M_FX_Skill_Fire_MeteorCore", "SG_FX_FireCore", Palette.Fire, m => Set(m, ("_Mode", 0f),
                ("_Intensity", 1f), ("_CoreBoost", 3.38f), ("_NoiseScale", 3.4f), ("_FlowSpeed", 1.8f), ("_Swirl", 3.5f), ("_PulseSpeed", 12f), ("_RimPower", 1.1f),
                ("_AgeDissolve", 0f)));
            public Material MeteorShell => ToneMat(MaterialFolder, "M_FX_Skill_Fire_MeteorShell", "SG_FX_FlameVolume", Palette.Fire, m => Set(m, ("_TailFade", 1f),
                ("_Displace", 0.24f), ("_NoiseScale", 2.4f), ("_FlowSpeed", 2.8f), ("_Swirl", -3f), ("_RimPower", 1.4f), ("_HeatBias", -0.04f),
                ("_EdgeSoftness", 0.039f), ("_CoreBoost", 1.95f), ("_Opacity", 0.92f), ("_AgeDissolve", 0f), ("_Dissolve", 0.16f), ("_Bands", 0.35f)));
            public Material Ember => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Ember", "SG_FX_FireCore", Palette.Fire, m => Set(m, ("_Mode", 1f),
                ("_Intensity", 1.1f), ("_CoreBoost", 2.6f), ("_NoiseScale", 9f)));
            public Material FireShock => ToneMat(MaterialFolder, "M_FX_Skill_Fire_Shock", "SG_FX_Shockwave", Palette.Fire, m => Set(m, ("_NoiseScale", 3f),
                ("_FlowSpeed", 2f), ("_AgeDissolve", 1f), ("_EdgeSoftness", 0.065f), ("_CoreBoost", 1.95f), ("_HeatBias", -0.05f), ("_Bands", 0.45f), ("_Opacity", 0.8f)));
            public Material Smoke => ToneMat(MaterialFolder, "M_FX_Skill_Smoke", "SG_FX_Smoke", Palette.Fire, m => Set(m, ("_Mode", 0f), ("_Opacity", 0.36f),
                ("_NoiseScale", 2f), ("_FlowSpeed", 0.6f), ("_EdgeSoftness", 0.25f), ("_NearFade", 1.5f)));
            public Material Scorch => ToneMat(MaterialFolder, "M_FX_Skill_Scorch", "SG_FX_Smoke", Palette.Fire, m => Set(m, ("_Mode", 1f), ("_Opacity", 0.8f),
                ("_NoiseScale", 3.5f), ("_EdgeSoftness", 0.13f)), glow: Palette.Fire.Main);
            public Material Chip
            {
                get
                {
                    Material m = LoadOrCreate(MaterialFolder, "M_FX_Skill_Rock", Shader.Find("Universal Render Pipeline/Lit"));
                    m.SetColor("_BaseColor", Palette.Trim);
                    m.SetFloat("_Smoothness", 0.1f);
                    m.renderQueue = -1;
                    EditorUtility.SetDirty(m);
                    return m;
                }
            }

            public Material Warning => ElemMat("M_FX_Skill_Warning", S["SG_FX_ElementSigil"], DangerTones, m => Set(m, ("_Mode", 2f), ("_Intensity", 1f),
                ("_CoreBoost", 2.6f), ("_Opacity", 0.9f), ("_AgeFade", 0.2f), ("_Spin", 0.4f)));

            // ice
            public Material Crystal => ElemMat("M_FX_Skill_Ice_Crystal", S["SG_FX_ElementCrystal"], Palette.Ice, m => Set(m, ("_Intensity", 1.05f),
                ("_CoreBoost", 2.21f), ("_RimPower", 2.5f), ("_NoiseScale", 4f), ("_EdgeSoftness", 0.078f), ("_AgeFade", 1f), ("_NearFade", 0.8f), ("_Opacity", 0.95f)));
            public Material IceGlow => ToneMat(MaterialFolder, "M_FX_Skill_Ice_Glow", "SG_FX_FireGlow", Palette.Ice, m => Set(m, ("_Mode", 1f),
                ("_AgeHeat", 0.7f), ("_EdgeSoftness", 0.26f), ("_Intensity", 1.1f), ("_CoreBoost", 2.86f), ("_Bands", 0.2f), ("_NearFade", 1.2f)));
            public Material IceSpark => ToneMat(MaterialFolder, "M_FX_Skill_Ice_Spark", "SG_FX_FireGlow", Palette.Ice, m => Set(m, ("_Mode", 1f),
                ("_AgeHeat", 0.9f), ("_EdgeSoftness", 0.195f), ("_Intensity", 1.1f), ("_CoreBoost", 2.86f), ("_Bands", 0.2f), ("_NearFade", 0.6f)));
            public Material IceTrail => ToneMat(MaterialFolder, "M_FX_Skill_Ice_Trail", "SG_FX_FireParticle", Palette.Ice, m => Set(m, ("_Mode", 2f),
                ("_TrailFlip", 1f), ("_NoiseScale", 1.6f), ("_FlowSpeed", 5f), ("_EdgeSoftness", 0.078f), ("_CoreBoost", 2.34f), ("_Bands", 0.2f), ("_Opacity", 0.85f)));
            public Material Mist => ToneMat(MaterialFolder, "M_FX_Skill_Ice_Mist", "SG_FX_Smoke", Palette.Ice, m => Set(m, ("_Mode", 0f), ("_Opacity", 0.36f),
                ("_NoiseScale", 2f), ("_FlowSpeed", 0.5f), ("_EdgeSoftness", 0.3f), ("_NearFade", 3f)),
                smoke: Color.Lerp(Palette.Ice.Main, Color.white, 0.55f), glow: Palette.Ice.Core);
            public Material Frost => ToneMat(MaterialFolder, "M_FX_Skill_Ice_Frost", "SG_FX_Smoke", Palette.Ice, m => Set(m, ("_Mode", 1f), ("_Opacity", 0.4f),
                ("_NoiseScale", 5f), ("_EdgeSoftness", 0.098f)), smoke: Color.Lerp(Palette.Ice.Main, Color.white, 0.35f), glow: Palette.Ice.Core);
            public Material IceShock => ToneMat(MaterialFolder, "M_FX_Skill_Ice_Shock", "SG_FX_Shockwave", Palette.Ice, m => Set(m, ("_NoiseScale", 3.5f),
                ("_FlowSpeed", 1.5f), ("_AgeDissolve", 0.9f), ("_EdgeSoftness", 0.065f), ("_CoreBoost", 1.82f), ("_HeatBias", -0.05f), ("_Bands", 0.2f), ("_Opacity", 0.8f)));

            // lightning (the card's yellow and violet, Palette.Lightning)
            static Color LCore => Palette.Lightning.Core;
            static Color LGlow => Palette.Lightning.Main * 1.3f;
            static Color LEdge => Palette.Lightning.Deep;
            public Material LBolt => BoltMat(MaterialFolder, "M_FX_Skill_Lightning_Bolt", "SG_FX_LightningBolt", LCore, LGlow, LEdge, m => Set(m,
                ("_Intensity", 1f), ("_CoreBoost", 3.9f), ("_CoreWidth", 0.12f), ("_GlowFalloff", 1.6f), ("_HaloOpacity", 0.9f), ("_NoiseScale", 6f),
                ("_FlowSpeed", 9f), ("_PulseCount", 1.5f), ("_PulseSpeed", 5f), ("_Seed", 0f)));
            public Material LBranch => BoltMat(MaterialFolder, "M_FX_Skill_Lightning_Branch", "SG_FX_LightningBolt", LCore, LGlow, LEdge, m => Set(m,
                ("_Intensity", 0.95f), ("_CoreBoost", 3.38f), ("_CoreWidth", 0.15f), ("_GlowFalloff", 1.8f), ("_HaloOpacity", 0.85f), ("_NoiseScale", 8f),
                ("_FlowSpeed", 12f), ("_PulseCount", 0f), ("_PulseSpeed", 0f), ("_Seed", 7f)));
            public Material LFlash => BoltMat(MaterialFolder, "M_FX_Skill_Lightning_Flash", "SG_FX_LightningFlash", LCore, LGlow * 1.15f, LEdge, m => Set(m,
                ("_Mode", 0f), ("_Intensity", 1f), ("_CoreBoost", 5.85f), ("_GlowFalloff", 12f), ("_Rays", 7f), ("_HaloOpacity", 1f)));
            public Material LRing => BoltMat(MaterialFolder, "M_FX_Skill_Lightning_Ring", "SG_FX_LightningFlash", LCore, LGlow * 1.4f, LEdge, m => Set(m,
                ("_Mode", 1f), ("_Intensity", 1f), ("_CoreBoost", 3.38f), ("_GlowFalloff", 4f), ("_HaloOpacity", 1f)));
            public Material LGlowMat => BoltMat(MaterialFolder, "M_FX_Skill_Lightning_Glow", "SG_FX_LightningFlash", LCore, LGlow, LEdge, m => Set(m,
                ("_Mode", 2f), ("_Intensity", 1f), ("_CoreBoost", 1.3f), ("_GlowFalloff", 3f), ("_HaloOpacity", 0.4f)));
            public Material LSpark => ToneMat(MaterialFolder, "M_FX_Skill_Lightning_Spark", "SG_FX_FireGlow", Palette.Lightning, m => Set(m, ("_Mode", 1f),
                ("_AgeHeat", 0.9f), ("_EdgeSoftness", 0.195f), ("_Intensity", 1.1f), ("_CoreBoost", 2.86f), ("_Bands", 0.2f), ("_NearFade", 0.5f)));

            // gravity
            public Material GBeam => BoltMat(MaterialFolder, "M_FX_Skill_Gravity_Beam", "SG_FX_LightningBolt", Palette.Gravity.Core, Palette.Gravity.Main,
                Palette.Gravity.Deep, m => Set(m, ("_Intensity", 1f), ("_CoreBoost", 3.12f), ("_CoreWidth", 0.14f), ("_GlowFalloff", 1.6f), ("_HaloOpacity", 0.9f),
                    ("_NoiseScale", 5f), ("_FlowSpeed", -8f), ("_PulseCount", 3f), ("_PulseSpeed", -6f), ("_Seed", 4f)));
            public Material GRing => BoltMat(MaterialFolder, "M_FX_Skill_Gravity_Ring", "SG_FX_LightningFlash", Palette.Gravity.Core, Palette.Gravity.Main * 1.2f,
                Palette.Gravity.Deep, m => Set(m, ("_Mode", 1f), ("_Intensity", 1f), ("_CoreBoost", 2.86f), ("_GlowFalloff", 4f), ("_HaloOpacity", 1f)));
            public Material GFlash => BoltMat(MaterialFolder, "M_FX_Skill_Gravity_Flash", "SG_FX_LightningFlash", Palette.Gravity.Core, Palette.Gravity.Main * 1.2f,
                Palette.Gravity.Deep, m => Set(m, ("_Mode", 0f), ("_Intensity", 1f), ("_CoreBoost", 4.55f), ("_GlowFalloff", 10f), ("_Rays", 5f), ("_HaloOpacity", 1f)));
            public Material GSpark => ToneMat(MaterialFolder, "M_FX_Skill_Gravity_Spark", "SG_FX_FireGlow", Palette.Gravity, m => Set(m, ("_Mode", 1f),
                ("_AgeHeat", 0.5f), ("_EdgeSoftness", 0.195f), ("_Intensity", 1.1f), ("_CoreBoost", 2.6f), ("_Bands", 0.2f), ("_NearFade", 0.6f)));
            public Material Void => ToneMat(MaterialFolder, "M_FX_Skill_Gravity_Void", "SG_FX_Smoke", VoidTones, m => Set(m, ("_Mode", 0f), ("_Opacity", 0.96f),
                ("_NoiseScale", 1.6f), ("_FlowSpeed", 1.4f), ("_EdgeSoftness", 0.078f), ("_NearFade", 1f)),
                smoke: Palette.Hex(0x0C0418), glow: Palette.Gravity.Main);
            public Material Vortex2 => ElemMat("M_FX_Skill_Gravity_Vortex", S["SG_FX_ElementSigil"], Palette.Gravity, m => Set(m, ("_Mode", 3f),
                ("_Intensity", 1f), ("_CoreBoost", 2.6f), ("_Spin", 1.2f), ("_NoiseScale", 3f), ("_Opacity", 0.9f), ("_AgeFade", 1f)));
            public Material GCrystal => ElemMat("M_FX_Skill_Gravity_Crystal", S["SG_FX_ElementCrystal"], Palette.Gravity, m => Set(m, ("_Intensity", 1f),
                ("_CoreBoost", 2.86f), ("_RimPower", 1.6f), ("_NoiseScale", 3f), ("_EdgeSoftness", 0.078f), ("_AgeFade", 1f), ("_NearFade", 0.8f)));
            public Material GShock => ToneMat(MaterialFolder, "M_FX_Skill_Gravity_Shock", "SG_FX_Shockwave", Palette.Gravity, m => Set(m, ("_NoiseScale", 3f),
                ("_FlowSpeed", 2f), ("_AgeDissolve", 1f), ("_EdgeSoftness", 0.065f), ("_CoreBoost", 1.69f), ("_HeatBias", -0.15f), ("_Bands", 0.2f), ("_Opacity", 0.8f)));

            // support (neutral) and mark
            public Material Barrier => ElemMat("M_FX_Skill_Shield_Barrier", S["SG_FX_ElementShell"], Palette.Neutral, m => Set(m, ("_Intensity", 1f),
                ("_CoreBoost", 2.6f), ("_RimPower", 2.2f), ("_Segments", 9f), ("_NoiseScale", 3f), ("_FlowSpeed", 3f), ("_Opacity", 0.8f), ("_NearFade", 2.4f),
                ("_Reveal", 1f)));
            public Material HealCircle => ElemMat("M_FX_Skill_Heal_Circle", S["SG_FX_ElementSigil"], Palette.Neutral, m => Set(m, ("_Mode", 0f),
                ("_Intensity", 1f), ("_CoreBoost", 2.34f), ("_Spin", 0.5f), ("_Segments", 24f), ("_Opacity", 0.75f), ("_AgeFade", 1f), ("_Reveal", 0f)));
            public Material NGlow => ToneMat(MaterialFolder, "M_FX_Skill_Neutral_Glow", "SG_FX_FireGlow", Palette.Neutral, m => Set(m, ("_Mode", 1f),
                ("_AgeHeat", 0.6f), ("_EdgeSoftness", 0.227f), ("_Intensity", 1.05f), ("_CoreBoost", 2.6f), ("_Bands", 0.2f), ("_NearFade", 0.9f)));
            public Material NShock => ToneMat(MaterialFolder, "M_FX_Skill_Neutral_Shock", "SG_FX_Shockwave", Palette.Neutral, m => Set(m, ("_NoiseScale", 3f),
                ("_FlowSpeed", 1.5f), ("_AgeDissolve", 0.95f), ("_EdgeSoftness", 0.065f), ("_CoreBoost", 1.82f), ("_HeatBias", -0.05f), ("_Bands", 0.2f), ("_Opacity", 0.8f)));
            public Material Reticle => ElemMat("M_FX_Skill_Mark_Reticle", S["SG_FX_ElementSigil"], DangerTones, m => Set(m, ("_Mode", 1f), ("_Intensity", 1.25f),
                ("_CoreBoost", 1.62f), ("_Spin", 0.8f), ("_Opacity", 0.95f), ("_Reveal", 1f), ("_AgeFade", 0f)));
            public Material ReticleOpen => ElemMat("M_FX_Skill_Mark_LockOn", S["SG_FX_ElementSigil"], DangerTones, m => Set(m, ("_Mode", 1f), ("_Intensity", 1.25f),
                ("_CoreBoost", 1.62f), ("_Spin", 2f), ("_Opacity", 0.95f), ("_Reveal", 0.2f), ("_AgeFade", 0f)));
            public Material MarkBeam => BoltMat(MaterialFolder, "M_FX_Skill_Mark_Beam", "SG_FX_LightningBolt", DangerTones.Core, DangerTones.Main, DangerTones.Deep,
                m => Set(m, ("_Intensity", 0.9f), ("_CoreBoost", 2.6f), ("_CoreWidth", 0.2f), ("_GlowFalloff", 2.4f), ("_HaloOpacity", 0.7f), ("_NoiseScale", 4f),
                    ("_FlowSpeed", 12f), ("_PulseCount", 2f), ("_PulseSpeed", 10f), ("_Seed", 2f)));
            /// <summary>Glow for the casting palm: no near fade (it sits 0.4 m from the eye), modest boost.</summary>
            public Material Hand(string tag, ElementTones tones) => ToneMat(MaterialFolder, "M_FX_Skill_Hand_" + tag, "SG_FX_FireGlow", tones,
                m => Set(m, ("_Mode", 1f), ("_AgeHeat", 0.6f), ("_EdgeSoftness", 0.227f), ("_Intensity", 1f), ("_CoreBoost", 2.6f), ("_Bands", 0.2f)));

            public Material MarkGlow => ToneMat(MaterialFolder, "M_FX_Skill_Mark_Glow", "SG_FX_FireGlow", DangerTones, m => Set(m, ("_Mode", 1f),
                ("_AgeHeat", 0.7f), ("_EdgeSoftness", 0.227f), ("_Intensity", 1f), ("_CoreBoost", 2.34f), ("_Bands", 0.2f)));
        }

        // ================================================================== small builders

        /// <summary>
        /// The release at the casting palm (spawned on the hand's layer, the view model for the local player): a flash in
        /// the element's colour, a quick ring and a spray of sparks thrown forward. Small: it sits 0.4 m from the eye.
        /// </summary>
        static GameObject HandBurst(string name, Material glow, Color light)
        {
            return Save(PrefabFolder + "/Cast", name, () =>
            {
                GameObject root = Root(name, 0.45f);
                Transform t = root.transform;
                ParticleSystem core = Ps(t, "Flash", glow, (0.07f, 0.09f), (0f, 0f), (0.16f, 0.2f), 2, world: false);
                Burst(core, 1);
                Spin(core, 180f, 0f);
                SizeOverLife(core, new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.6f));
                ParticleSystem bloom = Ps(t, "Bloom", glow, (0.12f, 0.14f), (0f, 0f), (0.3f, 0.34f), 2, world: false);
                Burst(bloom, 1);
                Fade(bloom, (0f, 0f), (0.45f, 0.2f), (0f, 1f));
                ParticleSystem sparks = Ps(t, "Sparks", glow, (0.12f, 0.25f), (2f, 4.5f), (0.006f, 0.012f), 16);
                Burst(sparks, 10);
                Shape(sparks, ParticleSystemShapeType.Cone, 0.02f, 40f);
                Stretch(sparks, 0.02f, 1.2f);
                Drag(sparks, 2f);
                FlashLight(root, light, 2.5f, 0.1f, 2f, 0f);
                return root;
            });
        }

        static string Folder(string card) => $"{PrefabFolder}/{card}";

        /// <summary>Flat particles on the ground (the emitter's XZ plane), turned at random around the up axis.</summary>
        static void OnGround(ParticleSystem ps, Mesh xzMesh)
        {
            MeshParticles(ps, xzMesh, alignLocal: true, randomRotation: false);
            ParticleSystem.MainModule main = ps.main;
            main.startRotation3D = true;
            main.startRotationX = 0f;
            main.startRotationZ = 0f;
            main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        }

        /// <summary>Particles that keep their authored size while their positions follow the effect radius.</summary>
        static void ShapeOnly(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Shape;
        }

        // 2026-11-09 ('more spectacular'): the second pass scales every flash, spark burst and shake from one place.
        public const float FlashSizeScale = 1.4f;
        public const float FlashLifeScale = 1.25f;
        public const float SparkCountScale = 1.7f;
        public const float SparkSpeedScale = 1.15f;
        public const float ShakeScale = 1.5f;

        static ParticleSystem Flash(Transform t, string name, Material m, float size, float life, Vector3 at = default)
        {
            size *= FlashSizeScale;
            life *= FlashLifeScale;
            ParticleSystem ps = Ps(t, name, m, (life, life * 1.2f), (0f, 0f), (size, size * 1.15f), 2, world: false, position: at);
            Burst(ps, 1);
            Spin(ps, 180f, 0f);
            SizeOverLife(ps, new Keyframe(0f, 0.55f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.4f));
            Order(ps, -2f);
            MaxSize(ps, 2f);
            return ps;
        }

        static ParticleSystem Sparks(Transform t, string name, Material m, short count, (float, float) speed, (float, float) life, float angle,
            float gravity = 0.6f, Vector3 rotation = default, float size = 0.03f)
        {
            count = (short)Mathf.RoundToInt(count * SparkCountScale);
            speed = (speed.Item1 * SparkSpeedScale, speed.Item2 * SparkSpeedScale);
            life = (life.Item1 * 1.2f, life.Item2 * 1.2f);
            size *= 1.2f;
            ParticleSystem ps = Ps(t, name, m, life, speed, (size * 0.7f, size * 1.2f), count + 8);
            Burst(ps, count);
            Shape(ps, ParticleSystemShapeType.Cone, 0.05f, angle, rotation);
            Stretch(ps, 0.03f, 1.3f);
            Gravity(ps, gravity);
            Drag(ps, 0.6f);
            return ps;
        }

        static ParticleSystem GroundRing(Transform t, string name, Material m, Mesh ring, float from, float to, float life, float delay = 0f)
        {
            ParticleSystem ps = Ps(t, name, m, (life, life), (0f, 0f), (to, to), 2, world: false, position: new Vector3(0f, 0.04f, 0f));
            Burst(ps, 1);
            OnGround(ps, ring);
            SizeOverLife(ps, new Keyframe(0f, from / to, 0f, 3f), new Keyframe(0.45f, 0.92f), new Keyframe(1f, 1f));
            Delay(ps, delay);
            return ps;
        }

        static void Shake(GameObject root, float strength, float punch = 0f, float delay = 0f)
        {
            var shake = root.AddComponent<ImpactShakeFx>();
            shake.Configure(strength * ShakeScale, punch * ShakeScale, delay);
        }

        static ShapeAnimFx.Track Track(Renderer r, string property, AnimationCurve intro, AnimationCurve outro, AnimationCurve scaleIntro = null,
            AnimationCurve scaleOutro = null) => new ShapeAnimFx.Track
        {
            renderer = r,
            property = property,
            intro = intro,
            outro = outro,
            scaleIntro = scaleIntro ?? new AnimationCurve(),
            scaleOutro = scaleOutro ?? new AnimationCurve(),
        };

        static AnimationCurve Ramp(float t0, float v0, float t1, float v1) => new AnimationCurve(new Keyframe(t0, v0), new Keyframe(t1, v1));

        static GameObject Beam(string folder, string name, Material streak, Material wake, float speed, float length, float width, float wakeWidth,
            float fade, float jitter) => Save(folder, name, () =>
        {
            GameObject root = Root(name, 0.8f);
            LineRenderer s = Line(root.transform, "Streak", streak, width, 6);
            s.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.7f, 0.9f), new Keyframe(1f, 1f));
            s.colorGradient = Alpha((0f, 0f), (1f, 0.4f), (1f, 1f));
            s.numCapVertices = 3;
            LineRenderer w = Line(root.transform, "Wake", wake, wakeWidth, 10);
            w.widthCurve = new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(1f, 1f));
            root.AddComponent<ShotTracerFx>().Configure(s, w, speed, length, width, wakeWidth, fade, jitter);
            return root;
        });

        // ================================================================== fire

        /// <summary>
        /// 火炎旋風: a fire bolt to the spot, then a tornado - two counter-turning flame tubes that grow out of the ground,
        /// flames and embers spiralling up, a fire ring and scorch on the ground, smoke from the top.
        /// </summary>
        static CardFx FlameVortex(Kit k)
        {
            string f = Folder("FlameVortex");
            GameObject area = Save(f, "FX_FlameVortex_Area", () =>
            {
                GameObject root = Root("FX_FlameVortex_Area", 2.4f);
                Transform t = root.transform;
                Transform full = Child(t, "Full");
                Transform partial = Child(t, "Partial");

                Flash(partial, "Flash", k.FireCore, 1.3f, 0.12f, new Vector3(0f, 0.6f, 0f));
                GroundRing(full, "FireRing", k.FireShock, k.Ring, 0.15f, 1f, 0.55f);
                ParticleSystem scorch = Ps(full, "Scorch", k.Scorch, (2.4f, 2.4f), (0f, 0f), (2.1f, 2.1f), 2, world: false, position: new Vector3(0f, 0.02f, 0f));
                Burst(scorch, 1);
                OnGround(scorch, k.Ground);
                Order(scorch, 5f);

                // The tornado: two flame tubes (FlameVolume streams down the tube and twists around its axis), counter-rotating.
                var tracks = new List<ShapeAnimFx.Track>();
                for (int i = 0; i < 2; i++)
                {
                    Transform spin = Child(partial, $"Funnel{i}");
                    spin.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                    spin.gameObject.AddComponent<SpinFx>().Configure(new Vector3(0f, 0f, i == 0 ? 220f : -160f));
                    MeshRenderer tube = MeshLayer(spin, "Tube", k.Tube, k.Vortex, 1f);
                    tube.transform.localScale = new Vector3(i == 0 ? 0.55f : 0.8f, i == 0 ? 0.55f : 0.8f, i == 0 ? 2.6f : 2.1f);
                    tracks.Add(Track(tube, "_Dissolve", Ramp(0f, 0.7f, 0.25f, 0.05f), Ramp(0f, 0.05f, 0.5f, 1.1f),
                        new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.25f, 1f, 0.5f, 0f)), Ramp(0f, 1f, 0.5f, 1.2f)));
                }

                root.AddComponent<ShapeAnimFx>().Configure(tracks.ToArray(), 1.5f);

                ParticleSystem spiral = Ps(partial, "SpiralFlames", k.Flame, (0.6f, 0.9f), (0.2f, 0.6f), (0.32f, 0.5f), 70);
                Rate(spiral, 42f, 0f, 1.4f);
                Shape(spiral, ParticleSystemShapeType.Circle, 0.45f, 0f, Up, 0.2f);
                Velocity(spiral, new Vector3(0f, 2.6f, 0f), orbitalY: 4.2f, radial: 0.15f);
                SizeOverLife(spiral, new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1f), new Keyframe(1f, 0.75f));
                Spin(spiral, 30f, 60f);

                ParticleSystem embers = Ps(partial, "Embers", k.FireSpark, (0.6f, 1.1f), (0.5f, 1.5f), (0.035f, 0.06f), 60);
                Rate(embers, 34f, 0f, 1.5f);
                Shape(embers, ParticleSystemShapeType.Circle, 0.5f, 0f, Up, 0.4f);
                Velocity(embers, new Vector3(0f, 4.2f, 0f), orbitalY: 3.4f, radial: 0.4f);
                Stretch(embers, 0.04f, 1.2f);

                Transform medium = Gate(partial, "Medium", VfxQuality.Medium);
                ParticleSystem smoke = Ps(medium, "Smoke", k.Smoke, (1.2f, 1.7f), (0.2f, 0.5f), (0.8f, 1.2f), 14, position: new Vector3(0f, 2.2f, 0f));
                Rate(smoke, 6f, 0f, 1.4f);
                Shape(smoke, ParticleSystemShapeType.Circle, 0.3f, 0f, Up);
                Velocity(smoke, new Vector3(0f, 1.6f, 0f), orbitalY: 1.2f);
                SizeOverLife(smoke, new Keyframe(0f, 0.6f), new Keyframe(1f, 1.9f));
                Spin(smoke, 180f, 40f);

                root.AddComponent<RadiusScaledFx>().Configure(new[] { full }, new[] { partial }, null);
                FlashLight(root, Palette.Fire.Main, 9f, 0.6f, 5f);
                Shake(root, 0.16f);
                return root;
            });

            GameObject beam = Beam(f, "FX_FlameVortex_Beam", k.FireBolt, k.FireTrail, 140f, 3.5f, 0.16f, 0.06f, 0.18f, 0f);
            GameObject hit = AssetDatabase.LoadAssetAtPath<GameObject>(Root_ + "/Prefabs/Skills/Fireball/FX_Fireball_TargetFlare.prefab");
            return new CardFx { Beam = beam, Area = area, Impact = hit };
        }

        /// <summary>焼夷グレネード: a fire bolt, a burst of flame, then the ground burns for the burn's duration.</summary>
        static CardFx Incendiary(Kit k)
        {
            string f = Folder("Incendiary");
            float burn = BurnSeconds("CD_Incendiary", 4f);
            GameObject area = Save(f, "FX_Incendiary_Area", () =>
            {
                GameObject root = Root("FX_Incendiary_Area", burn + 0.8f);
                Transform t = root.transform;
                Transform full = Child(t, "Full");
                Transform partial = Child(t, "Partial");

                Flash(partial, "Flash", k.FireCore, 1.1f, 0.1f, new Vector3(0f, 0.4f, 0f));
                ParticleSystem billows = Ps(partial, "Burst", k.Billow_, (0.35f, 0.5f), (1.5f, 3f), (0.5f, 0.8f), 10, position: new Vector3(0f, 0.3f, 0f));
                Burst(billows, 6);
                Shape(billows, ParticleSystemShapeType.Hemisphere, 0.25f, 0f, Up);
                MeshParticles(billows, k.Billow, alignLocal: false, randomRotation: true);
                Drag(billows, 3f);
                SizeOverLife(billows, new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1.1f), new Keyframe(1f, 1.3f));
                Sparks(partial, "Sparks", k.FireSpark, 18, (4f, 9f), (0.3f, 0.6f), 70f, 0.8f, Up, 0.04f);
                GroundRing(full, "Ring", k.FireShock, k.Ring, 0.15f, 1f, 0.45f);

                // The burning ground: flames all over the circle for the burn's duration, embers, smoke, scorch.
                ParticleSystem field = Ps(full, "Field", k.Flame, (0.5f, 0.85f), (0f, 0f), (0.55f, 0.95f), 80, position: new Vector3(0f, 0.05f, 0f));
                Rate(field, 26f, 0f, burn);
                Shape(field, ParticleSystemShapeType.Circle, 0.95f, 0f, Up);
                ShapeOnly(field);
                Velocity(field, new Vector3(0f, 0.7f, 0f), local: false);
                SizeOverLife(field, new Keyframe(0f, 0.4f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.7f));
                Spin(field, 20f, 0f);

                ParticleSystem embers = Ps(full, "Embers", k.FireSpark, (0.7f, 1.3f), (0f, 0f), (0.03f, 0.05f), 50, position: new Vector3(0f, 0.2f, 0f));
                Rate(embers, 14f, 0f, burn);
                Shape(embers, ParticleSystemShapeType.Circle, 0.9f, 0f, Up);
                ShapeOnly(embers);
                Velocity(embers, new Vector3(0f, 2.2f, 0f), new Vector3(0.4f, 0.6f, 0.4f), local: false);
                Noise(embers, 0.6f, 0.8f);

                Transform medium = Gate(full, "Medium", VfxQuality.Medium);
                ParticleSystem smoke = Ps(medium, "Smoke", k.Smoke, (1.2f, 1.8f), (0f, 0f), (1f, 1.5f), 16, position: new Vector3(0f, 0.6f, 0f));
                Rate(smoke, 3.5f, 0f, burn);
                Shape(smoke, ParticleSystemShapeType.Circle, 0.8f, 0f, Up);
                ShapeOnly(smoke);
                Velocity(smoke, new Vector3(0f, 1.1f, 0f), local: false);
                SizeOverLife(smoke, new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f));
                Spin(smoke, 180f, 30f);

                ParticleSystem scorch = Ps(full, "Scorch", k.Scorch, (burn + 0.6f, burn + 0.6f), (0f, 0f), (2.05f, 2.05f), 2, world: false,
                    position: new Vector3(0f, 0.02f, 0f));
                Burst(scorch, 1);
                OnGround(scorch, k.Ground);
                Order(scorch, 5f);

                root.AddComponent<RadiusScaledFx>().Configure(new[] { full }, new[] { partial }, null);
                FlashLight(root, Palette.Fire.Main, 8f, 0.5f, 4f);
                Shake(root, 0.12f);
                return root;
            });

            GameObject beam = Beam(f, "FX_Incendiary_Beam", k.FireBolt, k.FireTrail, 120f, 2.6f, 0.12f, 0.05f, 0.16f, 0f);
            GameObject hit = Save(f, "FX_Fire_Hit", () => FireHit(k));
            return new CardFx { Beam = beam, Area = area, Impact = hit };
        }

        static GameObject FireHit(Kit k)
        {
            GameObject root = Root("FX_Fire_Hit", 1f);
            Transform t = root.transform;
            Flash(t, "Flash", k.FireCore, 0.9f, 0.07f);
            ParticleSystem fire = Ps(t, "Fire", k.Billow_, (0.25f, 0.35f), (0.4f, 1.2f), (0.3f, 0.42f), 5);
            Burst(fire, 3);
            Shape(fire, ParticleSystemShapeType.Sphere, 0.15f);
            MeshParticles(fire, k.Billow, alignLocal: false, randomRotation: true);
            Drag(fire, 2f);
            SizeOverLife(fire, new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1.1f), new Keyframe(1f, 1.25f));
            Sparks(t, "Embers", k.FireSpark, 12, (3f, 7f), (0.2f, 0.45f), 80f, 0.6f, Up, 0.035f);
            return root;
        }

        /// <summary>
        /// メテオストライク: the warning sigil on the ground (closing in over the delay) while a burning rock falls out of the
        /// sky onto it; then the big blast - flash, a ball of fire, billows thrown out, a double shock ring, rocks and
        /// burning debris, dust, a crater scorch, a heavy camera shake.
        /// </summary>
        static CardFx Meteor(Kit k)
        {
            string f = Folder("MeteorStrike");
            float delay = Card("CD_MeteorStrike")?.impactDelay ?? 0.9f;
            GameObject telegraph = Save(f, "FX_MeteorStrike_Warning", () =>
            {
                GameObject root = Root("FX_MeteorStrike_Warning", delay + 0.15f);
                Transform t = root.transform;
                Transform full = Child(t, "Full");
                ParticleSystem zone = Ps(full, "Zone", k.Warning, (delay + 0.1f, delay + 0.1f), (0f, 0f), (2f, 2f), 2, world: false, position: new Vector3(0f, 0.05f, 0f));
                Burst(zone, 1);
                OnGround(zone, k.Ground);

                // The meteor: hot core, a flame shell streaming back, a fire trail, embers and smoke left in the air.
                Transform rock = Child(t, "Meteor");
                MeshLayer(rock, "Core", k.Sphere, k.MeteorCore, 1.5f);
                MeshRenderer shell = MeshLayer(rock, "Shell", k.Shell, k.MeteorShell, 1f);
                shell.transform.localScale = new Vector3(1.6f, 1.6f, 2.4f);
                shell.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // the shell's tail trails behind the flight
                TrailRenderer trail = Trail(rock, "Trail", k.FireTrail, 0.4f, 1.6f);
                trail.minVertexDistance = 0.4f;
                ParticleSystem embers = Ps(rock, "Embers", k.FireSpark, (0.4f, 0.8f), (0.5f, 2f), (0.06f, 0.1f), 80);
                Rate(embers, 0f, 1.6f, delay + 0.1f);
                Shape(embers, ParticleSystemShapeType.Sphere, 0.6f);
                Transform medium = Gate(rock, "Medium", VfxQuality.Medium);
                ParticleSystem smoke = Ps(medium, "Smoke", k.Smoke, (0.6f, 0.9f), (0f, 0.3f), (1.2f, 1.8f), 40);
                Rate(smoke, 0f, 0.7f, delay + 0.1f);
                Shape(smoke, ParticleSystemShapeType.Sphere, 0.5f);
                SizeOverLife(smoke, new Keyframe(0f, 0.6f), new Keyframe(1f, 1.6f));
                root.AddComponent<FallingStrikeFx>().Configure(rock, delay, 30f, new Vector3(10f, 0f, 7f));
                root.AddComponent<RadiusScaledFx>().Configure(new[] { full }, new Transform[0], null);
                return root;
            });

            GameObject area = Save(f, "FX_MeteorStrike_Blast", () =>
            {
                GameObject root = Root("FX_MeteorStrike_Blast", 3.2f);
                Transform t = root.transform;
                Transform full = Child(t, "Full");
                Transform partial = Child(t, "Partial");

                Flash(partial, "Flash", k.FireCore, 2f, 0.12f, new Vector3(0f, 1f, 0f));
                ParticleSystem ball = Ps(partial, "Fireball", k.BlastCore, (0.45f, 0.55f), (0f, 0f), (2f, 2.2f), 2, position: new Vector3(0f, 0.8f, 0f));
                Burst(ball, 1);
                MeshParticles(ball, k.Billow, alignLocal: false, randomRotation: true);
                SizeOverLife(ball, new Keyframe(0f, 0.3f, 0f, 6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 1.25f));

                ParticleSystem billows = Ps(partial, "Billows", k.Billow_, (0.5f, 0.8f), (4f, 8f), (0.7f, 1.1f), 18, position: new Vector3(0f, 0.5f, 0f));
                Burst(billows, 12);
                Shape(billows, ParticleSystemShapeType.Hemisphere, 0.4f, 0f, Up);
                MeshParticles(billows, k.Billow, alignLocal: false, randomRotation: true);
                Drag(billows, 4f);
                Velocity(billows, new Vector3(0f, 1.2f, 0f), local: false);
                SizeOverLife(billows, new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1.1f), new Keyframe(1f, 1.4f));

                GroundRing(full, "Shock", k.FireShock, k.Ring, 0.1f, 1.05f, 0.5f);
                Transform medium = Gate(full, "Medium", VfxQuality.Medium);
                GroundRing(medium, "Shock2", k.FireShock, k.Ring, 0.1f, 0.8f, 0.6f, 0.08f);

                ParticleSystem tongues = Ps(full, "Tongues", k.Flame, (0.5f, 0.9f), (0f, 0f), (0.8f, 1.4f), 30, position: new Vector3(0f, 0.1f, 0f));
                Burst(tongues, 16);
                Shape(tongues, ParticleSystemShapeType.Circle, 0.7f, 0f, Up);
                ShapeOnly(tongues);
                Velocity(tongues, new Vector3(0f, 1.6f, 0f), local: false);
                SizeOverLife(tongues, new Keyframe(0f, 0.5f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0.6f));

                Sparks(partial, "Sparks", k.FireSpark, 40, (8f, 18f), (0.4f, 0.9f), 75f, 1f, Up, 0.06f);
                ParticleSystem debris = Ps(partial, "Debris", k.Ember, (0.9f, 1.5f), (6f, 12f), (0.12f, 0.22f), 20, position: new Vector3(0f, 0.3f, 0f));
                Burst(debris, 12);
                Shape(debris, ParticleSystemShapeType.Cone, 0.3f, 55f, Up);
                MeshParticles(debris, k.Chunk, alignLocal: false, randomRotation: true);
                Gravity(debris, 1.6f);

                Transform mediumP = Gate(partial, "Medium", VfxQuality.Medium);
                ParticleSystem rocks = Ps(mediumP, "Rocks", k.Chip, (1f, 1.5f), (5f, 10f), (0.15f, 0.3f), 14, position: new Vector3(0f, 0.2f, 0f));
                Burst(rocks, 10);
                Shape(rocks, ParticleSystemShapeType.Cone, 0.3f, 50f, Up);
                MeshParticles(rocks, k.Chunk, alignLocal: false, randomRotation: true);
                Gravity(rocks, 1.8f);
                ParticleSystem dust = Ps(mediumP, "Dust", k.Smoke, (1.6f, 2.4f), (2f, 5f), (1.6f, 2.4f), 16, position: new Vector3(0f, 0.5f, 0f));
                Burst(dust, 10);
                Shape(dust, ParticleSystemShapeType.Hemisphere, 0.5f, 0f, Up);
                Drag(dust, 3f);
                Velocity(dust, new Vector3(0f, 0.9f, 0f), local: false);
                SizeOverLife(dust, new Keyframe(0f, 0.5f), new Keyframe(1f, 1.9f));
                Spin(dust, 180f, 25f);

                ParticleSystem crater = Ps(full, "Crater", k.Scorch, (3.2f, 3.2f), (0f, 0f), (2.3f, 2.3f), 2, world: false, position: new Vector3(0f, 0.02f, 0f));
                Burst(crater, 1);
                OnGround(crater, k.Ground);
                Order(crater, 5f);

                root.AddComponent<RadiusScaledFx>().Configure(new[] { full }, new[] { partial }, null);
                FlashLight(root, Palette.Fire.Main, 16f, 0.5f, 8f);
                Shake(root, 0.45f, 2.5f);
                return root;
            });

            return new CardFx { Telegraph = telegraph, Area = area, Impact = Save(f, "FX_Fire_Hit", () => FireHit(k)) };
        }

        // ================================================================== ice

        /// <summary>氷の槍: a faceted ice spear thrown at 110 m/s with a frost trail and glitter; it shatters on the target.</summary>
        static CardFx IceLance(Kit k)
        {
            string f = Folder("IceLance");
            GameObject beam = Save(f, "FX_IceLance_Spear", () =>
            {
                GameObject root = Root("FX_IceLance_Spear", 0.8f);
                Transform spear = Child(root.transform, "Spear");
                MeshRenderer body = MeshLayer(spear, "Body", k.Spear, k.Crystal, 1f);
                body.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
                ParticleSystem glow = Ps(spear, "Glow", k.IceGlow, (0.08f, 0.1f), (0f, 0f), (0.9f, 1f), 4, world: false, position: new Vector3(0f, 0f, 0.6f));
                Rate(glow, 30f, 0f, 0.6f);
                TrailRenderer trail = Trail(spear, "Trail", k.IceTrail, 0.2f, 0.55f);
                trail.minVertexDistance = 0.25f;
                ParticleSystem glitter = Ps(spear, "Glitter", k.IceSpark, (0.25f, 0.5f), (0.2f, 0.8f), (0.02f, 0.04f), 60);
                Rate(glitter, 0f, 3f, 0.6f);
                Shape(glitter, ParticleSystemShapeType.Sphere, 0.12f);
                Transform medium = Gate(spear, "Medium", VfxQuality.Medium);
                ParticleSystem mist = Ps(medium, "Mist", k.Mist, (0.35f, 0.55f), (0f, 0.3f), (0.3f, 0.45f), 40);
                Rate(mist, 0f, 1.2f, 0.6f);
                Shape(mist, ParticleSystemShapeType.Sphere, 0.1f);
                SizeOverLife(mist, new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f));
                spear.gameObject.SetActive(false);
                root.AddComponent<ThrownBeamFx>().Configure(spear, 110f);
                return root;
            });

            GameObject hit = Save(f, "FX_IceLance_Shatter", () => IceShatter(k, "FX_IceLance_Shatter", 2.3f));
            return new CardFx { Beam = beam, Impact = hit };
        }

        static GameObject IceShatter(Kit k, string name, float s)
        {
            GameObject root = Root(name, 1.4f);
            Transform t = root.transform;
            Flash(t, "Flash", k.IceGlow, 1.3f * s, 0.1f);
            ParticleSystem halo = Ps(t, "Halo", k.IceGlow, (0.22f, 0.26f), (0f, 0f), (1.6f * s, 1.8f * s), 2, world: false);
            Burst(halo, 1);
            Fade(halo, (0f, 0f), (0.5f, 0.15f), (0f, 1f));
            ParticleSystem shards = Ps(t, "Shards", k.Crystal, (0.5f, 0.9f), (2.5f * s, 5.5f * s), (0.14f * s, 0.26f * s), 20);
            Burst(shards, 14);
            Shape(shards, ParticleSystemShapeType.Sphere, 0.15f);
            MeshParticles(shards, k.Shard, alignLocal: false, randomRotation: true);
            Gravity(shards, 1.4f);
            ParticleSystem crust = Ps(t, "Crust", k.Crystal, (0.8f, 1.1f), (0f, 0f), (0.18f * s, 0.32f * s), 10);
            Burst(crust, 6);
            Shape(crust, ParticleSystemShapeType.Sphere, 0.3f * s, 0f, default, 0.1f);
            MeshParticles(crust, k.Shard, alignLocal: false, randomRotation: true);
            SizeOverLife(crust, new Keyframe(0f, 0.2f, 0f, 8f), new Keyframe(0.12f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0.3f));
            ParticleSystem glitter = Ps(t, "Glitter", k.IceSpark, (0.4f, 0.9f), (1f, 4f), (0.025f, 0.045f), 30);
            Burst(glitter, 18);
            Shape(glitter, ParticleSystemShapeType.Sphere, 0.3f);
            Drag(glitter, 2f);
            Velocity(glitter, new Vector3(0f, -0.4f, 0f), local: false);
            Transform medium = Gate(t, "Medium", VfxQuality.Medium);
            ParticleSystem mist = Ps(medium, "Mist", k.Mist, (0.6f, 0.9f), (0.3f, 1f), (0.5f * s, 0.8f * s), 6);
            Burst(mist, 3);
            Shape(mist, ParticleSystemShapeType.Sphere, 0.2f);
            Drag(mist, 3f);
            SizeOverLife(mist, new Keyframe(0f, 0.5f), new Keyframe(1f, 1.7f));
            FlashLight(root, Palette.Ice.Main, 5f, 0.1f, 3f);
            return root;
        }

        /// <summary>
        /// フロストノヴァ: around the caster, three rings of ice spikes burst out of the ground one after another, a frost ring
        /// sweeps out over a white frost patch, cold mist rolls outward and glitter falls.
        /// </summary>
        static CardFx FrostNova(Kit k)
        {
            string f = Folder("FrostNova");
            GameObject area = Save(f, "FX_FrostNova_Area", () =>
            {
                GameObject root = Root("FX_FrostNova_Area", 2.4f);
                Transform t = root.transform;
                Transform full = Child(t, "Full");

                GroundRing(full, "FrostRing", k.IceShock, k.Ring, 0.08f, 1.05f, 0.45f);
                ParticleSystem frost = Ps(full, "Frost", k.Frost, (2.3f, 2.3f), (0f, 0f), (2.1f, 2.1f), 2, world: false, position: new Vector3(0f, 0.02f, 0f));
                Burst(frost, 1);
                OnGround(frost, k.Ground);
                SizeOverLife(frost, new Keyframe(0f, 0.1f, 0f, 8f), new Keyframe(0.18f, 1f), new Keyframe(1f, 1f));
                Order(frost, 5f);

                float[] radii = { 0.42f, 0.7f, 0.96f };
                short[] counts = { 8, 12, 16 };
                for (int i = 0; i < radii.Length; i++)
                {
                    ParticleSystem spikes = Ps(full, $"Spikes{i}", k.Crystal, (1.3f, 1.6f), (0f, 0f), (0.75f + i * 0.2f, 1.15f + i * 0.25f), 24, world: false);
                    Burst(spikes, counts[i]);
                    Shape(spikes, ParticleSystemShapeType.Circle, radii[i], 0f, Up, 0.12f);
                    ShapeOnly(spikes);
                    MeshParticles(spikes, k.Spike, alignLocal: true, randomRotation: false);
                    ParticleSystem.MainModule main = spikes.main;
                    main.startRotation3D = true;
                    main.startRotationX = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
                    main.startRotationY = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
                    main.startRotationZ = new ParticleSystem.MinMaxCurve(-0.35f, 0.35f);
                    SizeOverLife(spikes, new Keyframe(0f, 0f, 0f, 12f), new Keyframe(0.08f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0.2f));
                    Delay(spikes, 0.04f + i * 0.07f);
                }

                ParticleSystem mist = Ps(full, "Mist", k.Mist, (0.8f, 1.1f), (0f, 0f), (0.7f, 1f), 12, position: new Vector3(0f, 0.2f, 0f));
                Burst(mist, 8);
                Shape(mist, ParticleSystemShapeType.Circle, 0.2f, 0f, Up);
                ShapeOnly(mist);
                Velocity(mist, Vector3.zero, radial: 7f);
                Drag(mist, 2.5f);
                SizeOverLife(mist, new Keyframe(0f, 0.5f), new Keyframe(1f, 1.6f));
                Spin(mist, 180f, 30f);

                ParticleSystem glitter = Ps(full, "Glitter", k.IceSpark, (0.8f, 1.4f), (0f, 0f), (0.04f, 0.07f), 70, position: new Vector3(0f, 0.6f, 0f));
                Burst(glitter, 50);
                Shape(glitter, ParticleSystemShapeType.Circle, 0.6f, 0f, Up);
                ShapeOnly(glitter);
                Velocity(glitter, new Vector3(0f, 1.2f, 0f), radial: 4f, local: false);
                Drag(glitter, 1.5f);
                Gravity(glitter, 0.15f);

                root.AddComponent<RadiusScaledFx>().Configure(new[] { full }, new Transform[0], null);
                FlashLight(root, Palette.Ice.Main, 10f, 0.4f, 4f);
                Shake(root, 0.14f, 1.2f);
                return root;
            });

            return new CardFx { Area = area, Impact = Save(f, "FX_Ice_Hit", () => IceShatter(k, "FX_Ice_Hit", 1.5f)) };
        }

        // ================================================================== lightning

        /// <summary>連鎖雷撃: the thick stylised bolt (LightningBoltFx) in the card's yellow and violet, a burst at every target.</summary>
        static CardFx ChainLightning(Kit k)
        {
            string f = Folder("ChainLightning");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Root_ + "/Prefabs/VFX/Lightning/FX_Lightning_Bolt.prefab");
            GameObject beam = Save(f, "FX_ChainLightning_Bolt", () =>
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(source);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.name = "FX_ChainLightning_Bolt";
                foreach (LineRenderer line in go.GetComponentsInChildren<LineRenderer>(true))
                {
                    line.sharedMaterial = line.name.Contains("Branch") ? k.LBranch : k.LBolt;
                }

                var bolt = go.GetComponent<LightningBoltFx>();
                var so = new SerializedObject(bolt);
                SerializedProperty width = so.FindProperty("width");
                if (width != null)
                {
                    width.floatValue = 0.62f;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
                go.AddComponent<LightningBeamFx>().Configure(1f, 0.035f);
                return go;
            });

            GameObject hit = Save(f, "FX_Lightning_Hit", () => LightningHit(k, "FX_Lightning_Hit", 1.35f));
            return new CardFx { Beam = beam, Impact = hit };
        }

        static GameObject LightningHit(Kit k, string name, float s)
        {
            GameObject root = Root(name, 0.8f);
            Transform t = root.transform;
            ParticleSystem halo = Ps(t, "Halo", k.LGlowMat, (0.1f, 0.12f), (0f, 0f), (1.3f * s, 1.5f * s), 2, world: false);
            Burst(halo, 1);
            Order(halo, -1f);
            Flash(t, "Star", k.LFlash, 1.6f * s, 0.07f);
            ParticleSystem ring = Ps(t, "Ring", k.LRing, (0.16f, 0.18f), (0f, 0f), (1.4f * s, 1.5f * s), 2, world: false);
            Burst(ring, 1);
            Sparks(t, "Sparks", k.LSpark, 16, (3f, 8f), (0.15f, 0.35f), 80f, 0.5f, Up, 0.035f);
            Transform arcs = Child(t, "Arcs");
            var lines = new LineRenderer[3];
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = Line(arcs, $"Arc{i}", k.LBranch, 0.06f, 6);
            }

            arcs.gameObject.AddComponent<SurfaceArcFx>().Configure(lines, 0.55f * s, 0.24f);
            FlashLight(root, Palette.Lightning.Main, 5f, 0.08f, 4f);
            return root;
        }

        /// <summary>
        /// サンダーチャージ: a crack of light around the caster, a shock ring at the feet, then while the buff lasts the body
        /// crackles - arcs re-striking around the chest and sparks dripping off - until the buff ends.
        /// </summary>
        static CardFx ThunderCharge(Kit k)
        {
            string f = Folder("ThunderCharge");
            float seconds = BuffSeconds("CD_ThunderCharge", SkillEffectKind.DamageBuff, 8f);
            GameObject aura = Save(f, "FX_ThunderCharge_Aura", () =>
            {
                GameObject root = Root("FX_ThunderCharge_Aura", seconds + 1f);
                Transform t = root.transform;
                ParticleSystem ring = Ps(t, "Ring", k.LRing, (0.3f, 0.32f), (0f, 0f), (2.4f, 2.6f), 2, world: false, position: new Vector3(0f, 0.05f, 0f));
                Burst(ring, 1);
                OnGround(ring, k.Ground);
                // Rises from the ring's edge, not next to the body: from 0.6 m at 6 m/s the sparks crossed the first-person view
                // as white vertical streaks for half a second.
                ParticleSystem rise = Ps(t, "Rise", k.LSpark, (0.4f, 0.6f), (0f, 0f), (0.04f, 0.07f), 44, position: new Vector3(0f, 0.1f, 0f));
                Burst(rise, 36);
                Shape(rise, ParticleSystemShapeType.Circle, 1.15f, 0f, Up, 0.05f);
                Velocity(rise, new Vector3(0f, 3.2f, 0f), local: false);
                Stretch(rise, 0.03f, 0.8f);

                ParticleSystem drip = Ps(t, "Drip", k.LSpark, (0.3f, 0.6f), (0.5f, 2f), (0.025f, 0.045f), 30, position: new Vector3(0f, 0.65f, 0f));
                Rate(drip, 9f, 0f, seconds, loop: false);
                Shape(drip, ParticleSystemShapeType.Sphere, 0.4f);
                Gravity(drip, 0.8f);
                Stretch(drip, 0.04f, 1.2f);

                ParticleSystem glow = Ps(t, "Glow", k.LGlowMat, (0.35f, 0.5f), (0f, 0f), (1.1f, 1.3f), 10, world: false, position: new Vector3(0f, 0.55f, 0f));
                Rate(glow, 4f, 0f, seconds);
                Fade(glow, (0f, 0f), (0.6f, 0.3f), (0f, 1f));

                Transform arcs = Child(t, "Arcs", new Vector3(0f, 0.6f, 0f));
                var lines = new LineRenderer[3];
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = Line(arcs, $"Arc{i}", k.LBranch, 0.05f, 6);
                }

                arcs.gameObject.AddComponent<SurfaceArcFx>().Configure(lines, 0.45f, 0.2f);
                arcs.gameObject.AddComponent<RepeatArcsFx>().Configure(0.35f, 0.9f);

                root.AddComponent<StatusAuraFx>().Configure(StatusAuraFx.Kind.DamageBuff, 0.5f);
                FlashLight(root, Palette.Lightning.Main, 6f, 0.25f, 4f);
                return root;
            });

            // Self cards never play their impact; it is still their own (an empty slot would get a generic one at bootstrap).
            return new CardFx { Aura = aura, Impact = Save(f, "FX_ThunderCharge_Hit", () => LightningHit(k, "FX_ThunderCharge_Hit", 0.8f)) };
        }

        // ================================================================== gravity

        /// <summary>
        /// グラビティスパイク: a dark violet beam pulsing toward the target, dark crystal spikes bursting out of it, and around the
        /// point a short vortex pulling inward that ends in a small implosion.
        /// </summary>
        static CardFx GravitySpike(Kit k)
        {
            string f = Folder("GravitySpike");
            float pull = BuffSeconds("CD_GravitySpike", SkillEffectKind.Pull, 0.6f);
            GameObject beam = Beam(f, "FX_GravitySpike_Beam", k.GBeam, k.GBeam, 160f, 6f, 0.14f, 0.05f, 0.18f, 0.02f);
            GameObject hit = Save(f, "FX_GravitySpike_Spikes", () =>
            {
                GameObject root = Root("FX_GravitySpike_Spikes", 1.2f);
                Transform t = root.transform;
                Flash(t, "Flash", k.GFlash, 1.2f, 0.08f);
                ParticleSystem spikes = Ps(t, "Spikes", k.GCrystal, (0.7f, 0.9f), (6f, 9f), (0.35f, 0.55f), 12);
                Burst(spikes, 7);
                Shape(spikes, ParticleSystemShapeType.Sphere, 0.1f);
                MeshParticles(spikes, k.SpikeZ, alignLocal: false, randomRotation: false);
                spikes.GetComponent<ParticleSystemRenderer>().alignment = ParticleSystemRenderSpace.Velocity; // +Z along the flight: point outward
                Drag(spikes, 14f);
                SizeOverLife(spikes, new Keyframe(0f, 0.3f, 0f, 8f), new Keyframe(0.1f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f));
                Sparks(t, "Motes", k.GSpark, 14, (2f, 5f), (0.3f, 0.5f), 90f, 0f, default, 0.03f);
                FlashLight(root, Palette.Gravity.Main, 4f, 0.12f, 3f);
                return root;
            });

            GameObject area = Save(f, "FX_GravitySpike_Pull", () => Vortex(k, "FX_GravitySpike_Pull", pull, 0.55f, 0.18f));
            return new CardFx { Beam = beam, Impact = hit, Area = area };
        }

        /// <summary>重力崩壊: a black singularity with a swirling disc that drags streaks and rubble in for the pull, then collapses.</summary>
        static CardFx GravityCollapse(Kit k)
        {
            string f = Folder("GravityCollapse");
            float pull = BuffSeconds("CD_GravityCollapse", SkillEffectKind.Pull, 1.2f);
            GameObject beam = Beam(f, "FX_GravityCollapse_Beam", k.GBeam, k.GBeam, 120f, 4f, 0.12f, 0.04f, 0.18f, 0.02f);
            GameObject area = Save(f, "FX_GravityCollapse_Area", () => Vortex(k, "FX_GravityCollapse_Area", pull, 2.2f, 0.4f));
            GameObject hit = Save(f, "FX_Gravity_Hit", () =>
            {
                GameObject root = Root("FX_Gravity_Hit", 0.9f);
                Transform t = root.transform;
                Flash(t, "Flash", k.GFlash, 0.9f, 0.07f);
                ParticleSystem ring = Ps(t, "Ring", k.GRing, (0.2f, 0.22f), (0f, 0f), (1.1f, 1.2f), 2, world: false);
                Burst(ring, 1);
                Sparks(t, "Motes", k.GSpark, 10, (1f, 3f), (0.3f, 0.5f), 90f, 0f, default, 0.03f);
                return root;
            });
            return new CardFx { Beam = beam, Area = area, Impact = hit };
        }

        /// <summary>
        /// A gravity well: a dark core (alpha blended, so it darkens the scene) inside a violet rim, a swirling disc on the
        /// ground, streaks and rubble pulled in from the edge for <paramref name="pull"/> seconds, then an implosion flash and
        /// a ring going out. <paramref name="core"/> is the void's size in metres (it does not follow the radius).
        /// </summary>
        static GameObject Vortex(Kit k, string name, float pull, float core, float shake)
        {
            GameObject root = Root(name, pull + 0.9f);
            Transform t = root.transform;
            Transform full = Child(t, "Full");
            float h = 0.4f + core * 0.5f;

            ParticleSystem voidBall = Ps(t, "Void", k.Void, (pull + 0.12f, pull + 0.12f), (0f, 0f), (core, core), 2, world: false, position: new Vector3(0f, h, 0f));
            Burst(voidBall, 1);
            ParticleSystem.MainModule vm = voidBall.main;
            vm.scalingMode = ParticleSystemScalingMode.Local;
            SizeOverLife(voidBall, new Keyframe(0f, 0f, 0f, 8f), new Keyframe(0.12f, 1f), new Keyframe(0.85f, 1.1f), new Keyframe(1f, 0f));
            Spin(voidBall, 180f, 120f);
            Fade(voidBall, (0f, 0f), (1f, 0.05f), (1f, 1f));
            Order(voidBall, 1f);

            ParticleSystem rim = Ps(t, "Rim", k.GRing, (pull, pull), (0f, 0f), (core * 1.35f, core * 1.35f), 2, world: false, position: new Vector3(0f, h, 0f));
            Burst(rim, 1);
            ParticleSystem.MainModule rm = rim.main;
            rm.scalingMode = ParticleSystemScalingMode.Local;
            SizeOverLife(rim, new Keyframe(0f, 0f, 0f, 8f), new Keyframe(0.12f, 1f), new Keyframe(0.85f, 1f), new Keyframe(1f, 0.2f));

            ParticleSystem disc = Ps(full, "Disc", k.Vortex2, (pull + 0.1f, pull + 0.1f), (0f, 0f), (2f, 2f), 2, world: false, position: new Vector3(0f, 0.06f, 0f));
            Burst(disc, 1);
            OnGround(disc, k.Ground);
            SizeOverLife(disc, new Keyframe(0f, 1.15f), new Keyframe(1f, 0.6f));

            ParticleSystem streaks = Ps(full, "Streaks", k.GSpark, (0.4f, 0.6f), (0f, 0f), (0.05f, 0.08f), 90, position: new Vector3(0f, h * 0.6f, 0f));
            Rate(streaks, 70f, 0f, pull);
            Shape(streaks, ParticleSystemShapeType.Circle, 1f, 0f, Up, 0.3f);
            ShapeOnly(streaks);
            Velocity(streaks, new Vector3(0f, 0.6f, 0f), radial: -11f, orbitalY: 1.5f, local: true);
            Stretch(streaks, 0.05f, 1.4f);

            Transform medium = Gate(full, "Medium", VfxQuality.Medium);
            ParticleSystem rubble = Ps(medium, "Rubble", k.Chip, (0.6f, 0.9f), (0f, 0f), (0.06f, 0.14f), 30, position: new Vector3(0f, 0.15f, 0f));
            Rate(rubble, 18f, 0f, pull);
            Shape(rubble, ParticleSystemShapeType.Circle, 0.85f, 0f, Up, 0.3f);
            ShapeOnly(rubble);
            MeshParticles(rubble, k.Chunk, alignLocal: false, randomRotation: true);
            Velocity(rubble, new Vector3(0f, 1.6f, 0f), radial: -7f, orbitalY: 2f, local: true);

            // The collapse when the pull ends.
            ParticleSystem implode = Ps(t, "Implode", k.GFlash, (0.12f, 0.14f), (0f, 0f), (core * 2.2f, core * 2.4f), 2, world: false, position: new Vector3(0f, h, 0f));
            Burst(implode, 1, pull);
            ParticleSystem.MainModule im = implode.main;
            im.scalingMode = ParticleSystemScalingMode.Local;
            im.duration = pull + 0.05f;
            ParticleSystem outRing = Ps(full, "OutRing", k.GShock, (0.45f, 0.45f), (0f, 0f), (1f, 1f), 2, world: false, position: new Vector3(0f, 0.08f, 0f));
            Burst(outRing, 1, pull);
            OnGround(outRing, k.Ring);
            SizeOverLife(outRing, new Keyframe(0f, 0.05f, 0f, 3f), new Keyframe(0.5f, 0.92f), new Keyframe(1f, 1f));
            ParticleSystem.MainModule om = outRing.main;
            om.duration = pull + 0.05f;
            ParticleSystem burst = Ps(t, "Burst", k.GSpark, (0.3f, 0.6f), (5f, 11f), (0.04f, 0.07f), 40, position: new Vector3(0f, h, 0f));
            Burst(burst, 30, pull);
            Shape(burst, ParticleSystemShapeType.Sphere, 0.2f);
            Stretch(burst, 0.04f, 1.3f);
            Drag(burst, 1.2f);
            ParticleSystem.MainModule bm = burst.main;
            bm.duration = pull + 0.05f;

            root.AddComponent<RadiusScaledFx>().Configure(new[] { full }, new Transform[0], null);
            FlashLight(root, Palette.Gravity.Main, 8f, pull, 4f);
            Shake(root, shake, 0f, pull);
            return root;
        }

        // ================================================================== support

        /// <summary>
        /// シールド: a hex barrier grows up around the ally from the feet, shimmers while the shield holds and breaks apart
        /// when it is used up or runs out; a ring and rising motes when it forms.
        /// </summary>
        static CardFx Shield(Kit k)
        {
            string f = Folder("Shield");
            float seconds = BuffSeconds("CD_Shield", SkillEffectKind.Shield, 8f);
            GameObject aura = Save(f, "FX_Shield_Barrier", () =>
            {
                GameObject root = Root("FX_Shield_Barrier", seconds + 1f);
                Transform t = root.transform;
                MeshRenderer bubble = MeshLayer(t, "Bubble", k.Sphere, k.Barrier, 2.4f);
                bubble.transform.localPosition = new Vector3(0f, 1f, 0f);
                root.AddComponent<ShapeAnimFx>().Configure(new[]
                {
                    Track(bubble, "_Reveal", Ramp(0f, 0f, 0.3f, 1f), AnimationCurve.Constant(0f, 1f, 1f),
                        new AnimationCurve(new Keyframe(0f, 1.15f), new Keyframe(0.35f, 1f)), Ramp(0f, 1f, 0.45f, 1.12f)),
                    Track(bubble, "_Dissolve", AnimationCurve.Constant(0f, 1f, 0f), Ramp(0f, 0f, 0.45f, 1.15f)),
                }, 0f);

                GroundRing(t, "Ring", k.NShock, k.Ring, 0.3f, 1.4f, 0.5f);
                ParticleSystem motes = Ps(t, "Motes", k.NGlow, (0.5f, 0.8f), (0f, 0f), (0.05f, 0.08f), 40, position: new Vector3(0f, 0.1f, 0f));
                Burst(motes, 26);
                Shape(motes, ParticleSystemShapeType.Circle, 1f, 0f, Up, 0.1f);
                Velocity(motes, new Vector3(0f, 3f, 0f), radial: -0.6f, local: false);
                ParticleSystem shimmer = Ps(t, "Shimmer", k.NGlow, (0.5f, 0.8f), (0f, 0f), (0.035f, 0.06f), 20, position: new Vector3(0f, 1f, 0f));
                Rate(shimmer, 5f, 0f, seconds);
                Shape(shimmer, ParticleSystemShapeType.Sphere, 1.15f, 0f, default, 0f);
                Velocity(shimmer, new Vector3(0f, 0.5f, 0f), local: false);

                root.AddComponent<StatusAuraFx>().Configure(StatusAuraFx.Kind.Shield, 0.5f);
                return root;
            });

            GameObject hit = Save(f, "FX_Shield_Hit", () =>
            {
                GameObject root = Root("FX_Shield_Hit", 0.8f);
                Flash(root.transform, "Flash", k.NGlow, 0.9f, 0.1f);
                Sparks(root.transform, "Motes", k.NGlow, 14, (1f, 3f), (0.3f, 0.6f), 90f, -0.2f, Up, 0.04f);
                return root;
            });

            return new CardFx { Aura = aura, Impact = hit };
        }

        /// <summary>範囲回復: a turning heal circle drawn over the area, a ring and motes rising across it; each healed ally glows up.</summary>
        static CardFx AreaHeal(Kit k)
        {
            string f = Folder("AreaHeal");
            GameObject area = Save(f, "FX_AreaHeal_Circle", () =>
            {
                GameObject root = Root("FX_AreaHeal_Circle", 2.2f);
                Transform t = root.transform;
                Transform full = Child(t, "Full");
                ParticleSystem circle = Ps(full, "Circle", k.HealCircle, (1.9f, 1.9f), (0f, 0f), (2f, 2f), 2, world: false, position: new Vector3(0f, 0.05f, 0f));
                Burst(circle, 1);
                OnGround(circle, k.Ground);
                GroundRing(full, "Wave", k.NShock, k.Ring, 0.08f, 1f, 0.7f);
                ParticleSystem motes = Ps(full, "Motes", k.NGlow, (0.8f, 1.4f), (0f, 0f), (0.06f, 0.1f), 90, position: new Vector3(0f, 0.1f, 0f));
                Rate(motes, 70f, 0f, 0.9f);
                Shape(motes, ParticleSystemShapeType.Circle, 0.95f, 0f, Up);
                ShapeOnly(motes);
                Velocity(motes, new Vector3(0f, 2.4f, 0f), local: false);
                Noise(motes, 0.4f, 0.8f);
                root.AddComponent<RadiusScaledFx>().Configure(new[] { full }, new Transform[0], null);
                FlashLight(root, Palette.Neutral.Main, 9f, 0.5f, 3f);
                return root;
            });

            GameObject aura = Save(f, "FX_AreaHeal_Ally", () =>
            {
                GameObject root = Root("FX_AreaHeal_Ally", 1.4f);
                Transform t = root.transform;
                GroundRing(t, "Ring", k.NShock, k.Ring, 0.3f, 0.9f, 0.5f);
                ParticleSystem spiral = Ps(t, "Spiral", k.NGlow, (0.7f, 1f), (0f, 0f), (0.06f, 0.1f), 50, position: new Vector3(0f, 0.1f, 0f));
                Burst(spiral, 34);
                Shape(spiral, ParticleSystemShapeType.Circle, 0.55f, 0f, Up, 0.05f);
                Velocity(spiral, new Vector3(0f, 2.2f, 0f), orbitalY: 3.5f, local: true);
                ParticleSystem glow = Ps(t, "Glow", k.NGlow, (0.45f, 0.5f), (0f, 0f), (1.6f, 1.8f), 2, world: false, position: new Vector3(0f, 1f, 0f));
                Burst(glow, 1);
                Fade(glow, (0f, 0f), (0.45f, 0.25f), (0f, 1f));
                return root;
            });

            return new CardFx { Area = area, Aura = aura, Impact = aura };
        }

        /// <summary>
        /// 敵のマーキング: a thin red ping to the target, then a reticle on the enemy (brackets closing in) that stays and
        /// pulses for as long as the enemy takes extra damage.
        /// </summary>
        static CardFx Mark(Kit k)
        {
            string f = Folder("MarkTarget");
            float seconds = BuffSeconds("CD_MarkTarget", SkillEffectKind.Vulnerability, 8f);
            GameObject beam = Beam(f, "FX_MarkTarget_Ping", k.MarkBeam, k.MarkBeam, 220f, 5f, 0.06f, 0.025f, 0.12f, 0f);
            GameObject hit = Save(f, "FX_MarkTarget_Reticle", () =>
            {
                GameObject root = Root("FX_MarkTarget_Reticle", seconds + 0.8f);
                Transform t = root.transform;
                // Above the head (a sigil through the body would be hidden by it), as camera-facing particles: a lock-on whose
                // brackets close in as it shrinks, then the steady reticle for as long as the mark lasts.
                Vector3 overHead = new Vector3(0f, 1.25f, 0f);
                ParticleSystem lockOn = Ps(t, "LockOn", k.ReticleOpen, (0.3f, 0.3f), (0f, 0f), (1.25f, 1.25f), 2, world: false, position: overHead);
                Burst(lockOn, 1);
                SizeOverLife(lockOn, new Keyframe(0f, 1.8f), new Keyframe(1f, 1f));
                Fade(lockOn, (0f, 0f), (1f, 0.15f), (1f, 0.85f), (0f, 1f));
                ParticleSystem reticle = Ps(t, "Reticle", k.Reticle, (seconds + 0.5f, seconds + 0.5f), (0f, 0f), (1.25f, 1.25f), 2, world: false,
                    position: overHead);
                Burst(reticle, 1, 0.26f);
                ParticleSystem.MainModule rm = reticle.main;
                rm.duration = 0.3f;
                Fade(reticle, (0f, 0f), (1f, 0.01f), (1f, 0.99f), (0f, 1f));
                Flash(t, "Flash", k.MarkGlow, 1.1f, 0.12f);
                ParticleSystem pulse = Ps(t, "Pulse", k.MarkGlow, (0.7f, 0.7f), (0f, 0f), (1.3f, 1.4f), 4, world: false);
                Rate(pulse, 1.6f, 0f, seconds);
                Fade(pulse, (0f, 0f), (0.55f, 0.2f), (0f, 1f));
                root.AddComponent<StatusAuraFx>().Configure(StatusAuraFx.Kind.Vulnerability, 0.3f);
                return root;
            });

            return new CardFx { Beam = beam, Impact = hit, AttachImpact = true };
        }

        // ================================================================== card data

        static CardDefinition Card(string file) => AssetDatabase.LoadAssetAtPath<CardDefinition>($"{CardFolder}/{file}.asset");

        static float BurnSeconds(string file, float fallback) => BuffSeconds(file, SkillEffectKind.DamageOverTime, fallback);

        static float BuffSeconds(string file, SkillEffectKind kind, float fallback)
        {
            CardDefinition card = Card(file);
            if (card == null)
            {
                return fallback;
            }

            float best = 0f;
            foreach (SkillEffectSpec e in card.effects)
            {
                if (e.kind == kind)
                {
                    best = Mathf.Max(best, e.duration);
                }
            }

            return best > 0f ? best : fallback;
        }

        // ================================================================== meshes

        /// <summary>A double pyramid along +Y: <paramref name="sides"/> around, tips <paramref name="up"/> above and <paramref name="down"/> below.</summary>
        static Mesh Bipyramid(int sides, float radius, float up, float down)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            Vector3 top = new Vector3(0f, up, 0f);
            Vector3 bottom = new Vector3(0f, -down, 0f);
            float total = up + down;
            for (int i = 0; i < sides; i++)
            {
                float a0 = i / (float)sides * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, 0f, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, 0f, Mathf.Sin(a1) * radius);
                Add(top, p1, p0);
                Add(bottom, p0, p1);
            }

            void Add(Vector3 a, Vector3 b, Vector3 c)
            {
                int start = vertices.Count;
                vertices.Add(a);
                vertices.Add(b);
                vertices.Add(c);
                uvs.Add(new Vector2(0.5f, (a.y + down) / total));
                uvs.Add(new Vector2(0f, (b.y + down) / total));
                uvs.Add(new Vector2(1f, (c.y + down) / total));
                triangles.Add(start);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            FixOutward(mesh);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Turns every triangle to face away from the mesh's centre (the shapes here are convex).</summary>
        static void FixOutward(Mesh mesh)
        {
            Vector3[] v = mesh.vertices;
            int[] tri = mesh.triangles;
            Vector3 centre = mesh.bounds.center;
            if (centre == Vector3.zero)
            {
                mesh.RecalculateBounds();
                centre = mesh.bounds.center;
            }

            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a);
                Vector3 mid = (a + b + c) / 3f;
                if (Vector3.Dot(n, mid - new Vector3(0f, mid.y * 0.2f, 0f)) < 0f)
                {
                    (tri[i + 1], tri[i + 2]) = (tri[i + 2], tri[i + 1]);
                }
            }

            mesh.triangles = tri;
        }

        static Mesh Rotate(Mesh mesh, Quaternion rotation)
        {
            Vector3[] v = mesh.vertices;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = rotation * v[i];
            }

            mesh.vertices = v;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>An open tube along +Z (z 0..1, radius 1): uv.x around, uv.y along; normals outward. Seen from both sides.</summary>
        static Mesh Tube_(int segments, int rings)
        {
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float z = r / (float)rings;
                float radius = Mathf.Lerp(0.7f, 1.25f, z); // widens toward the top, like a funnel
                for (int s = 0; s <= segments; s++)
                {
                    float a = s / (float)segments * Mathf.PI * 2f;
                    var n = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                    vertices.Add(new Vector3(n.x * radius, n.y * radius, z));
                    normals.Add(n);
                    uvs.Add(new Vector2(s / (float)segments, z));
                }
            }

            int row = segments + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int a = r * row + s;
                    int b = a + row;
                    triangles.AddRange(new[] { a, a + 1, b, b, a + 1, b + 1 });
                }
            }

            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
