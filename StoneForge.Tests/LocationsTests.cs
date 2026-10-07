using StoneForge;

// Locations' saved state (Locations, Location, LocationRoom, LocationPreset, LocationState): finding a location's rooms
// and presets, a preset's flags and entities, flags set one at a time through the game's scripts, and a state exported,
// written as JSON and stored back - its tags' types kept (laid out with FakeGame's ds maps and scripts).
public class LocationsTests : FakeGame
{
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    private readonly List<(string Script, GmValue[] Args)> _calls = new();
    private DsMap _all;
    private readonly ModContext _context = new("locations_test");

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    public LocationsTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        // As the game's: a location, room and preset found by tag - made, with no flags and no entities, when asked to.
        _scripts.Add("scr_locationRoomPresetGet", a =>
        {
            bool create = a.Length > 3 && a[3].AsBool;
            DsMap? Nested(DsMap parent, GmValue key)
            {
                if (parent.GetMap(key) is { } found || !create)
                    return parent.GetMap(key);
                var made = DsMap.Create();
                parent.AddMap(key, made);
                return made;
            }
            if (Nested(_all, a[0]) is not { } location || Nested(location, a[1]) is not { } room)
                return -1;
            bool isNew = room.GetMap(a[2]) == null;
            if (Nested(room, a[2]) is not { } preset)
                return -1;
            if (isNew)
            {
                preset["flags"] = 0;
                preset["entitiesDataMapString"] = "N/A";
            }
            return preset.Id;
        });
        _scripts.Add("scr_locationGenerateTag", a => $"{a[0].AsInt}_{a[1].AsInt}");
        foreach (string script in new[] { "scr_locationFlagSet", "scr_locationRoomPresetFlagSet", "scr_locationRoomPresetFlagUnset", "scr_locationRoomPresetDelete" })
            _scripts.Add(script, a => { _calls.Add((script, a)); return GmValue.Undefined; });
    }

    [Fact]
    public void The_place_the_player_leaves_is_told_once_its_saved()
    {
        const int SaverId = 100_001;
        Game();
        Osbrook("default", LocationFlags.None, """{"mobsMap": {"static": {}}}""");
        var world = new FakeWorld { LendsIds = true };
        World = world;
        world.Add(SaverId, 300);
        world.Vars[SaverId] = new() { ["locationTag"] = "12_7", ["roomTag"] = "r_global", ["presetTag"] = "default" };
        var saved = new List<LocationPreset>();
        Locations.OnSaved(_context, saved.Add);

        RunCode("gml_Object_o_roomEntitySaver_Other_12", SaverId);

        var preset = Assert.Single(saved);
        Assert.Equal(("12_7", "r_global", "default"), (preset.Location, preset.Room.AsString, preset.Tag.AsString));
        Assert.Equal("""{"mobsMap": {"static": {}}}""", preset.EntitiesJson);
    }

    // A game whose saved locations are these.
    private void Game()
    {
        _all = DsMap.Create();
        Globals["locationsRoomsDataMap"] = _all;
    }

    // A preset of location "12_7", room "r_global", with flags and entities.
    private DsMap Osbrook(GmValue preset, LocationFlags flags, string? entities)
    {
        var state = new LocationState("12_7", "r_global", preset, flags, entities);
        Assert.True(Locations.Store(state));
        return _all.GetMap("12_7")!.Value.GetMap("r_global")!.Value.GetMap(preset)!.Value;
    }

    [Fact]
    public void A_location_is_found_by_its_rooms_and_presets()
    {
        Game();
        Osbrook("default", LocationFlags.Mobs | LocationFlags.Doors, """{"mobsMap": {"static": {}}}""");
        Assert.True(Locations.Available);
        Assert.Equal(new[] { "12_7" }, Locations.Tags);
        var location = Locations.Get("12_7")!.Value;
        Assert.Equal(location, Locations.At(12, 7));
        Assert.Equal(new GmValue[] { "r_global" }, location.Rooms);
        var room = location.Room("r_global")!.Value;
        Assert.Equal(new GmValue[] { "default" }, room.Presets);
        var preset = room.Preset("default")!;
        Assert.Equal(LocationFlags.Mobs | LocationFlags.Doors, preset.Flags);
        Assert.True(preset.HasSaveData);
        using (var entities = new DsHolder(preset.Entities!.Value))
            Assert.NotNull(entities.Map.GetMap("mobsMap"));
        Assert.Null(Locations.Get("0_0"));
        Assert.Null(location.Room("r_dungeon_1"));
        Assert.Null(room.Preset("other"));
    }

    [Fact]
    public void A_preset_never_saved_has_no_entities()
    {
        Game();
        Osbrook("default", LocationFlags.None, null);
        var preset = Locations.Get("12_7")!.Value.Room("r_global")!.Value.Preset("default")!;
        Assert.False(preset.HasSaveData);
        Assert.Null(preset.EntitiesJson);
        Assert.Null(preset.Entities);
    }

    [Fact]
    public void Flags_go_through_the_games_scripts_one_at_a_time()
    {
        Game();
        Osbrook("default", LocationFlags.None, null);
        var preset = Locations.Get("12_7")!.Value.Room("r_global")!.Value.Preset("default")!;
        preset.SetFlags(LocationFlags.Mobs | LocationFlags.Npc);
        preset.UnsetFlags(LocationFlags.Doors);
        Locations.Get("12_7")!.Value.SetFlags(LocationFlags.LootDrop);
        Assert.Equal(new[] { "scr_locationRoomPresetFlagSet", "scr_locationRoomPresetFlagSet", "scr_locationRoomPresetFlagUnset", "scr_locationFlagSet" },
            _calls.Select(c => c.Script));
        Assert.Equal(new GmValue[] { "12_7", "r_global", "default", 1 }, _calls[0].Args);
        Assert.Equal(new GmValue[] { "12_7", "r_global", "default", 2 }, _calls[1].Args);
        Assert.Equal(new GmValue[] { "12_7", 16 }, _calls[3].Args);
    }

    [Fact]
    public void A_state_goes_out_as_JSON_and_back_with_its_tags_types()
    {
        Game();
        // (A preset tag that's a number: the game's key 3, not "3".)
        Osbrook(3, LocationFlags.Corpses, """{"npcMap": {}}""");
        var state = Locations.Get("12_7")!.Value.Room("r_global")!.Value.Preset(3)!.Export();
        var back = LocationState.FromJson(state.ToJson())!;
        Assert.Equal(GmKind.Real, back.Preset.Kind);
        Assert.Equal(state, back);

        Game();
        Assert.True(Locations.Store(back));
        var stored = _all.GetMap("12_7")!.Value.GetMap("r_global")!.Value.GetMap(3)!.Value;
        Assert.Equal((int)LocationFlags.Corpses, stored["flags"].AsInt);
        Assert.Equal("""{"npcMap": {}}""", stored["entitiesDataMapString"].AsString);
        Assert.Null(_all.GetMap("12_7").Value.GetMap("r_global").Value.GetMap("3"));
    }

    [Fact]
    public void Entities_that_arent_JSON_are_not_stored()
    {
        Game();
        Assert.False(Locations.Store(new LocationState("1_1", "r_global", "default", LocationFlags.Mobs, "not json")));
        Assert.Null(Locations.Get("1_1"));
        Assert.Null(LocationState.FromJson("not json"));
        Assert.Null(LocationState.FromJson("""{"room": "r_global"}"""));
    }

    [Fact]
    public void With_no_game_there_are_no_locations()
    {
        Assert.False(Locations.Available);
        Assert.Empty(Locations.Tags);
        Assert.Null(Locations.Get("12_7"));
        Assert.Null(Locations.Here);
        Assert.Throws<InvalidOperationException>(() => Locations.Store(new LocationState("1_1", "r_global", "default", LocationFlags.None, null)));
    }

    // (A copy the caller owns, destroyed after.)
    private sealed record DsHolder(DsMap Map) : IDisposable
    {
        public void Dispose() => Map.Destroy();
    }
}
