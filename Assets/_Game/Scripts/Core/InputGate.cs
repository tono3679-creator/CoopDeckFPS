using System.Collections.Generic;
using UnityEngine;

namespace CoopDeck.Core
{
    /// <summary>
    /// Menus that take over the local player's input (e.g. the NPC deck editor) register here.
    /// While any blocker is open the human controller issues no commands and frees the cursor.
    /// </summary>
    public static class InputGate
    {
        static readonly HashSet<object> Blockers = new HashSet<object>();

        public static bool IsBlocked => Blockers.Count > 0;

        public static void Block(object owner)
        {
            if (owner != null)
            {
                Blockers.Add(owner);
            }
        }

        public static void Release(object owner)
        {
            if (owner != null)
            {
                Blockers.Remove(owner);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Blockers.Clear();
        }
    }
}
