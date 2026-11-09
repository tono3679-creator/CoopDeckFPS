using CoopDeck.Core;
using UnityEngine;

namespace CoopDeck.Characters
{
    /// <summary>
    /// A playable character: its id (the launch argument -character &lt;id&gt;), name, element for the HUD, and the visual
    /// kit that replaces the player prefab's built-in body. A null kit means the body built into the player prefab
    /// (the Fox Gunner). A starting weapon replaces the player prefab's weapon data; movement, health and deck are shared for now.
    /// </summary>
    [CreateAssetMenu(menuName = "CoopDeck/Character Definition", fileName = "CD_Character")]
    public class CharacterDefinition : ScriptableObject
    {
        [SerializeField] string id = "FoxGunner";
        [SerializeField] string displayName = "Fox Gunner";
        [SerializeField] Element element = Element.Fire;
        [Tooltip("Body, held weapon and first-person view. Empty: the body already built into the player prefab.")]
        [SerializeField] CharacterKit kitPrefab;
        [Tooltip("The character's own starting weapon (stats, effects, on-hit ability). Empty: the player prefab's weapon.")]
        [SerializeField] CoopDeck.Weapons.WeaponDefinition startingWeapon;

        public string Id => id;
        public string DisplayName => displayName;
        public Element Element => element;
        public CharacterKit KitPrefab => kitPrefab;
        public CoopDeck.Weapons.WeaponDefinition StartingWeapon => startingWeapon;

        /// <summary>Set by the generator after the weapon data exists (Configure keeps the weapon).</summary>
        public void SetStartingWeapon(CoopDeck.Weapons.WeaponDefinition weapon)
        {
            startingWeapon = weapon;
        }

        public void Configure(string characterId, string name, Element characterElement, CharacterKit kit)
        {
            id = characterId;
            displayName = name;
            element = characterElement;
            kitPrefab = kit;
        }
    }
}
