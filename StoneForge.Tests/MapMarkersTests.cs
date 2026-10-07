using StoneForge;

// The world map's markers with the map closed (MapMarkers): read from and written to the save's list, four values
// each, as the game keeps them; and the player's placing and removing them, as events (laid out with FakeGame's lists,
// globals and room).
public class MapMarkersTests : FakeGame
{
    private const int ChoiceObject = 300, PointerObject = 301, ChoiceId = 100_010, PointerId = 100_020, HeldId = 100_030;
    private readonly FakeDs _ds = new();
    private readonly FakeWorld _world = new();
    private readonly ModContext _context = new("markers_test");
    private readonly DsList _saved;

    public MapMarkersTests()
    {
        Ds = _ds;
        World = _world;
        _world.LendsIds = true;
        _saved = DsList.Create();
        Globals["globalmapUserMarksList"] = _saved.Id;
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        Globals.Remove("globalmapUserMarksList");
        base.Dispose();
    }


    [Fact]
    public void The_markers_are_read_from_the_save_four_values_each()
    {
        foreach (GmValue value in new GmValue[] { "s_glmap_mark_user_Flag", 0, 1570.5, 630, "s_glmap_mark_user_Skull", 2, 12, 40 })
            _saved.Add(value);

        var markers = MapMarkers.All();

        Assert.False(MapMarkers.MapOpen);
        Assert.Equal(new[]
        {
            new MapMarker("s_glmap_mark_user_Flag", 0, new Point(1570.5, 630)),
            new MapMarker("s_glmap_mark_user_Skull", 2, new Point(12, 40)),
        }, markers);
        Assert.Equal(new WorldTile(30, 12), markers[0].Tile);
    }

    [Fact]
    public void Setting_adding_and_removing_write_the_save()
    {
        var flag = new MapMarker(MapMarkers.Sprites[4], 0, new Point(1570.5, 630));
        var chest = new MapMarker("s_glmap_mark_user_Chest", 0, new Point(52, 104));

        MapMarkers.Set(new[] { flag });
        MapMarkers.Add(chest);

        Assert.Equal(8, _saved.Count);
        Assert.Equal("s_glmap_mark_user_Flag", _saved[0].AsString);
        Assert.Equal(1570.5, _saved[2].AsReal);
        Assert.Equal(new[] { flag, chest }, MapMarkers.All());
        Assert.True(MapMarkers.Remove(flag));
        Assert.False(MapMarkers.Remove(flag));
        Assert.Equal(new[] { chest }, MapMarkers.All());
        MapMarkers.Set(Array.Empty<MapMarker>());
        Assert.Equal(0, _saved.Count);
    }

    [Fact]
    public void The_players_placing_and_removing_markers_are_events()
    {
        var flag = new MapMarker("s_glmap_mark_user_Flag", 0, new Point(1570.5, 630));
        var skull = new MapMarker("s_glmap_mark_user_Skull", 0, new Point(1572, 631));
        var placed = new List<MapMarker>();
        var removed = new List<MapMarker>();
        MapMarkers.OnPlaced(_context, placed.Add);
        MapMarkers.OnRemoved(_context, removed.Add);
        MapMarkers.Set(new[] { flag });
        _world.Add(ChoiceId, ChoiceObject);
        _world.Vars[ChoiceId] = new() { ["guiInteractiveState"] = 2 };

        // A choice in the map's menu clicked: the skull placed - over the flag, which the game takes off.
        RunCode("gml_Object_o_globalmapMarkUserContext_Other_25", ChoiceId, () => MapMarkers.Set(new[] { skull }));
        Assert.Equal(new[] { skull }, placed);
        Assert.Equal(new[] { flag }, removed);

        // (Hovering the choice - another state of the same event: nothing.)
        _world.Vars[ChoiceId]["guiInteractiveState"] = 0;
        RunCode("gml_Object_o_globalmapMarkUserContext_Other_25", ChoiceId, () => { });
        Assert.Single(placed);

        // A right-click with the map's pointer on the skull: it's taken off.
        _world.Add(HeldId, (int)GameObjectId.o_globalmapMarkUser);
        _world.Vars[HeldId] = new() { ["object_index"] = (int)GameObjectId.o_globalmapMarkUser };
        _world.Add(PointerId, PointerObject);
        _world.Vars[PointerId] = new() { ["guiInteractiveState"] = 5, ["markID"] = HeldId };
        RunCode("gml_Object_o_globalmapInteractiveNormal_Other_25", PointerId, () => MapMarkers.Set(Array.Empty<MapMarker>()));
        Assert.Equal(new[] { flag, skull }, removed);
        Assert.Single(placed);
    }
}
