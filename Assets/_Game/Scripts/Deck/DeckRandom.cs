using System;
using System.Collections.Generic;

namespace CoopDeck.Deck
{
    /// <summary>
    /// Injected randomness for the deck. Same seed + same operations = same deck order, which
    /// network sync, replays and bug reports rely on.
    /// </summary>
    public interface IRandom
    {
        /// <summary>Integer in [minInclusive, maxExclusive).</summary>
        int Range(int minInclusive, int maxExclusive);
    }

    public sealed class SeededRandom : IRandom
    {
        readonly Random random;

        public SeededRandom(int seed)
        {
            Seed = seed;
            random = new Random(seed);
        }

        public int Seed { get; }

        public int Range(int minInclusive, int maxExclusive) => random.Next(minInclusive, maxExclusive);
    }

    public static class ShuffleUtility
    {
        /// <summary>Unbiased Fisher–Yates shuffle.</summary>
        public static void Shuffle<T>(IList<T> list, IRandom random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}
