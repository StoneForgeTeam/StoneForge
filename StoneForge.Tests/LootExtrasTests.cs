using StoneForge;

// A loot table's slots beyond the game's nine (LootTable.Add with the nine taken): kept in its row as slot10 on, which the
// game's roll doesn't read, and rolled just after it by the game's own script - from a copy of the table with them in its
// nine slots and no equipment - as the same container (laid out with FakeGame's lists, globals and scripts).
public class LootExtrasTests : FakeGame
{
    private const string LootScript = "scr_loot_from_tables";
    private const int Chest = 100_002;
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    private readonly FakeWorld _world = new();
    private readonly ModContext _loader = new("loot_extras_test");
    // Each roll the game's script made: the table it read (by its key) - its nine slots and its first equipment chance.
    private readonly List<(string Key, string[] Slots, string Eq1Chance)> _rolls = new();

    public LootExtrasTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        World = _world;
        _world.LendsIds = true;
        _world.Add(Chest, 301);
        _world.Vars[Chest] = new();
        _world.Assets["o_inv_wine"] = 400;
        Hooks.Hookable = null;
        _scripts.Add(LootScript, args =>
        {
            var tables = Globals["drop_table"].AsDsMap!.Value;
            string key = args[0].AsString;
            var row = tables.GetMap(key + args[1].AsInt) ?? tables[key].AsDsMap!.Value;
            _rolls.Add((key, Enumerable.Range(1, 9).Select(n => row[$"slot{n}"].AsString).ToArray(), row["eq1_chance"].AsString));
            return 0;
        });
        var tables = DsMap.Create();
        var tomb = DsMap.Create();
        tomb["tierMod"] = "";
        for (int n = 1; n <= 9; n++)
            foreach (string suffix in new[] { "", "_chance", "_count", "_tags" })
                tomb[$"slot{n}{suffix}"] = "";
        tomb["eq1"] = "weapon";
        tomb["eq1_chance"] = "5";
        tables.AddMap("cryptTomb3", tomb);
        Globals["drop_table"] = tables.Id;
        LootTables.Install(_loader);
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_loader.Id);
        base.Dispose();
    }

    private void Roll() => Game.CallScript(LootScript, Instance.FromId(Chest), "cryptTomb", 3, false);

    [Fact]
    public void Past_the_nine_slots_an_item_goes_in_one_of_StoneForges()
    {
        var table = LootTables.Get("cryptTomb3")!;
        for (int i = 1; i <= 9; i++)
            Assert.False(table.Add($"gem{i}", 10).IsExtra);
        var tenth = table.Add("wine", 33, 1, 2);
        Assert.Equal((10, true), (tenth.Number, tenth.IsExtra));
        Assert.Equal(11, table.Add("gem", 5, tags: "rare").Number);
        Assert.Equal(11, LootTables.Get("cryptTomb3")!.Slots.Count);
        var row = Globals["drop_table"].AsDsMap!.Value.GetMap("cryptTomb3")!.Value;
        Assert.Equal(("o_inv_wine", "33", "1,2"), (row["slot10"].AsString, row["slot10_chance"].AsString, row["slot10_count"].AsString));
        // (An extra slot cleared is the first to fill again.)
        tenth.Clear();
        Assert.Equal(10, table.Add("bread", 50).Number);
    }

    [Fact]
    public void Slots_past_the_nine_are_rolled_after_the_games_roll_nine_at_a_time()
    {
        var table = LootTables.Get("cryptTomb3")!;
        for (int i = 1; i <= 20; i++)
            table.Add($"item{i}", 10);
        Roll();
        // (The game's own roll, then 11 extras: 9, then 2 - each from a copy with no equipment.)
        Assert.Equal(3, _rolls.Count);
        Assert.Equal(("cryptTomb", "item1", "5"), (_rolls[0].Key, _rolls[0].Slots[0], _rolls[0].Eq1Chance));
        Assert.Equal(Enumerable.Range(10, 9).Select(n => $"item{n}"), _rolls[1].Slots);
        Assert.Equal("0", _rolls[1].Eq1Chance);
        Assert.Equal(new[] { "item19", "item20", "", "", "", "", "", "", "" }, _rolls[2].Slots);
        // (The copy's gone after.)
        Assert.False(Globals["drop_table"].AsDsMap!.Value.Has("__stoneforge_extra_slots"));
    }

    [Fact]
    public void A_table_with_no_extras_is_rolled_once()
    {
        LootTables.Get("cryptTomb3")!.Add("wine", 33);
        Roll();
        Assert.Single(_rolls);
    }
}
