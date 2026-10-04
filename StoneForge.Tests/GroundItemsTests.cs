using StoneForge;

// Items on the ground (GroundItems, GroundItem, ItemFlight): an item's saved state through the game's own save scripts
// and an item made back from it, landed where it lay; a culled item woken for the moment and put back; a hop read,
// written as JSON and replayed; new items put down with or without the hop (laid out with FakeGame's room, ds maps and
// scripts).
public class GroundItemsTests : FakeGame
{
    private const int Wine = 100, Ring = 101;
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    private readonly FakeWorld _world = new();
    // What the game's scripts were given, and whether the item was awake for each.
    private readonly List<(string Script, GmValue[] Args)> _calls = new();
    private readonly List<bool> _awake = new();
    private int _next = 50;

    public GroundItemsTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        World = _world;
        _world.Parents[Wine] = (int)GameObjectId.o_loot;
        _world.Parents[(int)GameObjectId.o_weapon_loot] = (int)GameObjectId.o_loot;
        _world.Parents[Ring] = (int)GameObjectId.o_player;
        _world.Assets["o_loot_wine"] = Wine;
        _world.Assets["o_weapon_loot"] = (int)GameObjectId.o_weapon_loot;
        // As the game's: an item's save - its object, where it lies, its stack and data.
        _scripts.Add("scr_locationRoomEntityLootSaveDataGet", a =>
        {
            int id = IdOf(a[0]);
            _calls.Add(("get", a));
            _awake.Add(_world.Active.Contains(id));
            var map = DsMap.Create();
            map["object_name"] = "o_loot_wine";
            map["x"] = _world.Vars[id]["x"];
            map["y"] = _world.Vars[id]["y"];
            map["stack"] = 3;
            return map.Id;
        });
        // (Made where the save says, as scr_loot makes it: in the air, hopping.)
        _scripts.Add("scr_locationRoomEntityLootInstanceCreate", a =>
        {
            var saved = a[0].AsDsMap!.Value;
            return saved["object_name"].AsString == "o_loot_wine" ? Item(Wine, saved["x"].AsReal, saved["y"].AsReal, speed: 8) : -4;
        });
        _scripts.Add("scr_locationRoomEntityLootSaveDataSet", a => { _calls.Add(("set", a)); return GmValue.Undefined; });
        _scripts.Add("scr_loot", a => { _calls.Add(("scr_loot", a)); return Item(a[0].AsInt, a[1].AsReal, a[2].AsReal, speed: 8); });
        _scripts.Add("scr_weapon_loot", a =>
        {
            _calls.Add(("scr_weapon_loot", a));
            return a[0].AsString == "Shiv" ? Item((int)GameObjectId.o_weapon_loot, a[1].AsReal, a[2].AsReal, speed: 8) : -4;
        });
    }

    // An instance argument as the game's scripts get it: a reference or a number.
    private static int IdOf(GmValue value) => value.Kind == GmKind.Instance ? value.AsInstance.Id : value.AsInt;

    // An item on the ground at (x, y): dynamic unless said, in the air if it has speed.
    private int Item(int obj, double x, double y, bool culled = false, string type = "dynamic", double speed = 0)
    {
        int id = _next++;
        _world.Add(id, obj, culled);
        _world.Vars[id] = new()
        {
            ["object_index"] = obj, ["x"] = x, ["y"] = y, ["roomEntityType"] = type,
            ["speed"] = speed, ["gravity"] = speed > 0 ? 0.6 : 0,
        };
        return id;
    }

    [Fact]
    public void Every_item_on_the_ground_is_found_off_screen_too()
    {
        int near = Item(Wine, 10, 10), far = Item((int)GameObjectId.o_weapon_loot, 500, 500, culled: true);
        Item(Ring, 0, 0);
        Assert.Equal(new[] { near, far }, GroundItems.All().Select(i => i.Instance.Id).Order());
        Assert.Equal(new[] { near }, GroundItems.All(includeCulled: false).Select(i => i.Instance.Id));
        var item = new GroundItem(Instance.FromId(near));
        Assert.Equal("o_loot_wine", item.ObjectName);
        Assert.Equal((10.0, 10.0), (item.X, item.Y));
        Assert.True(item.IsOnGround);
        Instance.FromId(near).Destroy();
        Assert.False(item.IsOnGround);
    }

    [Fact]
    public void An_item_goes_out_in_the_games_save_format_and_comes_back_where_it_lay()
    {
        var item = new GroundItem(Instance.FromId(Item(Wine, 130, 78)));
        string json = item.ToJson()!;
        Assert.Contains("\"stack\"", json);

        var made = GroundItems.Create(json)!.Value;
        Assert.NotEqual(item, made);
        // (Given its saved state - which lands it - and nothing else of ours.)
        var (script, args) = _calls.Last();
        Assert.Equal("set", script);
        Assert.Equal(made.Instance.Id, IdOf(args[0]));
        Assert.Equal((130.0, 78.0), (made.X, made.Y));
        Assert.Empty(_world.UserEvents);
    }

    [Fact]
    public void A_static_item_has_no_saved_state()
    {
        var item = new GroundItem(Instance.FromId(Item(Wine, 0, 0, type: "static")));
        Assert.True(item.IsStatic);
        Assert.Null(item.ToJson());
        Assert.Null(item.Save());
        Assert.Empty(_calls);
    }

    [Fact]
    public void A_culled_item_is_woken_for_its_save_and_put_back()
    {
        int id = Item(Wine, 40, 40, culled: true);
        var item = new GroundItem(Instance.FromId(id));
        Assert.True(item.Instance.IsCulled);
        Assert.False(item.IsStatic);
        Assert.NotNull(item.ToJson());
        Assert.Equal(new[] { true }, _awake);
        Assert.Contains(id, _world.Culled);
        Assert.DoesNotContain(id, _world.Active);
    }

    [Fact]
    public void What_isnt_an_items_save_makes_nothing()
    {
        Assert.Null(GroundItems.Create("not json"));
        Assert.Null(GroundItems.Create("""{"x": 1}"""));
        Assert.Null(GroundItems.Create("""{"object_name": "o_loot_nothing", "x": 1, "y": 1}"""));
        Assert.DoesNotContain(_calls, c => c.Script == "set");
    }

    [Fact]
    public void A_hop_is_read_and_replayed_on_another_item()
    {
        int id = Item((int)GameObjectId.o_weapon_loot, 100, 90, speed: 8);
        _world.Vars[id]["targ_x"] = 126;
        _world.Vars[id]["targ_y"] = 116;
        _world.Vars[id]["hspeed"] = 0;
        _world.Vars[id]["vspeed"] = -8;
        _world.Vars[id]["yy"] = 95;
        _world.Vars[id]["image_angle"] = 30;
        var flight = new GroundItem(Instance.FromId(id)).Flight!.Value;
        Assert.Equal(new ItemFlight(100, 90, 126, 116, 0, -8, 0.6, 95, 30), flight);
        Assert.Equal(flight, ItemFlight.FromJson(flight.ToJson()));
        Assert.Null(ItemFlight.FromJson("""{"x": 1}"""));

        int copy = Item(Wine, 126, 116);
        var landed = new GroundItem(Instance.FromId(copy));
        Assert.False(landed.InFlight);
        Assert.Null(landed.Flight);
        landed.Fly(flight);
        var vars = _world.Vars[copy];
        Assert.Equal((100.0, 90.0, 126.0, 116.0, -8.0, 0.6), (vars["x"].AsReal, vars["y"].AsReal, vars["targ_x"].AsReal,
            vars["targ_y"].AsReal, vars["vspeed"].AsReal, vars["gravity"].AsReal));
        // (Not a weapon: no height to draw at.)
        Assert.False(vars.ContainsKey("yy"));
    }

    [Fact]
    public void A_new_item_lies_where_its_put_unless_it_hops()
    {
        var wine = GroundItems.Spawn("wine", 52, 52)!.Value;
        Assert.Equal(("scr_loot", Wine), (_calls[0].Script, _calls[0].Args[0].AsInt));
        // (Landed: its user event 2.)
        Assert.Equal((wine.Instance.Id, 2), Assert.Single(_world.UserEvents));

        var shiv = GroundItems.Spawn("Shiv", 52, 52, hop: true, quality: ItemQuality.Magical)!.Value;
        Assert.Equal("scr_weapon_loot", _calls[1].Script);
        Assert.Equal(new GmValue[] { "Shiv", 52, 52, 100, 3 }, _calls[1].Args);
        Assert.True(shiv.InFlight);
        Assert.Single(_world.UserEvents);

        Assert.Null(GroundItems.Spawn("nothing", 0, 0));
    }
}
