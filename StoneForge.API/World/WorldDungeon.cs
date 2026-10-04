namespace StoneForge;

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
