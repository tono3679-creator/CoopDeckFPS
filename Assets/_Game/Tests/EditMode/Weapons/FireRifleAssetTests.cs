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
        public void Mesh_FitsTheBudget_AndCarriesOutlineNormals()
        {
            Mesh mesh = Spawn(PrefabPath).GetComponentInChildren<MeshFilter>().sharedMesh;
            Assert.That(mesh.triangles.Length / 3, Is.LessThanOrEqualTo(10000));
            Assert.That(mesh.subMeshCount, Is.EqualTo(1));

            var outline = new List<Vector3>();
            mesh.GetUVs(3, outline);
            Assert.That(outline.Count, Is.EqualTo(mesh.vertexCount), "UV3 = averaged normals for the outline pass");
            Assert.That(outline.All(n => Mathf.Abs(n.magnitude - 1f) < 0.01f), Is.True, "unit normals");
            // Hard edges were kept: some vertices' own normals differ from the averaged ones.
            Vector3[] normals = mesh.normals;
            int split = Enumerable.Range(0, normals.Length).Count(i => Vector3.Dot(normals[i], outline[i]) < 0.95f);
            Assert.That(split, Is.GreaterThan(mesh.vertexCount / 20), "hard-surface edges are kept");
        }

        [Test]
        public void Materials_AreToon_WithEmission_AndAThinnerFirstPersonOutline()
        {
            var world = Load<Material>(MaterialPath);
            var viewModel = Load<Material>(ViewModelMaterialPath);
            foreach (Material m in new[] { world, viewModel })
            {
                Assert.That(m.shader.name, Is.EqualTo("CoopDeck/Character/Toon"), m.name);
                Assert.That(m.IsKeywordEnabled("_EMISSION"), Is.True, m.name);
                Assert.That(m.IsKeywordEnabled("_OUTLINE_UV3_NORMALS"), Is.True, m.name);
                Assert.That(m.GetTexture("_BaseMap"), Is.Not.Null, m.name);
                Assert.That(m.GetTexture("_EmissionMap"), Is.Not.Null, m.name);
                Assert.That(m.GetColor("_EmissionColor").maxColorComponent, Is.GreaterThan(1f), m.name + ": HDR so bloom (threshold 1) catches it");
            }

            Assert.That(viewModel.GetFloat("_OutlineWidth"), Is.LessThan(world.GetFloat("_OutlineWidth") * 0.5f));
            Assert.That(Spawn(PrefabPath).GetComponentInChildren<MeshRenderer>().sharedMaterial, Is.EqualTo(world));
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
