namespace StoneForge;

/// <summary>The player's character: its attributes as the game reads them (scr_atr - level, head, name...), whether enemies
/// are after it, XP as the game gives it, its own stats and psyche, and walking it somewhere as a click does.
/// <code>
/// if (!Player.InCombat)
///     Player.GiveXp(50);
/// Game.Log($"Level {Player.Level}");
/// </code>
/// Game thread only, in a game (<see cref="Exists"/>).</summary>
public static class Player
{
    /// <summary>Its instance (o_player); none with no game.</summary>
    public static Instance Instance => Instances.All(GameObjectId.o_player).FirstOrDefault();

    /// <summary>Whether there's a player: a game is being played.</summary>
    public static bool Exists => !Instance.IsNone;

    /// <summary>One of its attributes, as the game reads them (scr_atr - mods hooking it included): "LVL", "Head",
    /// "nameKey", "STR"...</summary>
    public static GmValue Attribute(string name) => Game.CallScript("scr_atr", default, name);

    /// <summary>Its level.</summary>
    public static int Level => (int)Attribute("LVL").AsReal;

    /// <summary>The share of its maximum health it can have now, in % (the game's Health_Threshold: wounds and hunger lower
    /// it) - 100 without one. Its HUD's bar fills to max_hp × this / 100.</summary>
    public static double HealthCap => Instance.Get("Health_Threshold") is { Kind: GmKind.Real } cap ? cap.AsReal : 100;

    /// <summary>The share of its maximum energy it can have now, in % (Max_Energy_Threshold) - 100 without one.</summary>
    public static double EnergyCap => Instance.Get("Max_Energy_Threshold") is { Kind: GmKind.Real } cap ? cap.AsReal : 100;

    /// <summary>Whether enemies are after it - in combat, as the game counts it (its hostile mobs: scr_getAgredMobsCount).</summary>
    public static bool InCombat => Exists && Game.CallScript("scr_getAgredMobsCount", default, true).AsReal > 0;

    /// <summary>Whether a unit is after it (scr_isMobAgred).</summary>
    public static bool IsHuntedBy(Instance unit) => Game.CallScript("scr_isMobAgred", default, unit).AsBool;

    /// <summary>Gives it XP as the game does (scr_get_XP: its XP bonuses, levelling up). With <paramref name="killed"/>,
    /// the combat log says so as for a kill of that unit. The XP it got.</summary>
    public static double GiveXp(double xp, Instance? killed = null)
    {
        double gained = Game.CallScript("scr_get_XP", default, xp).AsReal;
        if (gained > 0 && killed is { IsNone: false } unit && unit.Exists)
        {
            using var name = GmArray.From(new[] { Game.CallScript("scr_actionsLogGetName", default, unit) });
            Game.CallScript("scr_actionsLogXP", default, "death", name, gained);
        }
        return gained;
    }

    /// <summary>The XP a unit's death is worth to it, as the game works it out (o_enemy's Destroy): the unit's gain_xp,
    /// less for a unit of a lower tier than its level.</summary>
    public static double KillXp(Instance unit)
        => unit.Get("gain_xp").AsReal * Math.Min(1 - 0.15 * (Level / 5.0 - unit.Get("Tier").AsReal), 1);

    /// <summary>Walks it to a cell, as a click on the world does (scr_player_move: its path, one cell a turn).</summary>
    public static void WalkTo(Cell cell)
    {
        if (Instance is { IsNone: false } player)
            Game.CallScript("scr_player_move", player, cell.Center.X, cell.Center.Y);
    }

    /// <summary>Takes it across the edge of the area to the next one of the world map, as walking off it does
    /// (scr_playerTileborderTransition).</summary>
    public static void CrossAreaEdge()
    {
        if (Instance is { IsNone: false } player)
            Game.CallScript("scr_playerTileborderTransition", player);
    }

    /// <summary>Adds to one of its character stats (the statistics page: "contractsFailed", "attacks"...).</summary>
    public static void AddStat(string stat, double amount = 1) => Game.CallScript("scr_characterStatsUpdateAdd", default, stat, amount);

    /// <summary>Changes its psyche as the game does (scr_psy_change): "MoraleSituational", "Sanity"... by
    /// <paramref name="amount"/>, for <paramref name="reason"/> (the game's key for why, shown in its log).</summary>
    public static void ChangePsyche(string what, double amount, string reason) => Game.CallScript("scr_psy_change", default, what, amount, reason);
}
