using System.Text.Json.Nodes;
using StoneForge;

// The game's ds_maps and ds_lists (laid out with FakeGame's ds model, whose marks behave as the worst the game could
// do): reading and changing them, their nested maps and lists, JSON, and AssignFrom - which copies into the maps and
// lists the game holds on to, rather than making new ones.
public class DsTests : FakeGame
{
    private readonly FakeDs _ds = new();

    public DsTests() => Ds = _ds;

    private static string Json(string json) => JsonNode.Parse(json)!.ToJsonString();

    [Fact]
    public void A_map_reads_and_changes_its_keys()
    {
        var map = DsMap.Create();
        map["hp"] = 10;
        map["name"] = "Verren";
        map[3] = true;
        Assert.Equal(3, map.Count);
        Assert.Equal(10, map["hp"].AsInt);
        Assert.Equal("Verren", map["name"].AsString);
        Assert.True(map.Has(3));
        Assert.False(map.Has("3"));
        Assert.True(map["missing"].IsUndefined);
        Assert.Equal(7, map.Get("missing", 7).AsInt);
        Assert.Equal(new GmValue[] { "hp", "name", 3 }, map.Keys);
        map.Remove("hp");
        Assert.False(map.Has("hp"));
        Assert.Equal(2, map.Count);
    }

    [Fact]
    public void A_number_is_a_map_or_list_only_if_one_has_it()
    {
        var map = DsMap.Create();
        var list = DsList.Create();
        GmValue mapId = map, listId = list;
        Assert.Equal(map, mapId.AsDsMap);
        Assert.Equal(list, listId.AsDsList);
        Assert.Null(((GmValue)99).AsDsMap);
        Assert.Null(((GmValue)"0").AsDsMap);
        Assert.Null(((GmValue)(-1)).AsDsList);
        map.Destroy();
        Assert.False(map.Exists);
        Assert.Null(mapId.AsDsMap);
    }

    [Fact]
    public void Nested_maps_and_lists_are_owned_and_go_with_their_key()
    {
        var map = DsMap.Create();
        var inner = DsMap.Create();
        var list = DsList.Create();
        map.AddMap("inner", inner);
        map.AddList("list", list);
        map["plain"] = inner.Id;
        Assert.True(map.IsMap("inner"));
        Assert.True(map.IsList("list"));
        Assert.False(map.IsMap("plain"));
        Assert.False(map.IsMap("missing"));
        Assert.Equal(inner, map.GetMap("inner"));
        Assert.Equal(list, map.GetList("list"));
        Assert.Null(map.GetMap("plain"));
        // (Set over, the nested one is destroyed - and the key's a plain value, not marked.)
        map["list"] = 5;
        Assert.False(list.Exists);
        Assert.False(map.IsList("list"));
        map.Destroy();
        Assert.False(inner.Exists);
    }

    [Fact]
    public void A_list_reads_and_changes_its_elements()
    {
        var list = DsList.Create();
        list.Add(1);
        list.Add("two");
        list.Add(3);
        Assert.Equal(3, list.Count);
        Assert.Equal("two", list[1].AsString);
        list[1] = 2;
        Assert.Equal(2, list[1].AsInt);
        list.RemoveAt(0);
        Assert.Equal(2, list.Count);
        Assert.Equal(3, list[1].AsInt);
        list.RemoveAt(9);
        Assert.Equal(2, list.Count);
        list.Clear();
        Assert.Equal(0, list.Count);
    }

    [Fact]
    public void A_list_owns_its_nested_ones_and_a_slot_set_over_loses_its_mark()
    {
        var list = DsList.Create();
        var inner = DsMap.Create();
        var nested = DsList.Create();
        list.Add(0);
        list.AddMap(inner);
        list.AddList(nested);
        Assert.True(list.IsMap(1));
        Assert.True(list.IsList(2));
        Assert.False(list.IsMap(0));
        Assert.False(list.IsMap(7));
        Assert.Equal(inner, list.GetMap(1));
        list[1] = "plain";
        Assert.False(inner.Exists);
        Assert.False(list.IsMap(1));
        Assert.Equal("plain", list[1].AsString);
        // (And the rest stay as they were.)
        Assert.Equal(3, list.Count);
        Assert.Equal(nested, list.GetList(2));
        list.RemoveAt(2);
        Assert.False(nested.Exists);
        list.AddMap(DsMap.Create());
        var last = list.GetMap(2)!.Value;
        list.Clear();
        Assert.False(last.Exists);
    }

    [Fact]
    public void Json_from_dotnet_reads_with_its_escapes()
    {
        // (As .NET wrote a save the game gave up on: ' and < escaped - the game's decoder can't read those.)
        const string json = """{"dungeon":"Bernarhof\u0027s Cenotaph","note":"\u003C1>","list":[1.5,"it\u0027s"]}""";
        var map = DsMap.FromJson(json)!.Value;
        Assert.Equal("Bernarhof's Cenotaph", map["dungeon"].AsString);
        Assert.Equal("<1>", map["note"].AsString);
        Assert.Equal("it's", map.GetList("list")!.Value[1].AsString);
        var list = DsList.FromJson("""["it\u0027s","\u003Cb>"]""")!.Value;
        Assert.Equal("it's", list[0].AsString);
        Assert.Equal("<b>", list[1].AsString);
    }

