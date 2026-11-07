namespace CoopDeck.Combat
{
    /// <summary>
    /// Marks a registered combatant that never fights back (shooting-range dummies). AI allies
    /// only attack one when the team is already shooting it, and it never counts as "in combat".
    /// </summary>
    public interface ITrainingTarget
    {
    }
}
