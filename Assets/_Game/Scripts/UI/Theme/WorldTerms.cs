namespace CoopDeck.UI
{
    /// <summary>
    /// World names shown by the UI. ALL PROVISIONAL (仮設定, Docs/UI_DESIGN.md §1): change them
    /// here only. If a confirmed name appears elsewhere in the project, that name wins.
    /// </summary>
    public static class WorldTerms
    {
        /// <summary>The world.</summary>
        public const string World = "エルディア";

        /// <summary>The four-element energy of the ancient civilisation.</summary>
        public const string Energy = "原脈";

        /// <summary>The player's exploration corps.</summary>
        public const string Corps = "灯守隊";

        /// <summary>The home base.</summary>
        public const string Base = "継ぎ木港";

        /// <summary>Areas torn by the ancient device's malfunction.</summary>
        public const string Rift = "裂界";

        /// <summary>The once-per-run specialised skill.</summary>
        public const string Specialisation = "原脈同調";

        /// <summary>The sandbox scene (training ground at the base).</summary>
        public const string TrainingGround = Base + " 訓練区画";

        /// <summary>The sandbox's enemy arena.</summary>
        public const string TrainingArena = "模擬裂界 闘技区画";
    }
}
