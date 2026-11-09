using System;
using CoopDeck.Animation;
using CoopDeck.Skills;
using CoopDeck.Weapons;
using UnityEngine;

namespace CoopDeck.Characters
{
    /// <summary>
    /// Puts the selected character's body and first-person view into the player before anything else wakes up.
    /// The player prefab is built with the first character's body (the Fox Gunner) and keeps it when that one, or
    /// an unknown id, is selected. For a character with a <see cref="CharacterKit"/> the built-in body and view-model
    /// parts are removed, the kit's body goes under the player root and its view-model parts under the view-model
    /// root, and the animator driver, the weapon's muzzle and the caster's hands are pointed at the new parts.
    /// The character's starting weapon (if it has one) replaces the weapon data; motor, health and deck are not touched.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class PlayerCharacterSwap : MonoBehaviour
    {
        [SerializeField] CharacterDefinition[] characters = new CharacterDefinition[0];

        [Header("Built-in parts (replaced by a kit)")]
        [SerializeField] GameObject builtInBody;
        [SerializeField] GameObject[] builtInViewModelParts = new GameObject[0];

        [Header("Player")]
        [SerializeField] Transform viewModelRoot;
        [SerializeField] Transform eye;
        [SerializeField] CharacterAnimatorDriver driver;
        [SerializeField] MonoBehaviour locomotionSource;
        [SerializeField, Min(0.1f)] float walkSpeed = 6.5f;
        [SerializeField] HitscanWeapon weapon;
        [SerializeField] SkillCaster caster;
        [SerializeField] Camera viewModelCamera;
        [SerializeField] Camera worldCamera;

        CharacterDefinition current;

        /// <summary>The character in use (the first one until a kit was applied).</summary>
        public CharacterDefinition Current => current != null ? current : characters.Length > 0 ? characters[0] : null;

        public CharacterKit ActiveKit { get; private set; }

        public void Configure(CharacterDefinition[] available, GameObject body, GameObject[] viewModelParts, Transform viewModel,
            Transform eyes, CharacterAnimatorDriver animatorDriver, MonoBehaviour locomotion, float walk, HitscanWeapon gun,
            SkillCaster skillCaster, Camera viewModelCam, Camera worldCam)
        {
            characters = available;
            builtInBody = body;
            builtInViewModelParts = viewModelParts;
            viewModelRoot = viewModel;
            eye = eyes;
            driver = animatorDriver;
            locomotionSource = locomotion;
            walkSpeed = walk;
            weapon = gun;
            caster = skillCaster;
            viewModelCamera = viewModelCam;
            worldCamera = worldCam;
        }

        void Awake() => Apply(CharacterSelection.Requested);

        public CharacterDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            foreach (CharacterDefinition c in characters)
            {
                if (c != null && string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return null;
        }

        /// <summary>
        /// Applies a character once (at Awake). Nothing selected or an unknown id means the built-in character (the
        /// first, the fox): its body stays and its starting weapon still applies. Returns false when the body stays built in.
        /// </summary>
        bool Apply(string id)
        {
            CharacterDefinition chosen = Find(id);
            if (chosen == null)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    Debug.LogWarning($"[CoopDeck] Unknown character '{id}': keeping {Current?.DisplayName ?? "the built-in body"}.");
                }

                chosen = characters.Length > 0 ? characters[0] : null;
                if (chosen == null)
                {
                    return false;
                }
            }

            current = chosen;
            if (weapon != null && chosen.StartingWeapon != null)
            {
                weapon.SetDefinition(chosen.StartingWeapon); // the character's own gun (stats, effects, on-hit ability)
            }

            if (chosen.KitPrefab == null)
            {
                return false;
            }

            Remove(builtInBody);
            foreach (GameObject part in builtInViewModelParts)
            {
                Remove(part);
            }

            // Instantiated under the player so the body's components find the animator driver as they wake up.
            CharacterKit kit = Instantiate(chosen.KitPrefab, transform, false);
            kit.name = chosen.KitPrefab.name;
            ActiveKit = kit;
            if (kit.Body != null)
            {
                kit.Body.transform.SetParent(transform, false);
            }

            if (kit.ViewModel != null && viewModelRoot != null)
            {
                while (kit.ViewModel.childCount > 0)
                {
                    kit.ViewModel.GetChild(0).SetParent(viewModelRoot, false);
                }
            }

            if (driver != null && kit.BodyAnimator != null)
            {
                driver.Configure(kit.BodyAnimator, locomotionSource, walkSpeed, kit.ClipStrideSpeed);
                driver.ConfigureRings(kit.Rings, kit.MaxBlendSpeed, kit.ReloadClipSeconds);
            }

            if (kit.Arms != null)
            {
                kit.Arms.SetDriver(driver);
                kit.Arms.SetShoulders(eye, kit.LeftShoulderFromEye, kit.RightShoulderFromEye, kit.TorsoYaw, kit.TorsoLean);
            }

            if (weapon != null && kit.Muzzle != null)
            {
                weapon.SetMuzzle(kit.Muzzle);
            }

            if (caster != null)
            {
                if (kit.Muzzle != null)
                {
                    caster.SetCastOrigin(kit.Muzzle);
                }

                caster.SetCastHand(kit.Arms != null ? kit.Arms.Hand(true) : null, viewModelCamera, worldCamera);
            }

            foreach (ICharacterKitListener listener in kit.GetComponents<ICharacterKitListener>())
            {
                listener.OnKitApplied(driver); // the kit's own extras (the rabbit's lightning) follow this player's driver
            }

            return true;
        }

        static void Remove(GameObject part)
        {
            if (part == null)
            {
                return;
            }

            part.SetActive(false); // inactive first: its components never wake up
            part.name += " (removed)"; // Destroy waits for the end of the frame: keep it out of name lookups
            Destroy(part);
        }
    }
}
