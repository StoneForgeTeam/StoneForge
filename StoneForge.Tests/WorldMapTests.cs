using StoneForge;

// The world map (WorldMap, WorldTile, TileSeeds, WorldDungeon): where the player is, a cell's two stores - saved over
// generated, as the game reads them - its seeds and its dungeon, and writes going through the game's own scripts (laid
// out with FakeGame's ds maps and scripts).
public class WorldMapTests : FakeGame
{
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    // Each cell's store by layer (0 saved, 1 generated).
    private readonly Dictionary<(int, int, int), DsMap> _layers = new();
    private readonly List<(string Script, GmValue[] Args)> _writes = new();

    public WorldMapTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        _scripts.Add("scr_globaltile_get_tile", a => _layers.TryGetValue((a[0].AsInt, a[1].AsInt, a[2].AsInt), out var map) ? map : -1);
        foreach (string script in new[] { "scr_globaltile_set", "scr_globaltile_dungeon_set", "scr_globaltile_dungeon_set_map", "scr_globaltile_dungeon_set_list" })
            _scripts.Add(script, a => { _writes.Add((script, a)); return GmValue.Undefined; });
    }

    // A world of width x height with the player on (x, y).
    private void World(int x, int y, int width = 20, int height = 10)
    {
        Globals["playerGridX"] = x;
        Globals["playerGridY"] = y;
        Globals["worldWidth"] = width;
        Globals["worldHeight"] = height;
        Globals["globalmapLocationsDataMap"] = DsMap.Create();
    }

    private DsMap Layer(int x, int y, int layer)
    {
        var map = DsMap.Create();
        _layers[(x, y, layer)] = map;
        return map;
    }

    [Fact]
    public void The_player_is_on_a_cell_of_the_world_map()
    {
        World(12, 7);
        Assert.True(WorldMap.Available);
        Assert.False(WorldMap.InPrologue);
        Assert.Equal(new WorldTile(12, 7), WorldMap.PlayerCell);
        Assert.Equal(new WorldTile(12, 7), WorldMap.Here);
        Assert.Equal("12_7", WorldMap.Here!.Value.Tag);
        Assert.Equal((20, 10), (WorldMap.Width, WorldMap.Height));
    }

    [Fact]
    public void The_prologue_and_the_main_menu_have_no_world_map()
    {
        Assert.False(WorldMap.Available);
        Assert.Null(WorldMap.PlayerCell);
        Assert.Null(WorldMap.Here);
        Assert.Null(WorldMap.Place);
        Assert.Throws<InvalidOperationException>(() => WorldMap.Tile(0, 0));
        World(-4, -4);
        Assert.True(WorldMap.InPrologue);
        Assert.False(WorldMap.Available);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(20, 0)]
    [InlineData(0, 10)]
    public void A_cell_off_the_map_throws(int x, int y)
    {
        World(0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldMap.Tile(x, y));
    }

    [Fact]
    public void A_cell_reads_its_saved_value_over_the_generated_one()
    {
        World(3, 4);
        Layer(3, 4, 0)["Location"] = "Osbrook";
        var generated = Layer(3, 4, 1);
        generated["Location"] = "Forest";
        generated["biome"] = "pine";
        var tile = WorldMap.Tile(3, 4);
        Assert.Equal("Osbrook", tile["Location"].AsString);
        Assert.Equal("Osbrook", tile.Location);
        Assert.Equal("pine", tile["biome"].AsString);
        Assert.Equal("Forest", tile.Get("Location", TileLayer.Generated).AsString);
        Assert.True(tile.Get("biome", TileLayer.Saved).IsUndefined);
        Assert.True(tile["nothing"].IsUndefined);
    }

    [Fact]
    public void A_cell_with_no_values_yet_reads_undefined()
    {
        World(0, 0);
        var tile = WorldMap.Tile(5, 5);
        Assert.Null(tile.Saved);
        Assert.True(tile["Location"].IsUndefined);
        Assert.Null(tile.Location);
        Assert.Null(tile.Dungeon);
        Assert.Equal(-1, tile.Seeds.Layout);
    }

    [Fact]
    public void Setting_a_value_goes_through_scr_globaltile_set_on_the_saved_layer()
    {
        World(0, 0);
        WorldMap.Tile(2, 3)["visited"] = true;
        var (script, args) = Assert.Single(_writes);
        Assert.Equal("scr_globaltile_set", script);
        Assert.Equal(new GmValue[] { "visited", true, 2, 3, 0 }, args);
    }

    [Fact]
    public void Seeds_are_read_from_the_saved_layer()
    {
        World(0, 0);
        var saved = Layer(1, 1, 0);
        saved["seed"] = 123;
        saved["mobsSeed"] = -2;
        Layer(1, 1, 1)["growSeed"] = 999;
        var seeds = WorldMap.Tile(1, 1).Seeds;
        Assert.Equal(123, seeds.Layout);
        Assert.Equal(-2, seeds.Mobs);
        // (A generated value isn't a seed the cell's been given.)
        Assert.Equal(-1, seeds.Growth);
        Assert.Equal(6, TileSeeds.Names.Count);
    }

    [Fact]
    public void A_dungeon_is_read_and_written_through_the_games_scripts()
    {
        World(0, 0);
        var dungeon = DsMap.Create();
        dungeon["boss_alive"] = true;
        var graphs = DsMap.Create();
        dungeon.AddMap("saveGraphMap", graphs);
        Layer(6, 2, 0).AddMap("dungeon", dungeon);

        var d = WorldMap.Tile(6, 2).Dungeon!;
        Assert.True(d["boss_alive"].AsBool);
        Assert.Equal(graphs, d.GetMap("saveGraphMap"));
        Assert.Null(d.GetMap("boss_alive"));
        Assert.Contains((GmValue)"saveGraphMap", d.Keys);

        d["boss_alive"] = false;
        var list = DsList.Create();
        d.SetList("dungeon_levels", list);
        Assert.Equal(new[] { "scr_globaltile_dungeon_set", "scr_globaltile_dungeon_set_list" }, _writes.Select(w => w.Script));
        Assert.Equal(new GmValue[] { "boss_alive", false, 6, 2 }, _writes[0].Args);
        Assert.Equal(new GmValue[] { "dungeon_levels", list.Id, 6, 2 }, _writes[1].Args);
    }
}
