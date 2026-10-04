namespace StoneForge;

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

    /// <summary>Sets a value of the cell's dungeon as the game does (scr_globaltile_dungeon_set) - made if the cell has
    /// none yet.</summary>
    public void SetDungeonValue(string key, GmValue value) => Game.CallScript("scr_globaltile_dungeon_set", default, key, value, X, Y);

    /// <summary>Sets a map of the cell's dungeon (scr_globaltile_dungeon_set_map), which the dungeon then owns.</summary>
    public void SetDungeonMap(string key, DsMap map) => Game.CallScript("scr_globaltile_dungeon_set_map", default, key, map, X, Y);

    /// <summary>Sets a list of the cell's dungeon (scr_globaltile_dungeon_set_list), which the dungeon then owns.</summary>
    public void SetDungeonList(string key, DsList list) => Game.CallScript("scr_globaltile_dungeon_set_list", default, key, list, X, Y);

    private DsMap? Layer(int layer) => Game.CallScript("scr_globaltile_get_tile", default, X, Y, layer, false).AsDsMap;
}
