using System.Collections.Generic;
using System.Linq;
using CoopDeck.Core;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.Tests
{
    /// <summary>
    /// Checks the Fire Rifle model, materials and prefab, and that the player holds it in first person
    /// (muzzle used by the weapon and skills) and in the body's right hand (run the bootstrap first).
    /// </summary>
    public class FireRifleAssetTests
    {
        const string ModelPath = "Assets/_Game/Art/Weapons/FireRifle/Models/FireRifle.fbx";
        const string EmissionPath = "Assets/_Game/Art/Weapons/FireRifle/Textures/T_FireRifle_Emission.png";
        const string BaseColorPath = "Assets/_Game/Art/Weapons/FireRifle/Textures/T_FireRifle_BaseColor.png";
        const string NormalPath = "Assets/_Game/Art/Weapons/FireRifle/Textures/T_FireRifle_Normal.png";
        const string SurfacePath = "Assets/_Game/Art/Weapons/FireRifle/Textures/T_FireRifle_Surface.png";
        const string MaterialMaskPath = "Assets/_Game/Art/Weapons/FireRifle/Textures/T_FireRifle_MaterialMask.png";
        const string MaterialPath = "Assets/_Game/Art/Weapons/FireRifle/Materials/M_FireRifle_Toon.mat";
        const string ViewModelMaterialPath = "Assets/_Game/Art/Weapons/FireRifle/Materials/M_FireRifle_Toon_FP.mat";
        const string PrefabPath = "Assets/_Game/Prefabs/Weapons/PF_Weapon_FireRifle.prefab";
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";

        readonly List<GameObject> spawned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in spawned.Where(g => g != null))
            {
                Object.DestroyImmediate(go);
            }

            spawned.Clear();
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path + " missing: run the bootstrap");
            return asset;
        }

        GameObject Spawn(string path)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Load<GameObject>(path));
            spawned.Add(go);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return go;
        }

        static bool IsFireRifle(GameObject instance)
        {
            return PrefabUtility.GetCorrespondingObjectFromOriginalSource(instance) == AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        }

        [Test]
        public void Rifle_Is0_85mLong_GripAtOrigin_MuzzleForward()
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.None), "a static prop");

            GameObject rifle = Spawn(PrefabPath);
            Bounds bounds = rifle.GetComponentInChildren<MeshRenderer>().bounds;
            Assert.That(bounds.size.z, Is.EqualTo(0.85f).Within(0.02f), "length along +Z");
            Assert.That(bounds.size.z, Is.GreaterThan(bounds.size.x * 4f), "long and narrow (not turned sideways)");
            Assert.That(bounds.Contains(Vector3.zero), Is.True, "the origin (grip) is inside the rifle");
            Assert.That(bounds.min.z, Is.EqualTo(-0.245f).Within(0.03f), "stock behind the grip");

            Transform muzzle = rifle.transform.Find("Muzzle");
            Transform leftHand = rifle.transform.Find("LeftHandGrip");
            Assert.That(muzzle, Is.Not.Null);
            Assert.That(leftHand, Is.Not.Null);
            Assert.That(muzzle.localPosition.z, Is.EqualTo(0.605f).Within(0.02f), "muzzle in front");
            Assert.That(muzzle.localPosition.y, Is.EqualTo(0.137f).Within(0.02f), "muzzle above the grip");
            Assert.That(Vector3.Dot(muzzle.forward, Vector3.forward), Is.GreaterThan(0.999f), "muzzle faces +Z");
            Assert.That(leftHand.localPosition.z, Is.InRange(0.3f, 0.5f), "left hand under the barrel shroud");
            Assert.That(bounds.max.z, Is.EqualTo(muzzle.localPosition.z).Within(0.02f), "the muzzle is the front end");
        }

        [Test]
        public void Mesh_HasACloseUpLodAndAFarLod_AndCarriesOutlineNormals()
        {
            GameObject rifle = Spawn(PrefabPath);
            var lodGroup = rifle.GetComponent<LODGroup>();
            Assert.That(lodGroup, Is.Not.Null, "FireRifle_Body_LOD0 / _LOD1 in the FBX make a LOD group");
            LOD[] lods = lodGroup.GetLODs();
            Assert.That(lods.Length, Is.EqualTo(2));
            Mesh near = ((MeshRenderer)lods[0].renderers.Single()).GetComponent<MeshFilter>().sharedMesh;
            Mesh far = ((MeshRenderer)lods[1].renderers.Single()).GetComponent<MeshFilter>().sharedMesh;
            // rebuilt from Meshy's 437k-triangle sculpt (rebuild_weapon_hq.py): 60k with small bevels on the creases for
            // first person; the far LOD for other players' rifles. Meshy's own 7.7k remesh was faceted, 30k folded the
            // sculpt's rounded edges into creases.
            Assert.That(near.triangles.Length / 3, Is.InRange(80000, 140000));
            Assert.That(far.triangles.Length / 3, Is.InRange(20000, 40000));
            Assert.That(lods[0].screenRelativeTransitionHeight, Is.InRange(0.05f, 0.3f), "first person (over 100% of the screen) is always LOD0");

            foreach (Mesh mesh in new[] { near, far })
            {
                Assert.That(mesh.subMeshCount, Is.EqualTo(1));
                var outline = new List<Vector3>();
                mesh.GetUVs(3, outline);
                Assert.That(outline.Count, Is.EqualTo(mesh.vertexCount), "UV3 = averaged normals for the outline pass");
                Assert.That(outline.All(n => Mathf.Abs(n.magnitude - 1f) < 0.01f), Is.True, "unit normals");
                // Hard edges were kept: some vertices' own normals differ from the averaged ones.
                Vector3[] normals = mesh.normals;
                int split = Enumerable.Range(0, normals.Length).Count(i => Vector3.Dot(normals[i], outline[i]) < 0.95f);
                Assert.That(split, Is.GreaterThan(mesh.vertexCount / 20), mesh.name + ": hard-surface edges are kept");
            }
        }

        [Test]
        public void Materials_AreHardSurface_WithMaskEdgesEmission_AndAThinnerFirstPersonOutline()
        {
            var world = Load<Material>(MaterialPath);
            var viewModel = Load<Material>(ViewModelMaterialPath);
            foreach (Material m in new[] { world, viewModel })
            {
                Assert.That(m.shader.name, Is.EqualTo("CoopDeck/Weapon/HardSurfaceToon"), m.name);
                Assert.That(m.IsKeywordEnabled("_EMISSION"), Is.True, m.name);
                Assert.That(m.IsKeywordEnabled("_OUTLINE_UV3_NORMALS"), Is.True, m.name);
                Assert.That(m.GetTexture("_BaseMap"), Is.Not.Null, m.name);
                Assert.That(m.GetTexture("_EmissionMap"), Is.Not.Null, m.name);
                Assert.That(m.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(1f), m.name + ": HDR so bloom (threshold 1) catches it");
                Assert.That(m.IsKeywordEnabled("_NORMALMAP"), Is.True, m.name + ": the sculpt's detail as a normal map");
                Assert.That(m.GetTexture("_BumpMap"), Is.EqualTo(Load<Texture2D>(NormalPath)), m.name);
                Assert.That(m.IsKeywordEnabled("_SURFACE_MAPS"), Is.True, m.name + ": per-material gloss and edge highlights");
                Assert.That(m.GetTexture("_MaterialMask"), Is.EqualTo(Load<Texture2D>(MaterialMaskPath)), m.name);
                Assert.That(m.GetTexture("_SurfaceMap"), Is.EqualTo(Load<Texture2D>(SurfacePath)), m.name);
                Assert.That(m.GetVector("_MetalParams").y, Is.GreaterThan(m.GetVector("_PolymerParams").y), m.name + ": metal is metallic, polymer is not");
                Assert.That(m.GetFloat("_OutlineMinPixels"), Is.GreaterThanOrEqualTo(1f), m.name + ": no outline thinner than a pixel");
            }

            var baseImporter = (TextureImporter)AssetImporter.GetAtPath(BaseColorPath);
            Assert.That(baseImporter.maxTextureSize, Is.EqualTo(4096));
            Assert.That(baseImporter.textureCompression, Is.EqualTo(TextureImporterCompression.CompressedHQ));
            Assert.That(((TextureImporter)AssetImporter.GetAtPath(NormalPath)).textureType, Is.EqualTo(TextureImporterType.NormalMap));
            Assert.That(Load<Texture2D>(BaseColorPath).width, Is.EqualTo(4096));
            foreach (string data in new[] { SurfacePath, MaterialMaskPath })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(data);
                Assert.That(importer.sRGBTexture, Is.False, data + " holds data, not colour");
            }

            Assert.That(viewModel.GetFloat("_OutlineWidth"), Is.LessThan(world.GetFloat("_OutlineWidth") * 0.5f));
            Assert.That(Spawn(PrefabPath).GetComponentsInChildren<MeshRenderer>().All(r => r.sharedMaterial == world), Is.True);
        }

        /// <summary>
        /// The view-model camera is the last camera of the player's stack: post-processing runs there once for the whole
        /// stack, so tonemapping, bloom and SMAA reach the rifle and arms (on the world camera they ran before the overlay
        /// drew them). The Handheld tier renders at native resolution (0.833 + FSR stair-stepped the rifle).
        /// </summary>
        [Test]
        public void PlayerCameraStack_PostProcessesAndAntiAliasesTheViewModel()
        {
            GameObject player = Spawn(PlayerPath);
            Camera[] cameras = player.GetComponentsInChildren<Camera>(true);
            Camera world = cameras.Single(c => c.name == "PlayerCamera");
            Camera viewModel = cameras.Single(c => c.name == "ViewModelCamera");
            var worldData = new SerializedObject(world.GetComponent("UniversalAdditionalCameraData"));
            var viewModelData = new SerializedObject(viewModel.GetComponent("UniversalAdditionalCameraData"));
            Assert.That(worldData.FindProperty("m_RenderPostProcessing").boolValue, Is.False, "post-processing runs once, on the last camera");
            Assert.That(viewModelData.FindProperty("m_RenderPostProcessing").boolValue, Is.True, "the overlay (last camera) post-processes the stack");
            Assert.That(viewModelData.FindProperty("m_CameraType").intValue, Is.EqualTo(1), "the view model is an overlay camera");
            Assert.That(worldData.FindProperty("m_Antialiasing").intValue, Is.EqualTo(2), "SMAA (the base camera's setting applies to the stack)");
            Assert.That(worldData.FindProperty("m_AntialiasingQuality").intValue, Is.EqualTo(2), "SMAA High");

            var handheld = new SerializedObject(Load<Object>("Assets/_Game/Settings/Rendering/URP_Handheld.asset"));
            Assert.That(handheld.FindProperty("m_RenderScale").floatValue, Is.EqualTo(1f).Within(0.001f), "Handheld renders at native resolution");
        }

        [Test]
        public void MaterialMask_CoversEveryPaintAndMetal()
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(System.IO.File.ReadAllBytes(MaterialMaskPath));
            Color[] pixels = texture.GetPixels();
            Object.DestroyImmediate(texture);
            // share of each class across the whole atlas (gutters included): all present, none taking over
            var share = new float[4];
            foreach (Color p in pixels)
            {
                share[0] += p.r;
                share[1] += p.g;
                share[2] += p.b;
                share[3] += p.a;
            }

            for (int i = 0; i < 4; i++)
            {
                share[i] /= pixels.Length;
                Assert.That(share[i], Is.InRange(0.01f, 0.7f), $"class {i} (white, red, polymer, metal): {share[i]:P1}");
            }
        }

        [Test]
        public void EmissionMap_LightsOnlyTheGlowingParts()
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(System.IO.File.ReadAllBytes(EmissionPath));
            Color32[] pixels = texture.GetPixels32();
            float lit = pixels.Count(p => p.r + p.g + p.b > 30) / (float)pixels.Length;
            Object.DestroyImmediate(texture);
            Assert.That(lit, Is.InRange(0.005f, 0.25f), $"glowing share of the texture: {lit:P1}");
        }

        [Test]
        public void Player_HoldsTheRifle_InFirstPerson_AndInTheBodysRightHand()
        {
            GameObject player = Spawn(PlayerPath);

            // First person: the rifle model under the view model, its muzzle used by the weapon and the skills.
            Transform viewModel = player.GetComponentsInChildren<Transform>(true).First(t => t.name == "ViewModel");
            GameObject fpRifle = viewModel.GetComponentsInChildren<Transform>(true).Select(t => t.gameObject).First(IsFireRifle);
            Transform fpMuzzle = fpRifle.transform.Find("Muzzle");
            var hitscan = new SerializedObject(player.GetComponent<HitscanWeapon>());
            var caster = new SerializedObject(player.GetComponent<SkillCaster>());
            Assert.That(hitscan.FindProperty("muzzle").objectReferenceValue, Is.EqualTo(fpMuzzle), "HitscanWeapon.muzzle");
            Assert.That(caster.FindProperty("castOrigin").objectReferenceValue, Is.EqualTo(fpMuzzle), "SkillCaster.castOrigin");
            foreach (Renderer r in fpRifle.GetComponentsInChildren<Renderer>(true))
            {
                Assert.That(r.gameObject.layer, Is.EqualTo(GameLayers.ViewModelIndex), "drawn by the view-model camera");
                Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(r.sharedMaterial, Is.EqualTo(Load<Material>(ViewModelMaterialPath)));
            }

            // Third person: the same rifle on the body's Weapon bone (the clips animate it, hand IK holds it), seen
            // (like the body) only as a shadow.
            Animator body = player.transform.Find("BodyVisual").GetComponent<Animator>();
            Transform weaponBone = body.GetComponentsInChildren<Transform>(true).First(t => t.name == "Weapon");
            Transform held = weaponBone.Cast<Transform>().FirstOrDefault(t => IsFireRifle(t.gameObject));
            Assert.That(held, Is.Not.Null, "rifle under the Weapon bone");
            Assert.That(held.localPosition.magnitude, Is.LessThan(0.001f), "the Weapon bone is the grip");
            foreach (Renderer r in held.GetComponentsInChildren<Renderer>(true))
            {
                Assert.That(r.shadowCastingMode, Is.EqualTo(ShadowCastingMode.ShadowsOnly));
                Assert.That(r.gameObject.layer, Is.EqualTo(player.layer));
            }

            // In the shared rifle pose (Combat_Idle) the grip sits in the right palm and the muzzle points forward.
            body.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            body.Rebind();
            body.Update(0f);
            body.Update(0.1f);
            Transform rightHand = body.GetBoneTransform(HumanBodyBones.RightHand);
            Transform forearm = body.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Vector3 palm = rightHand.position + (rightHand.position - forearm.position).normalized * 0.05f;
            Assert.That(Vector3.Distance(held.position, palm), Is.LessThan(0.1f), "grip in the palm");
            Assert.That(Vector3.Dot(held.forward, player.transform.forward), Is.GreaterThan(0.8f), "muzzle forward");
        }
    }
}
