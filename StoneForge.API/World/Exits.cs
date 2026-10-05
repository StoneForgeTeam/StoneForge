namespace StoneForge;

/// <summary>The ways out of a place - an entrance, stairs, a dungeon's way in and out, a map edge (the game's
/// o_transitions_door and its children) - found and used as the player clicking one does. (The doors that open and close
/// in a room are <see cref="Doors"/>.)</summary>
public static class Exits
{
    private static int _exits = -2;

    /// <summary>The way out nearest a room position; none if the room has none.</summary>
    public static Instance Nearest(Point position)
    {
        if (_exits == -2)
            _exits = Gm.AssetGetIndex("o_transitions_door");
        return Instances.Nearest(position.X, position.Y, _exits);
    }

    /// <summary>The way out nearest a cell (its middle).</summary>
    public static Instance Nearest(Cell cell) => Nearest(cell.Center);

    /// <summary>Uses a way out as the player clicking it does: through it at once when the player can reach it from where
    /// it stands (scr_can_interract_posgrid), else walked to and then through (scr_delay_move_grid).</summary>
    public static void Use(Instance exit)
    {
        if (exit.IsNone || !exit.Exists)
            return;
        if (Game.CallScript("scr_can_interract_posgrid", exit, exit, exit.Get("in_grid")).AsBool)
            Game.CallBuiltinAs("event_user", exit, exit, 0);
        else
            Game.CallScript("scr_delay_move_grid", exit);
    }
}
