using System.Text.Json.Nodes;
using StoneForge;

// Game values as System.Text.Json nodes and back (GmValue.ToJsonNode / FromJsonNode, GmArray / GmStruct.ToJsonNode):
// plain values, arrays and structs at any depth (laid out with FakeGame's model of them), what JSON has no way to
// write, and text that isn't JSON.
public class GmJsonTests : FakeGame
{
    private readonly FakeRefs _refs = new();

    public GmJsonTests() => Refs = _refs;

    private static string Json(string json) => JsonNode.Parse(json)!.ToJsonString();
    private static string? Text(JsonNode? node) => node?.ToJsonString();

    [Fact]
    public void Plain_values_are_JSON_values()
    {
        Assert.Equal("3.5", Text(((GmValue)3.5).ToJsonNode()));
        Assert.Equal("true", Text(((GmValue)true).ToJsonNode()));
        Assert.Equal("\"Verren\"", Text(((GmValue)"Verren").ToJsonNode()));
        Assert.Null(GmValue.Undefined.ToJsonNode());
        // (An instance as its id: what the game's functions take.)
        Assert.Equal("100042", Text(((GmValue)Instance.FromId(100042)).ToJsonNode()));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Numbers_JSON_cant_write_are_null(double number) => Assert.Null(((GmValue)number).ToJsonNode());

    [Fact]
    public void Plain_JSON_values_are_game_values()
    {
        Assert.Equal(3.5, GmValue.FromJsonNode(JsonValue.Create(3.5)).AsReal);
        Assert.Equal(GmKind.Bool, GmValue.FromJsonNode(JsonNode.Parse("true")).Kind);
        Assert.Equal("Verren", GmValue.FromJsonNode(JsonNode.Parse("\"Verren\"")).AsString);
        Assert.True(GmValue.FromJsonNode(null).IsUndefined);
    }

    [Fact]
    public void An_array_is_written_with_everything_in_it()
    {
        int list = _refs.NewArray();
        _refs.Push(list, 3);
        int hero = _refs.NewStruct();
        _refs.SetMember(hero, "name", "Felice");
        _refs.SetRef(hero, "kit", list, isArray: true);
        int array = _refs.NewArray();
        _refs.Push(array, 1);
        _refs.Push(array, "two");
        _refs.Push(array, true);
        _refs.Push(array, GmValue.Undefined);
        _refs.PushRef(array, hero, isArray: false);

        using GmArray game = _refs.Array(array).AsArray!;
        Assert.Equal(Json("""[1, "two", true, null, {"name": "Felice", "kit": [3]}]"""), game.ToJsonNode().ToJsonString());
        Assert.Equal(game.ToJsonNode().ToJsonString(), game.ToJson());
        Assert.Equal(game.ToJsonNode().ToJsonString(), Text(((GmValue)game).ToJsonNode()));
    }

    [Fact]
    public void A_struct_is_written_as_an_object_and_a_method_in_it_as_null()
    {
        int hero = _refs.NewStruct();
        _refs.SetMember(hero, "level", 3);
        int method = _refs.NewStruct();
        _refs.Methods.Add(method);
        _refs.SetRef(hero, "greet", method, isArray: false);

        using GmStruct game = _refs.Struct(hero).AsStruct!;
        Assert.Equal(Json("""{"level": 3, "greet": null}"""), game.ToJsonNode().ToJsonString());
    }

    [Fact]
    public void A_struct_that_contains_itself_throws()
    {
        int hero = _refs.NewStruct();
        _refs.SetRef(hero, "self", hero, isArray: false);
        using GmStruct game = _refs.Struct(hero).AsStruct!;
        Assert.Throws<InvalidOperationException>(() => game.ToJsonNode());
    }

    [Fact]
    public void The_same_struct_twice_is_not_containing_itself()
    {
        // (Reached twice side by side, not from inside itself: written twice.)
        int shared = _refs.NewStruct();
        _refs.SetMember(shared, "hp", 10);
        int pair = _refs.NewArray();
        _refs.PushRef(pair, shared, isArray: false);
        _refs.PushRef(pair, shared, isArray: false);
        using GmArray game = _refs.Array(pair).AsArray!;
        Assert.Equal(Json("""[{"hp": 10}, {"hp": 10}]"""), game.ToJsonNode().ToJsonString());
    }

    [Fact]
    public void JSON_arrays_and_objects_become_new_game_arrays_and_structs()
    {
        var json = JsonNode.Parse("""{"name": "Felice", "kit": [10, 2, {"blade": true}], "level": 3}""")!;
        GmValue made = GmValue.FromJsonNode(json);
        Assert.Equal(GmKind.Struct, made.Kind);
        using GmStruct hero = made.AsStruct!;
        Assert.Equal("Felice", hero["name"].AsString);
        using GmArray kit = hero["kit"].AsArray!;
        Assert.Equal(3, kit.Length);
        // (And back: the same JSON.)
        Assert.Equal(json.ToJsonString(), hero.ToJsonNode().ToJsonString());
        using GmArray list = GmArray.FromJsonNode(JsonNode.Parse("[1, [2, 3]]")!.AsArray());
        Assert.Equal(Json("[1, [2, 3]]"), list.ToJson());
    }

    [Fact]
    public void Text_that_isnt_the_right_JSON_gives_null()
    {
        Assert.Null(GmStruct.FromJson("not json"));
        Assert.Null(GmStruct.FromJson("[1, 2]"));
        Assert.Null(GmArray.FromJson("{\"a\": 1}"));
        Assert.Null(GmArray.FromJson("{"));
        Assert.NotNull(GmStruct.FromJson("""{"a": 1}"""));
        Assert.NotNull(GmArray.FromJson("[1]"));
    }

    [Fact]
    public void A_ds_list_writes_NaN_as_null()
    {
        Ds = new FakeDs();
        var list = DsList.Create();
        list.Add(double.NaN);
        list.Add(1);
        Assert.Equal("[null,1]", list.ToJson());
    }
}
