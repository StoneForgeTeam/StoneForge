namespace StoneForge;

/// <summary>The world map: which cell the player is on, each cell's values (<see cref="Tile"/>) - its areas' seeds,
/// its location, its dungeon - and where the player is as one string (<see cref="Place"/>). Game thread only, and only
/// on the world map: none on the main menu, nor in the prologue, which has a map of its own (<see cref="Available"/>).</summary>
public static class WorldMap
{
    /// <summary>Whether there's a world map to read: a game loaded or begun, out of the prologue.</summary>
    public static bool Available
        => Game.Global["playerGridX"] is { Kind: GmKind.Real } x && x.AsReal != -4
            && Game.Global["globalmapLocationsDataMap"].AsDsMap != null;

    /// <summary>Whether the game is in the prologue, whose map isn't the world map (the game's playerGridX -4).</summary>
    public static bool InPrologue => Game.Global["playerGridX"] is { Kind: GmKind.Real } x && x.AsReal == -4;

    /// <summary>How many cells the world map is across and down.</summary>
    public static int Width => Game.Global["worldWidth"].AsInt;
    public static int Height => Game.Global["worldHeight"].AsInt;

    /// <summary>The cell the player is on (the whole area they're in - its rooms, its dungeon's floors); null without a
    /// world map.</summary>
    public static (int X, int Y)? PlayerCell
        => Available ? (Game.Global["playerGridX"].AsInt, Game.Global["playerGridY"].AsInt) : null;

    /// <summary>The dungeon floor the player is on: 0 on the surface (the game's floor_counter).</summary>
    public static int Floor => Game.Global["floor_counter"] is { Kind: GmKind.Real } floor ? floor.AsInt : 0;

    /// <summary>Where the player is, as one string, the same in every game for the same spot: the room's name,
    /// "#f&lt;floor&gt;" in a dungeon (every floor of one is built in the same room), and "@x_y", the world-map cell
    /// (neighbouring cells are built in the same room too) - "r_globalmap_forest#f2@12_7". Null with no player (the main
    /// menu). Without a world map (the prologue) it's the room and floor alone.</summary>
    public static string? Place
    {
        get
        {
            if (Game.CallBuiltin("instance_find", (int)GameObjectId.o_player, 0).AsInstance.IsNone)
                return null;
            string place = Game.CallBuiltin("room_get_name", Gm.Room).AsString;
            if (Floor > 0)
                place += "#f" + Floor;
            if (PlayerCell is var (x, y))
                place += $"@{x}_{y}";
            return place;
        }
    }

    /// <summary>The world-map cell at (<paramref name="x"/>, <paramref name="y"/>). Outside the map throws, as does no
    /// world map.</summary>
    public static WorldTile Tile(int x, int y)
    {
        if (!Available)
            throw new InvalidOperationException("There's no world map: no game is loaded or begun, or it's the prologue (WorldMap.Available).");
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is off the world map ({Width} x {Height}).");
        return new WorldTile(x, y);
    }

    /// <summary>The cell the player is on; null without a world map.</summary>
    public static WorldTile? Here => PlayerCell is var (x, y) ? new WorldTile(x, y) : null;
}
