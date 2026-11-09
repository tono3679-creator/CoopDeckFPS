using CoopDeck.Characters;
using UnityEditor;

namespace CoopDeck.EditorTools
{
    /// <summary>
    /// CoopDeck &gt; Play As: which character the local player plays when Play is pressed in the Editor
    /// (<see cref="CharacterSelection"/>; in a build the launch argument -character &lt;id&gt; does the same).
    /// Stored per user in EditorPrefs. PlayMode tests pin their own character, so the choice never changes them.
    /// </summary>
    public static class CharacterSelectionMenu
    {
        const string FoxItem = "CoopDeck/Play As/Fox Gunner";
        const string RabbitItem = "CoopDeck/Play As/Lightning Rabbit";

        static string Current => EditorPrefs.GetString(CharacterSelection.EditorPrefKey, string.Empty);

        [MenuItem(FoxItem, priority = 40)]
        static void PlayFox() => EditorPrefs.DeleteKey(CharacterSelection.EditorPrefKey);

        [MenuItem(RabbitItem, priority = 41)]
        static void PlayRabbit() => EditorPrefs.SetString(CharacterSelection.EditorPrefKey, LightningRabbitAssetFactory.Id);

        [MenuItem(FoxItem, true)]
        static bool FoxChecked()
        {
            Menu.SetChecked(FoxItem, Current != LightningRabbitAssetFactory.Id);
            return true;
        }

        [MenuItem(RabbitItem, true)]
        static bool RabbitChecked()
        {
            Menu.SetChecked(RabbitItem, Current == LightningRabbitAssetFactory.Id);
            return true;
        }
    }
}
