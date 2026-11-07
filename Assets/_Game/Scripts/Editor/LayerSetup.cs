using CoopDeck.Core;
using UnityEditor;
using UnityEngine;

namespace CoopDeck.EditorTools
{
    /// <summary>Writes the game's user layers into ProjectSettings/TagManager.asset.</summary>
    public static class LayerSetup
    {
        public static void EnsureLayers()
        {
            Object tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var tagManager = new SerializedObject(tagManagerAsset);
            SerializedProperty layers = tagManager.FindProperty("layers");

            SetLayer(layers, GameLayers.PlayerIndex, GameLayers.PlayerName);
            SetLayer(layers, GameLayers.EnemyIndex, GameLayers.EnemyName);
            SetLayer(layers, GameLayers.ViewModelIndex, GameLayers.ViewModelName);

            tagManager.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetLayer(SerializedProperty layers, int index, string name)
        {
            SerializedProperty layer = layers.GetArrayElementAtIndex(index);
            if (layer.stringValue == name)
            {
                return;
            }

            if (!string.IsNullOrEmpty(layer.stringValue))
            {
                Debug.LogWarning($"[CoopDeck] Layer {index} is '{layer.stringValue}', renaming to '{name}'.");
            }

            layer.stringValue = name;
        }
    }
}
