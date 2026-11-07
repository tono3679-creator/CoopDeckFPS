using System.IO;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    /// <summary>Project paths used by the generators. Generated assets live under Assets/_Game.</summary>
    public static class GamePaths
    {
        public const string Root = "Assets/_Game";
        public const string EnvironmentMaterials = Root + "/Art/Environments/Materials";
        public const string EnvironmentTextures = Root + "/Art/Environments/Textures";
        public const string CharacterMaterials = Root + "/Art/Characters/Materials";
        public const string WeaponMaterials = Root + "/Art/Weapons/Materials";
        public const string VfxMaterials = Root + "/Art/VFX/Materials";
        public const string VfxShader = Root + "/Art/VFX/Shaders/FX_AdditiveSoft.shader";
        public const string InputActions = Root + "/Settings/Input/GameControls.inputactions";
        public const string Rendering = Root + "/Settings/Rendering";
        public const string WeaponData = Root + "/ScriptableObjects/Weapons";
        public const string CharacterData = Root + "/ScriptableObjects/Characters";
        public const string VfxPrefabs = Root + "/Prefabs/VFX";
        public const string CharacterPrefabs = Root + "/Prefabs/Characters";
        public const string EnemyPrefabs = Root + "/Prefabs/Enemies";
        public const string SandboxScene = Root + "/Scenes/Sandbox_Phase1.unity";
    }

    public static class EditorUtil
    {
        public static void EnsureFolder(string assetFolder)
        {
            if (AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetFolder)?.Replace('\\', '/');
            string name = Path.GetFileName(assetFolder);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, name);
        }

        public static bool Exists(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null;
        }

        // Serialized private fields are set through SerializedObject so components keep their encapsulation.

        public static void SetObject(Object target, string property, Object value)
        {
            Edit(target, property, p => p.objectReferenceValue = value);
        }

        public static void SetFloat(Object target, string property, float value)
        {
            Edit(target, property, p => p.floatValue = value);
        }

        public static void SetInt(Object target, string property, int value)
        {
            Edit(target, property, p => p.intValue = value);
        }

        public static void SetString(Object target, string property, string value)
        {
            Edit(target, property, p => p.stringValue = value);
        }

        public static void SetBool(Object target, string property, bool value)
        {
            Edit(target, property, p => p.boolValue = value);
        }

        public static void SetVector3(Object target, string property, Vector3 value)
        {
            Edit(target, property, p => p.vector3Value = value);
        }

        public static void SetObjectArray(Object target, string property, Object[] values)
        {
            Edit(target, property, p =>
            {
                p.arraySize = values.Length;
                for (int i = 0; i < values.Length; i++)
                {
                    p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
                }
            });
        }

        static void Edit(Object target, string property, System.Action<SerializedProperty> apply)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogError($"[CoopDeck] Property '{property}' not found on {target.GetType().Name} ({target.name}).");
                return;
            }

            apply(p);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        /// <summary>Primitive used as a visual only: no collider, optional shadow settings.</summary>
        public static GameObject VisualPrimitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
            Vector3 localScale, Material material, Vector3 localEuler = default, bool castShadows = true)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localEulerAngles = localEuler;
            go.transform.localScale = localScale;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
    }
}
