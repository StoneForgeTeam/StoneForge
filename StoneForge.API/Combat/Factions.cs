namespace StoneForge;

/// <summary>Units' factions, as the game keeps them for who fights whom: a unit added to its faction's list
/// (scr_faction_map_add - enemies hostile to it go for it, its allies don't) or taken out.</summary>
public static class Factions
{
    /// <summary>Puts a unit in its faction's list (its own faction variables say which).</summary>
    public static void Join(Instance unit) => Game.CallScript("scr_faction_map_add", unit, unit);

    /// <summary>Takes a unit out of its faction's list.</summary>
    public static void Leave(Instance unit) => Game.CallScript("scr_faction_map_remove", unit, unit);
}
