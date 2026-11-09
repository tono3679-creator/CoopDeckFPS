namespace CoopDeck.Combat
{
    /// <summary>
    /// The body's hit flash (<c>HitFlash</c> on enemies). An effect that shows a hit later than its damage (the
    /// Thunderhop's chained strike, Docs/LIGHTNING_VFX.md) moves the flash to that moment and can soften it so the
    /// outline stays readable. Display only: damage, events and their timing are untouched.
    /// </summary>
    public interface IHitFlashView
    {
        /// <summary>
        /// Replaces the current flash with one of <paramref name="strength"/> (1 = the normal hit flash) that starts
        /// after <paramref name="delay"/> seconds (0 = now). Later damage flashes normally again.
        /// </summary>
        void ShowFlash(float strength, float delay);
    }
}
