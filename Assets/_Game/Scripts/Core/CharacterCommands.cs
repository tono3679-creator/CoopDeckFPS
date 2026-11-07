using UnityEngine;

namespace CoopDeck.Core
{
    /// <summary>
    /// One frame of intent for a character. A human input reader, an NPC brain, a test script
    /// or (later) the network layer produces it; the character simulation only ever reads this.
    /// </summary>
    public struct CharacterCommands
    {
        public const int SkillSlotCount = 3;

        /// <summary>Planar movement, x = strafe right, y = forward. Magnitude 0..1.</summary>
        public Vector2 Move;

        /// <summary>View rotation to apply this frame, in degrees. x = yaw (right +), y = pitch (up +).</summary>
        public Vector2 LookDelta;

        public bool JumpPressed;
        public bool SprintHeld;
        public bool DashPressed;
        public bool FireHeld;
        public bool FirePressed;
        public bool ReloadPressed;

        /// <summary>Bit i set = skill slot i was pressed this frame.</summary>
        public int SkillPressedMask;

        public bool UseItemPressed;
        public bool CycleItemPressed;

        public bool IsSkillPressed(int slot) => (SkillPressedMask & (1 << slot)) != 0;

        public void SetSkillPressed(int slot)
        {
            SkillPressedMask |= 1 << slot;
        }
    }

    /// <summary>
    /// Anything that can drive a character: local input, NPC AI, an autopilot, a network proxy.
    /// </summary>
    public interface ICharacterCommandSource
    {
        /// <summary>Called once per simulation step by the character that owns this source.</summary>
        CharacterCommands GetCommands(float deltaTime);
    }

    /// <summary>
    /// Where a character aims from, and how weapon recoil is fed back into its view.
    /// Implemented by the first-person look for humans and by the aim controller for NPCs.
    /// </summary>
    public interface IAimProvider
    {
        Ray GetAimRay();
        void AddRecoil(Vector2 kickDegrees);
    }
}