    [Fact]
    public void Json_goes_both_ways()
    {
        const string json = """{"name":"Verren","hp":10,"alive":true,"stats":{"str":12},"items":[1,"two",{"id":3},[4]]}""";
        var map = DsMap.FromJson(json)!.Value;
        Assert.True(map.IsMap("stats"));
        Assert.True(map.IsList("items"));
        Assert.Equal(12, map.GetMap("stats")!.Value["str"].AsInt);
        Assert.Equal(Json(json), Json(map.ToJson()));
        Assert.Null(DsMap.FromJson("not json"));

        var list = DsList.FromJson("""[1,"two",{"id":3},[4,[5]]]""")!.Value;
        Assert.Equal(4, list.Count);
        Assert.True(list.IsMap(2));
        Assert.True(list.GetList(3)!.Value.IsList(1));
        Assert.Equal(Json("""[1,"two",{"id":3},[4,[5]]]"""), Json(list.ToJson()));
        Assert.Null(DsList.FromJson("""{"a":1}"""));
        // (The map it was read into is gone, its nested ones with it; the list is a copy of its own: the map read
        // first, its "stats" and items' map, and the list's map - and the lists nested in each.)
        Assert.Equal(4, _ds.Maps.Count);
        Assert.Equal(5, _ds.Lists.Count);
    }

    [Fact]
    public void AssignFrom_copies_into_the_maps_and_lists_already_held()
    {
        var target = DsMap.FromJson("""{"gone":1,"hp":5,"stats":{"str":1,"old":2},"items":[{"id":1},{"id":2},9],"deep":{"a":{"b":1}}}""")!.Value;
        // (What the game holds on to: the map, its nested map and list, a map nested two deep, a map in the list.)
        var stats = target.GetMap("stats")!.Value;
        var items = target.GetList("items")!.Value;
        var deepest = target.GetMap("deep")!.Value.GetMap("a")!.Value;
        var firstItem = items.GetMap(0)!.Value;
        var source = DsMap.FromJson("""{"hp":7,"stats":{"str":3},"items":[{"id":4}],"deep":{"a":{"b":2,"c":[1]}},"new":{"x":1}}""")!.Value;

        target.AssignFrom(source);

        Assert.Equal(Json(source.ToJson()), Json(target.ToJson()));
        Assert.Equal(3, stats["str"].AsInt);
        Assert.False(stats.Has("old"));
        Assert.Equal(1, items.Count);
        Assert.Equal(4, firstItem["id"].AsInt);
        Assert.Equal(2, deepest["b"].AsInt);
        Assert.True(deepest.IsList("c"));
        // (The source keeps its own: nothing nested in one is in the other.)
        Assert.NotEqual(source.GetMap("new")!.Value, target.GetMap("new")!.Value);
        Assert.NotEqual(source.GetMap("deep")!.Value.GetMap("a")!.Value.GetList("c"), deepest.GetList("c"));
        source.Destroy();
        Assert.True(target.GetMap("new")!.Value.Exists);
        Assert.Equal(1, target.GetMap("new")!.Value["x"].AsInt);
    }

    [Fact]
    public void AssignFrom_remakes_a_slot_whose_kind_changes()
    {
        var target = DsMap.FromJson("""{"a":{"x":1},"b":[1],"c":5,"d":{"y":1}}""")!.Value;
        var wasMap = target.GetMap("a")!.Value;
        var wasList = target.GetList("b")!.Value;
        var wasMapToo = target.GetMap("d")!.Value;
        var source = DsMap.FromJson("""{"a":7,"b":{"z":1},"c":[2],"d":[3]}""")!.Value;

        target.AssignFrom(source);

        Assert.Equal(Json(source.ToJson()), Json(target.ToJson()));
        Assert.False(target.IsMap("a"));
        Assert.True(target.IsMap("b"));
        Assert.True(target.IsList("c"));
        Assert.True(target.IsList("d"));
        // (The ones replaced are destroyed, not left behind.)
        Assert.False(wasMap.Exists);
        Assert.False(wasList.Exists);
        Assert.False(wasMapToo.Exists);
    }

    [Fact]
    public void A_list_assigns_in_place_by_index()
    {
        var target = DsList.FromJson("""[{"id":1},[1,2],3,{"id":4},5]""")!.Value;
        var kept = target.GetMap(0)!.Value;
        var keptList = target.GetList(1)!.Value;
        var remade = target.GetMap(3)!.Value;
        var source = DsList.FromJson("""[{"id":9},[7],{"z":1},"four",[6],7]""")!.Value;

        target.AssignFrom(source);

        Assert.Equal(Json(source.ToJson()), Json(target.ToJson()));
        Assert.Equal(kept, target.GetMap(0));
        Assert.Equal(9, kept["id"].AsInt);
        Assert.Equal(keptList, target.GetList(1));
        Assert.Equal(1, keptList.Count);
        Assert.True(target.IsMap(2));
        Assert.False(target.IsMap(3));
        Assert.False(remade.Exists);
        Assert.True(target.IsList(4));

        // (Cut to a shorter one: the nested ones past its end destroyed.)
        target.AssignFrom(DsList.FromJson("[1]")!.Value);
        Assert.Equal(1, target.Count);
        Assert.False(target.IsMap(0));
        Assert.False(kept.Exists);
        Assert.False(keptList.Exists);
    }

    [Fact]
    public void Assigning_from_itself_changes_nothing()
    {
        var map = DsMap.FromJson("""{"a":{"b":1}}""")!.Value;
        var inner = map.GetMap("a")!.Value;
        map.AssignFrom(map);
        Assert.Equal(inner, map.GetMap("a"));
        Assert.Equal(1, inner["b"].AsInt);
    }
}
