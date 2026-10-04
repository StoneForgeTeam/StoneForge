namespace StoneForge;

/// <summary>The ways out of a place - doors, stairs, a dungeon's entrance and exit (the game's o_transitions_door and its
/// children) - found and used as the player clicking one does.</summary>
public static class Doors
{
    private static int _doors = -2;

    /// <summary>The way out nearest a room position; none if the room has none.</summary>
    public static Instance Nearest(double x, double y)
    {
        if (_doors == -2)
            _doors = Gm.AssetGetIndex("o_transitions_door");
        return Instances.Nearest(x, y, _doors);
    }

    /// <summary>Uses a way out as the player clicking it does: through it at once when the player can reach it from where
    /// it stands (scr_can_interract_posgrid), else walked to and then through (scr_delay_move_grid).</summary>
    public static void Use(Instance door)
    {
        if (door.IsNone || !door.Exists)
            return;
        if (Game.CallScript("scr_can_interract_posgrid", door, door, door.Get("in_grid")).AsBool)
            Game.CallBuiltinAs("event_user", door, door, 0);
        else
            Game.CallScript("scr_delay_move_grid", door);
    }
}
