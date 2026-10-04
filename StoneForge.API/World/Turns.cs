namespace StoneForge;

/// <summary>The game's turns, run by hand - for a mod that keeps a world's clock itself (another game's, say): the world's
/// own turn (time passing, effects, regeneration: scr_global_turn) and the units' (their AI, each in the player's list
/// of units to run: o_player's alarm 4).</summary>
public static class Turns
{
    /// <summary>The world's turn passes (scr_global_turn, as the player): time, the effects on everyone, regeneration.</summary>
    public static void PassWorld()
    {
        if (Player.Instance is { IsNone: false } player)
            Game.CallScript("scr_global_turn", player);
    }

    /// <summary>The units take their turns (o_player's alarm 4: every unit in its list of units to run, in turn).</summary>
    public static void RunUnits()
    {
        if (Player.Instance is { IsNone: false } player)
            Game.CallBuiltinAs("event_perform", player, player, 2, 4);
    }
}
