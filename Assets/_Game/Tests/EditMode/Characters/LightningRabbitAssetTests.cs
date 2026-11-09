using System;
using System.IO;
using System.Linq;
using CoopDeck.Animation;
using CoopDeck.Characters;
using CoopDeck.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations.Rigging;
using Object = UnityEngine.Object;

namespace CoopDeck.Tests
{
    /// <summary>
    /// The Lightning Rabbit and its Lightning SMG as Unity sees them (Docs/LIGHTNING_RABBIT.md): the high-detail body
    /// with three LODs and a valid humanoid avatar, every fox motion re-solved for the rabbit as a humanoid clip that
    /// leaves the springs (ears, scarf, tail) alone, the same controller layout and parameters as the fox, the body's
    /// hand IK and springs, the first-person arms cut from its sculpt, the high-detail SMG, and the character
    /// definitions and kit the player swaps in.
    /// </summary>
    public class LightningRabbitAssetTests
    {
        const string Art = "Assets/_Game/Art/Characters/LightningRabbit";
        const string ModelPath = Art + "/Models/LightningRabbit.fbx";
        const string ArmsPath = Art + "/Models/LightningRabbit_FPArms.fbx";
        const string AnimFolder = "Assets/_Game/Animations/LightningRabbit";
        const string SetPath = AnimFolder + "/A_LightningRabbit_Set.fbx";
        const string TablePath = AnimFolder + "/A_LightningRabbit_Clips.json";
        const string ControllerPath = AnimFolder + "/AC_LightningRabbit_TP.controller";
        const string ClipMaskPath = AnimFolder + "/AM_LightningRabbit_ClipImport.mask";
        const string UpperMaskPath = AnimFolder + "/AM_LightningRabbit_UpperBody.mask";
        const string FoxControllerPath = "Assets/_Game/Animations/FoxGunner/AC_FoxGunner_TP.controller";
        const string BodyPrefabPath = "Assets/_Game/Prefabs/Characters/PF_LightningRabbit_Body.prefab";
        const string KitPath = "Assets/_Game/Prefabs/Characters/PF_CharacterKit_LightningRabbit.prefab";
        const string PlayerPath = "Assets/_Game/Prefabs/Characters/PF_Player.prefab";
        const string SmgPrefabPath = "Assets/_Game/Prefabs/Weapons/PF_Weapon_LightningSMG.prefab";
        const string SmgModelPath = "Assets/_Game/Art/Weapons/LightningSMG/Models/LightningSMG.fbx";
        const string RabbitDefinitionPath = "Assets/_Game/ScriptableObjects/Characters/CD_LightningRabbit.asset";
        const string FoxDefinitionPath = "Assets/_Game/ScriptableObjects/Characters/CD_FoxGunner.asset";

        [Serializable]
        class Table
        {
            public Entry[] clips;
        }

        [Serializable]
        class Entry
        {
            public string name;
            public bool loop;
        }

        static T Load<T>(string path) where T : Object
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            Assert.That(asset, Is.Not.Null, path + " missing: run the bootstrap");
            return asset;
        }

        static int Triangles(Mesh mesh) => (int)Enumerable.Range(0, mesh.subMeshCount).Sum(i => (long)mesh.GetIndexCount(i)) / 3;

        [Test]
        public void Body_IsTheHighDetailRebuild_WithThreeLods_AndAHumanoidAvatar()
        {
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            Assert.That(avatar != null && avatar.isValid && avatar.isHuman, Is.True, "humanoid avatar");
            var prefab = Load<GameObject>(BodyPrefabPath);
            SkinnedMeshRenderer[] skins = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int Tris(int lod) => Triangles(skins.First(s => s.name.EndsWith("_LOD" + lod)).sharedMesh);
            Assert.That(Tris(0), Is.InRange(150000, 170000), "LOD0: the 160k body (no silent reduction)");
            Assert.That(Tris(1), Is.InRange(35000, 45000));
            Assert.That(Tris(2), Is.InRange(12000, 18000));
            LODGroup group = prefab.GetComponent<LODGroup>();
            Assert.That(group != null && group.lodCount == 3, Is.True, "a LOD group with the three meshes");

            var texture = Load<Texture2D>(Art + "/Textures/T_LightningRabbit_BaseColor.png");
            Assert.That(texture.width, Is.EqualTo(4096));
            var normal = (TextureImporter)AssetImporter.GetAtPath(Art + "/Textures/T_LightningRabbit_Normal.png");
            Assert.That(normal.textureType, Is.EqualTo(TextureImporterType.NormalMap));
            foreach (SkinnedMeshRenderer skin in skins.Where(s => !s.name.EndsWith("_Glow"))) // glow copies: LightningRabbitLookTests
            {
                Assert.That(skin.sharedMaterials.Select(m => m.name), Is.EqualTo(new[] { "M_LightningRabbit_SoftToon", "M_LightningRabbit_Outline" }));
                Assert.That(skin.sharedMaterials[0].GetTexture("_BaseMap"), Is.SameAs(texture));
            }
        }

