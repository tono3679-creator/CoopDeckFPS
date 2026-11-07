using UnityEngine;

namespace CoopDeck.Core
{
    /// <summary>
    /// Physics/render layer names. The editor bootstrap writes them into the TagManager at the
    /// indices below; runtime code resolves them by name so a moved index only breaks loudly.
    /// </summary>
    public static class GameLayers
    {
        public const string PlayerName = "Player";
        public const string EnemyName = "Enemy";
        public const string ViewModelName = "ViewModel";

        public const int PlayerIndex = 6;
        public const int EnemyIndex = 7;
        public const int ViewModelIndex = 8;

        public static int Player => LayerMask.NameToLayer(PlayerName);
        public static int Enemy => LayerMask.NameToLayer(EnemyName);
        public static int ViewModel => LayerMask.NameToLayer(ViewModelName);

        /// <summary>What hitscan shots from a player can hit: everything but players, view models and UI.</summary>
        public static int PlayerShotMask => Without(Physics.DefaultRaycastLayers, Player, ViewModel, LayerMask.NameToLayer("UI"));

        /// <summary>Area queries for skills: every character and target, allies included (teams are filtered in code).</summary>
        public static int SkillQueryMask => Without(Physics.DefaultRaycastLayers, ViewModel, LayerMask.NameToLayer("UI"));

        /// <summary>What counts as ground or walls for character movement queries.</summary>
        public static int MovementBlockingMask => Without(Physics.DefaultRaycastLayers, Player, ViewModel, LayerMask.NameToLayer("UI"));

        static int Without(int mask, params int[] layers)
        {
            foreach (int layer in layers)
            {
                if (layer >= 0)
                {
                    mask &= ~(1 << layer);
                }
            }

            return mask;
        }
    }
}
