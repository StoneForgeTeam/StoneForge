namespace StoneForge;

/// <summary>The game's log of what happens (the actions log, bottom left): a unit's name as it writes it, and a line of
/// its own by key.</summary>
public static class ActionsLog
{
    /// <summary>A unit's name as the log writes it (scr_actionsLogGetName: coloured by who it is).</summary>
    public static GmValue NameOf(Instance unit) => Game.CallScript("scr_actionsLogGetName", default, unit);

    /// <summary>One of the game's log lines (scr_actionsLog): its key ("knockback", "noDamage"...) and the values it
    /// fills in.</summary>
    public static void Write(string key, params GmValue[] values)
    {
        using var array = GmArray.From(values);
        Game.CallScript("scr_actionsLog", default, key, array);
    }
}