        [Test]
        public void Clips_AreTheFoxSet_ResolvedForTheRabbit_AndLeaveTheSpringsAlone()
        {
            Table table = JsonUtility.FromJson<Table>(File.ReadAllText(TablePath));
            Table fox = JsonUtility.FromJson<Table>(File.ReadAllText("Assets/_Game/Animations/FoxGunner/A_FoxGunner_Clips.json"));
            Assert.That(table.clips.Select(c => c.name.Replace("A_LightningRabbit_", "")),
                Is.EquivalentTo(fox.clips.Select(c => c.name.Replace("A_FoxGunner_", ""))), "every fox motion exists for the rabbit");

            var clips = AssetDatabase.LoadAllAssetsAtPath(SetPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__")).ToDictionary(c => c.name);
            foreach (Entry e in table.clips)
            {
                Assert.That(clips.TryGetValue(e.name, out AnimationClip clip) && clip.isHumanMotion, Is.True, e.name + " is a humanoid clip");
                Assert.That(clip.isLooping, Is.EqualTo(e.loop), e.name + " loop flag");
                string[] paths = AnimationUtility.GetCurveBindings(clip).Select(b => b.path).ToArray();
                Assert.That(paths.Any(p => p.EndsWith("/Weapon")), Is.True, e.name + " moves the SMG (Weapon bone)");
                Assert.That(paths.Any(p => p.Contains("/Ear_") || p.Contains("/Scarf_") || p.Contains("/Tail")), Is.False,
                    e.name + " leaves the ears, scarf and tail to the springs");
            }

            var upper = Load<AvatarMask>(UpperMaskPath);
            bool Active(AvatarMask mask, string leaf) => Enumerable.Range(0, mask.transformCount)
                .Any(i => mask.GetTransformPath(i).EndsWith("/" + leaf) && mask.GetTransformActive(i));
            Assert.That(Active(upper, "LeftHandIndex2") && Active(upper, "Weapon") && Active(upper, "IK_Hand_L"), Is.True,
                "the upper-body layers carry the fingers, the SMG and the hand goals");
            Assert.That(Active(Load<AvatarMask>(ClipMaskPath), "Scarf_F1"), Is.False, "the scarf is not imported from the clips");
        }

        [Test]
        public void Controller_HasTheFoxLayoutAndParameters()
        {
            var rabbit = Load<AnimatorController>(ControllerPath);
            var fox = Load<AnimatorController>(FoxControllerPath);
            Assert.That(rabbit.layers.Select(l => l.name), Is.EqualTo(fox.layers.Select(l => l.name)));
            Assert.That(rabbit.parameters.Select(p => (p.name, p.type)), Is.EqualTo(fox.parameters.Select(p => (p.name, p.type))));
            foreach (AnimatorControllerLayer layer in rabbit.layers)
            {
                foreach (ChildAnimatorState s in layer.stateMachine.states)
                {
                    Assert.That(s.state.motion != null || s.state.name == "Empty", Is.True, $"{layer.name}/{s.state.name} has a motion");
                    if (s.state.motion is AnimationClip clip)
                    {
                        Assert.That(clip.name, Does.StartWith("A_LightningRabbit_"), $"{layer.name}/{s.state.name} plays a rabbit clip");
                    }
                }
            }
        }

        [Test]
        public void BodyPrefab_HasHandIk_LookAt_AndSprings()
        {
            var prefab = Load<GameObject>(BodyPrefabPath);
            var builder = prefab.GetComponent<RigBuilder>();
            Assert.That(builder.layers.Single().rig.GetComponentsInChildren<TwoBoneIKConstraint>().Length, Is.EqualTo(2), "hand IK");
            Assert.That(prefab.GetComponent<FoxBodyAnimator>(), Is.Not.Null, "look-at and muzzle correction");
            var tail = new SerializedObject(prefab.GetComponent<FoxSecondaryMotion>()).FindProperty("tail");
            Assert.That(tail.arraySize, Is.EqualTo(2), "the tail's two bones");
            ChainSecondaryMotion.Chain[] chains = prefab.GetComponent<ChainSecondaryMotion>().Chains;
            Assert.That(chains.Select(c => (c.name, c.bones.Length)),
                Is.EquivalentTo(new[] { ("Ear_L", 3), ("Ear_R", 3), ("Scarf_F", 4), ("Scarf_B", 4) }), "three-joint ears and four-joint scarf tails");
            Assert.That(chains.SelectMany(c => c.bones).All(b => b != null), Is.True);
        }

        [Test]
        public void FirstPersonArms_AreCutFromTheSculpt_WithFiveFingersAndPalmMarkers()
        {
            var arms = Load<GameObject>(ArmsPath);
            SkinnedMeshRenderer skin = arms.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(Triangles(skin.sharedMesh), Is.InRange(38000, 41000), "the sculpt's arms at 40k");
            string[] names = arms.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
            foreach (string side in new[] { "Left", "Right" })
            {
                foreach (string digit in FirstPersonArms.Digits)
                {
                    for (int j = 1; j <= 3; j++)
                    {
                        Assert.That(names, Does.Contain(side + digit + j));
                    }
                }

                Assert.That(names, Does.Contain(side + "Palm"));
                Assert.That(names, Does.Contain(side + "PalmOut"));
            }

            Assert.That(Load<Texture2D>(Art + "/Textures/T_LightningRabbit_FPArms_BaseColor.png").width, Is.EqualTo(4096));
        }

        [Test]
        public void Smg_IsTheHighDetailRebuild_WithSocketsAndGlowingWindows()
        {
            var prefab = Load<GameObject>(SmgPrefabPath);
            MeshFilter[] filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            // rebuild_weapon_hq.py: 55k from the sculpt plus 1 mm bevels on the creases (Docs/WEAPON_VISUAL_QUALITY.md §7)
            Assert.That(Triangles(filters.First(f => f.name.EndsWith("_LOD0")).sharedMesh), Is.InRange(85000, 105000), "LOD0 at about 90k");
            Assert.That(Triangles(filters.First(f => f.name.EndsWith("_LOD1")).sharedMesh), Is.InRange(25000, 31000), "LOD1 at 30k");
            Assert.That(prefab.transform.Find("Muzzle"), Is.Not.Null);
            Transform foregrip = prefab.transform.Find("LeftHandGrip");
            Assert.That(foregrip.localPosition.z, Is.InRange(0.2f, 0.26f), "the vertical foregrip 23 cm ahead of the grip");
            Material material = prefab.GetComponentInChildren<MeshRenderer>().sharedMaterial;
            Assert.That(material.IsKeywordEnabled("_EMISSION") && material.GetTexture("_EmissionMap") != null, Is.True, "glowing windows");
            Assert.That(material.GetTexture("_BaseMap").width, Is.EqualTo(4096));
            Assert.That(material.shader.name, Is.EqualTo("CoopDeck/Weapon/HardSurfaceToon"), "the hard-surface weapon shader");
            Assert.That(material.IsKeywordEnabled("_SURFACE_MAPS") && material.GetTexture("_MaterialMask") != null && material.GetTexture("_SurfaceMap") != null,
                Is.True, "per-material gloss and edge highlights");
            Assert.That(AssetDatabase.LoadAllAssetsAtPath(SmgModelPath).OfType<Mesh>().Count(), Is.EqualTo(2));
        }

        [Test]
        public void Characters_AreDefined_AndThePlayerCanSwapInTheRabbitKit()
        {
            var rabbit = Load<CharacterDefinition>(RabbitDefinitionPath);
            var fox = Load<CharacterDefinition>(FoxDefinitionPath);
            Assert.That((rabbit.Id, rabbit.Element), Is.EqualTo(("LightningRabbit", Element.Lightning)));
            Assert.That((fox.Id, fox.Element, fox.KitPrefab), Is.EqualTo(("FoxGunner", Element.Fire, (CharacterKit)null)));
            Assert.That(rabbit.KitPrefab, Is.SameAs(Load<GameObject>(KitPath).GetComponent<CharacterKit>()));

            CharacterKit kit = rabbit.KitPrefab;
            Assert.That(kit.Body.name, Is.EqualTo("BodyVisual"));
            Assert.That(kit.BodyAnimator.runtimeAnimatorController.name, Is.EqualTo("AC_LightningRabbit_TP"));
            Assert.That(kit.Body.GetComponentsInChildren<Transform>(true).Any(t => t.name == "HeldSMG" && t.parent.name == "Weapon"), Is.True);
            Assert.That(kit.Muzzle.parent.name, Is.EqualTo("LightningSMG"));
            Assert.That(kit.Arms, Is.Not.Null);
            Assert.That(kit.Rings.Length, Is.EqualTo(3), "walk, run and sprint rings");
            Assert.That(kit.Body.GetComponentsInChildren<Renderer>(true).All(r => r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly),
                Is.True, "the local player only sees its own body's shadow");

            var swap = Load<GameObject>(PlayerPath).GetComponent<PlayerCharacterSwap>();
            Assert.That(swap, Is.Not.Null);
            SerializedProperty list = new SerializedObject(swap).FindProperty("characters");
            Assert.That(Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue),
                Is.EqualTo(new Object[] { fox, rabbit }), "the fox is built in, the rabbit swaps in");
        }

        [Test]
        public void LaunchArgument_PicksTheCharacter()
        {
            Assert.That(CharacterSelection.FromArguments(new[] { "game.exe", "-character", "LightningRabbit" }), Is.EqualTo("LightningRabbit"));
            Assert.That(CharacterSelection.FromArguments(new[] { "game.exe", "-character=LightningRabbit", "-perfrun", "30" }), Is.EqualTo("LightningRabbit"));
            Assert.That(CharacterSelection.FromArguments(new[] { "game.exe", "-perfrun", "30" }), Is.Empty);
            Assert.That(CharacterSelection.FromArguments(new[] { "game.exe", "-character" }), Is.Empty);
        }
    }
}
