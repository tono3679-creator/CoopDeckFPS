using System;
using UnityEngine;

namespace CoopDeck.Characters
{
    /// <summary>
    /// Which character the local player plays. There is no selection screen yet: the launch argument
    /// <c>-character &lt;id&gt;</c> (e.g. <c>-character LightningRabbit</c>) picks it in a build, the editor menu
    /// CoopDeck &gt; Play As in the Editor, and tests set <see cref="Override"/>. Empty means the player prefab's own body.
    /// </summary>
    public static class CharacterSelection
    {
        public const string Argument = "-character";
        public const string EditorPrefKey = "CoopDeck.Character";

        /// <summary>Set by tests (and anything that picks a character in code); wins over the argument and the menu.</summary>
        public static string Override { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Override = null;

        public static string Requested
        {
            get
            {
                if (!string.IsNullOrEmpty(Override))
                {
                    return Override;
                }

                string fromArgs = FromArguments(Environment.GetCommandLineArgs());
                if (!string.IsNullOrEmpty(fromArgs))
                {
                    return fromArgs;
                }
#if UNITY_EDITOR
                return UnityEditor.EditorPrefs.GetString(EditorPrefKey, string.Empty);
#else
                return string.Empty;
#endif
            }
        }

        /// <summary>The id after <c>-character</c> (or <c>-character=id</c>), empty when absent.</summary>
        public static string FromArguments(string[] args)
        {
            if (args == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }

                if (a.StartsWith(Argument + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return a.Substring(Argument.Length + 1);
                }
            }

            return string.Empty;
        }
    }
}
