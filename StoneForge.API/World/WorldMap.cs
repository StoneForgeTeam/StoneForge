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

/// <summary>Which of a world-map cell's two stores a value is read from (<see cref="WorldTile.Get"/>).</summary>
public enum TileLayer
{
    /// <summary>The saved value, else the generated one - as the game reads a cell.</summary>
    Any,
    /// <summary>What the save keeps for the cell (the game's globalmapLocationsDataMap): its seeds, its dungeon, what's
    /// happened there.</summary>
    Saved,
    /// <summary>What the world map generated for it from the world seed (the game's terrainGrid), made again each game and
    /// never saved.</summary>
    Generated,
}

/// <summary>One cell of the world map (<see cref="WorldMap.Tile"/>): its values - the saved ones over those the map
/// generated, as the game reads them - its areas' seeds, its location and its dungeon. Writes go through the game's own
/// scripts (scr_globaltile_set...), so anything hooking them sees them.</summary>
public readonly record struct WorldTile(int X, int Y)
{
    /// <summary>The game's name for the cell, "x_y".</summary>
    public string Tag => $"{X}_{Y}";

    /// <summary>A value of the cell, saved or else generated (undefined if it has neither). Setting one sets the saved
    /// value (scr_globaltile_set), kept with the save.</summary>
    public GmValue this[string key]
    {
        get => Get(key);
        set => Game.CallScript("scr_globaltile_set", default, key, value, X, Y, 0);
    }

    /// <summary>A value of the cell from <paramref name="layer"/> (undefined if it has none there).</summary>
    public GmValue Get(string key, TileLayer layer = TileLayer.Any)
    {
        if (layer != TileLayer.Generated && Saved is { } saved && saved.Has(key))
            return saved[key];
        if (layer != TileLayer.Saved && Generated is { } generated && generated.Has(key))
            return generated[key];
        return GmValue.Undefined;
    }

    /// <summary>The cell's saved values, the map the game keeps for it (null if it has none yet).</summary>
    public DsMap? Saved => Layer(0);

    /// <summary>The values the world map generated for the cell (null if it has none).</summary>
    public DsMap? Generated => Layer(1);

    /// <summary>The seeds its areas are built from.</summary>
    public TileSeeds Seeds => new(this);

    /// <summary>The location here, by the game's key ("Osbrook", "RoadsideCemetery"...); null if there's none.</summary>
    public string? Location => Get("Location") is { Kind: GmKind.String } location ? location.AsString : null;

    /// <summary>The cell's dungeon; null if it has none.</summary>
    public WorldDungeon? Dungeon => Saved is { } saved && saved.GetMap("dungeon") is { } map ? new WorldDungeon(this, map) : null;

    private DsMap? Layer(int layer) => Game.CallScript("scr_globaltile_get_tile", default, X, Y, layer, false).AsDsMap;
}

/// <summary>The seeds a world-map cell's areas are built from (scr_globaltile_seed_get): the same seed, the same area.
/// Each is -1 while it hasn't been set (the cell's not been visited), and -2 when it's to be rolled again (its area
/// respawns).</summary>
public readonly struct TileSeeds
{
    /// <summary>The game's names for them.</summary>
    public static readonly IReadOnlyList<string> Names = new[] { "seed", "growSeed", "mobsSeed", "presetSeed", "containersSeed", "Trade_Seed" };

    private readonly WorldTile _tile;

    internal TileSeeds(WorldTile tile) => _tile = tile;

    /// <summary>The layout ("seed").</summary>
    public double Layout => this["seed"];
    /// <summary>What grows ("growSeed").</summary>
    public double Growth => this["growSeed"];
    /// <summary>Which mobs ("mobsSeed").</summary>
    public double Mobs => this["mobsSeed"];
    /// <summary>Which room presets ("presetSeed").</summary>
    public double Preset => this["presetSeed"];
    /// <summary>What's in its containers ("containersSeed").</summary>
    public double Containers => this["containersSeed"];
    /// <summary>What its traders have ("Trade_Seed").</summary>
    public double Trade => this["Trade_Seed"];

    /// <summary>A seed by the game's name (<see cref="Names"/>), saved; -1 if it hasn't been set.</summary>
    public double this[string name] => _tile.Get(name, TileLayer.Saved) is { Kind: GmKind.Real } seed ? seed.AsReal : -1;
}

/// <summary>A world-map cell's dungeon (<see cref="WorldTile.Dungeon"/>): its values - its floors' seeds and saved graphs,
/// its boss, whether it's open, its reset timer, its contract... - as the save keeps them. Writes go through the game's own
/// scripts (scr_globaltile_dungeon_set...), so anything hooking them sees them.</summary>
public sealed class WorldDungeon
{
    internal WorldDungeon(WorldTile tile, DsMap values)
    {
        Tile = tile;
        Values = values;
    }

    /// <summary>The cell it's on.</summary>
    public WorldTile Tile { get; }

    /// <summary>All of its values, the map the save keeps.</summary>
    public DsMap Values { get; }

    /// <summary>A value (undefined if it hasn't one). Setting it goes through scr_globaltile_dungeon_set.</summary>
    public GmValue this[string key]
    {
        get => Values[key];
        set => Game.CallScript("scr_globaltile_dungeon_set", default, key, value, Tile.X, Tile.Y);
    }

    /// <summary>Its value names.</summary>
    public IReadOnlyList<GmValue> Keys => Values.Keys;

    /// <summary>A nested map (its saved floor graphs, "saveGraphMap"; which rooms dropped what, "roomsDropMap"...); null if
    /// it has none by that name.</summary>
    public DsMap? GetMap(string key) => Values.GetMap(key);

    /// <summary>A nested list ("dungeon_levels"); null if it has none by that name.</summary>
    public DsList? GetList(string key) => Values.GetList(key);

    /// <summary>Puts <paramref name="map"/> in as a nested map (scr_globaltile_dungeon_set_map): the dungeon owns it from
    /// then on, and one there before is destroyed.</summary>
    public void SetMap(string key, DsMap map) => Game.CallScript("scr_globaltile_dungeon_set_map", default, key, map, Tile.X, Tile.Y);

    /// <summary>Puts <paramref name="list"/> in as a nested list (scr_globaltile_dungeon_set_list): the dungeon owns it from
    /// then on, and one there before is destroyed.</summary>
    public void SetList(string key, DsList list) => Game.CallScript("scr_globaltile_dungeon_set_list", default, key, list, Tile.X, Tile.Y);
}
