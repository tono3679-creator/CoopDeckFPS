namespace CoopDeck.Core
{
    /// <summary>Skill elements. Order is part of save/network data: append only.</summary>
    public enum Element : byte
    {
        None = 0,
        Fire = 1,
        Ice = 2,
        Lightning = 3,
        Gravity = 4,
    }

    public enum TeamId : byte
    {
        Neutral = 0,
        Players = 1,
        Enemies = 2,
    }
}
