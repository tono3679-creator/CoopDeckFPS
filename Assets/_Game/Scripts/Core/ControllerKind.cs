namespace CoopDeck.Core
{
    /// <summary>Who issues a character's <see cref="CharacterCommands"/>.</summary>
    public enum ControllerKind : byte
    {
        None = 0,

        /// <summary>A local human (devices → commands).</summary>
        Human = 1,

        /// <summary>An NPC brain on this machine (later: on the host only).</summary>
        AI = 2,

        /// <summary>Commands received over the network (Phase 6).</summary>
        Remote = 3,

        /// <summary>Tests, autopilot and other scripted sources.</summary>
        Scripted = 4,
    }

    /// <summary>
    /// Optional on an <see cref="ICharacterCommandSource"/>: says what kind of controller it is, so
    /// HUD and tools can find the local human without knowing the concrete class.
    /// </summary>
    public interface IControllerIdentity
    {
        ControllerKind Kind { get; }
    }
}
