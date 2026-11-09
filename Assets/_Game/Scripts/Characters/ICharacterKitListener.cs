using CoopDeck.Animation;

namespace CoopDeck.Characters
{
    /// <summary>
    /// A component on a <see cref="CharacterKit"/>'s root that wants to know when <see cref="PlayerCharacterSwap"/> has
    /// put the kit into a player (its body and view-model parts are then in place, driven by <paramref name="driver"/>).
    /// </summary>
    public interface ICharacterKitListener
    {
        void OnKitApplied(CharacterAnimatorDriver driver);
    }
}
