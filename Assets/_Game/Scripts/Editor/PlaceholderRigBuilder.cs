using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace CoopDeck.EditorTools
{
    /// <summary>Bones of the placeholder full-body rig (paths are what the generated clips animate).</summary>
    public sealed class RigParts
    {
        public const string HipsPath = "Hips";
        public const string SpinePath = "Hips/Spine";
        public const string HeadPath = "Hips/Spine/Head";
        public const string ShoulderLPath = "Hips/Spine/Shoulder_L";
        public const string ShoulderRPath = "Hips/Spine/Shoulder_R";
        public const string HipLPath = "Hips/Hip_L";
        public const string HipRPath = "Hips/Hip_R";

        public static readonly string[] UpperBodyPaths = { SpinePath, HeadPath, ShoulderLPath, ShoulderRPath };
        public static readonly string[] LowerBodyPaths = { HipsPath, HipLPath, HipRPath };

        public Transform Visual;
        public Transform Hips;
        public Transform Spine;
        public Transform Head;
        public Transform ShoulderL;
        public Transform ShoulderR;
        public Transform HipL;
        public Transform HipR;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public readonly List<Renderer> BodyRenderers = new List<Renderer>();
    }

    /// <summary>
    /// A blocky humanoid (pelvis, torso, head, arms, legs) on a Generic bone hierarchy. It exists so
    /// the Animator, blend tree, layers and rig constraints are real now; a skinned model with the
    /// same parameter names replaces it later (ANIMATION_GUIDELINES.md).
    /// </summary>
    public static class PlaceholderRigBuilder
    {
        public const float HipsHeight = 0.95f;

        public static RigParts Build(Transform parent, Material body, Material limbs, Material head, Material accent, bool shadowsOnly)
        {
            var rig = new RigParts();
            rig.Visual = Bone("Visual", parent, Vector3.zero);
            rig.Hips = Bone("Hips", rig.Visual, new Vector3(0f, HipsHeight, 0f));
            rig.Spine = Bone("Spine", rig.Hips, new Vector3(0f, 0.1f, 0f));
            rig.Head = Bone("Head", rig.Spine, new Vector3(0f, 0.62f, 0f));
            rig.ShoulderL = Bone("Shoulder_L", rig.Spine, new Vector3(-0.3f, 0.52f, 0f));
            rig.ShoulderR = Bone("Shoulder_R", rig.Spine, new Vector3(0.3f, 0.52f, 0f));
            rig.HipL = Bone("Hip_L", rig.Hips, new Vector3(-0.12f, -0.05f, 0f));
            rig.HipR = Bone("Hip_R", rig.Hips, new Vector3(0.12f, -0.05f, 0f));

            Mesh(rig, PrimitiveType.Cube, "Pelvis", rig.Hips, Vector3.zero, new Vector3(0.36f, 0.18f, 0.22f), body, shadowsOnly, true);
            Mesh(rig, PrimitiveType.Capsule, "Torso", rig.Spine, new Vector3(0f, 0.3f, 0f), new Vector3(0.46f, 0.32f, 0.3f), body, shadowsOnly, true);
            Mesh(rig, PrimitiveType.Sphere, "HeadMesh", rig.Head, new Vector3(0f, 0.18f, 0f), Vector3.one * 0.36f, head, shadowsOnly, true);
            Mesh(rig, PrimitiveType.Cube, "Visor", rig.Head, new Vector3(0f, 0.2f, 0.16f), new Vector3(0.26f, 0.08f, 0.06f), accent, shadowsOnly, false);
            Mesh(rig, PrimitiveType.Capsule, "Arm_L", rig.ShoulderL, new Vector3(0f, -0.28f, 0f), new Vector3(0.13f, 0.3f, 0.13f), limbs, shadowsOnly, true);
            Mesh(rig, PrimitiveType.Capsule, "Arm_R", rig.ShoulderR, new Vector3(0f, -0.28f, 0f), new Vector3(0.13f, 0.3f, 0.13f), limbs, shadowsOnly, true);
            Mesh(rig, PrimitiveType.Capsule, "Leg_L", rig.HipL, new Vector3(0f, -0.44f, 0f), new Vector3(0.16f, 0.44f, 0.16f), limbs, shadowsOnly, true);
            Mesh(rig, PrimitiveType.Capsule, "Leg_R", rig.HipR, new Vector3(0f, -0.44f, 0f), new Vector3(0.16f, 0.44f, 0.16f), limbs, shadowsOnly, true);
            return rig;
        }

        static Transform Bone(string name, Transform parent, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        static void Mesh(RigParts rig, PrimitiveType type, string name, Transform bone, Vector3 position, Vector3 scale, Material material,
            bool shadowsOnly, bool isBody)
        {
            GameObject go = EditorUtil.VisualPrimitive(type, name, bone, position, scale, material);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = shadowsOnly ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            rig.Renderers.Add(renderer);
            if (isBody)
            {
                rig.BodyRenderers.Add(renderer);
            }
        }
    }
}
