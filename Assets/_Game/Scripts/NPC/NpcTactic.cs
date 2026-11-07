namespace CoopDeck.NPC
{
    /// <summary>Combat policy the player sets for NPC allies (V / R3 cycles it).</summary>
    public enum NpcTactic : byte
    {
        /// <summary>Balanced: shooting, skills, dodging, combos when they come up.</summary>
        Auto = 0,

        /// <summary>Attack the enemy the player marked (or the player's target) first.</summary>
        Focus = 1,

        /// <summary>Finish elements allies applied; hold matching cards longer for them.</summary>
        ComboFirst = 2,
    }

    /// <summary>High-level mode of the NPC state machine.</summary>
    public enum NpcMode : byte
    {
        Follow = 0,
        Engage = 1,
        Regroup = 2,
        Rescue = 3,
        Evade = 4,
        Downed = 5,
    }

    /// <summary>What the utility layer picked this think.</summary>
    public enum NpcActionKind : byte
    {
        None = 0,
        Shoot = 1,
        SingleSkill = 2,
        AreaSkill = 3,
        Support = 4,
        ComboSetup = 5,
        ComboFinish = 6,
        Revive = 7,
        Evade = 8,
        Regroup = 9,
        Follow = 10,
    }

    public static class NpcLabels
    {
        public static string Tactic(NpcTactic tactic)
        {
            switch (tactic)
            {
                case NpcTactic.Focus: return "集中攻撃";
                case NpcTactic.ComboFirst: return "コンボ優先";
                default: return "自動戦闘";
            }
        }

        public static NpcTactic Next(NpcTactic tactic) =>
            tactic == NpcTactic.Auto ? NpcTactic.Focus : tactic == NpcTactic.Focus ? NpcTactic.ComboFirst : NpcTactic.Auto;

        public static string Mode(NpcMode mode)
        {
            switch (mode)
            {
                case NpcMode.Engage: return "交戦";
                case NpcMode.Regroup: return "合流";
                case NpcMode.Rescue: return "救助";
                case NpcMode.Evade: return "回避";
                case NpcMode.Downed: return "ダウン";
                default: return "追従";
            }
        }

        public static string Action(NpcActionKind action)
        {
            switch (action)
            {
                case NpcActionKind.Shoot: return "射撃";
                case NpcActionKind.SingleSkill: return "スキル";
                case NpcActionKind.AreaSkill: return "範囲スキル";
                case NpcActionKind.Support: return "支援";
                case NpcActionKind.ComboSetup: return "コンボの起点";
                case NpcActionKind.ComboFinish: return "コンボ追撃";
                case NpcActionKind.Revive: return "蘇生";
                case NpcActionKind.Evade: return "回避";
                case NpcActionKind.Regroup: return "合流";
                case NpcActionKind.Follow: return "追従";
                default: return "待機";
            }
        }
    }
}
