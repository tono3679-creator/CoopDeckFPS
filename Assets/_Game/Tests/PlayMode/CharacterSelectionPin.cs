using CoopDeck.Characters;
using NUnit.Framework;

/// <summary>
/// Every PlayMode test plays the Fox Gunner unless it picks a character itself (LightningRabbitPlayModeTests), so the
/// Editor's CoopDeck &gt; Play As choice never changes what the existing tests see. Outside any namespace: applies to
/// the whole test assembly.
/// </summary>
[SetUpFixture]
public class CharacterSelectionPin
{
    [OneTimeSetUp]
    public void PinFox() => CharacterSelection.Override = "FoxGunner";

    [OneTimeTearDown]
    public void Unpin() => CharacterSelection.Override = null;
}
