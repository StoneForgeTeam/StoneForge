using StoneForge;

// The game's loot tables (LootTables): a row read and changed as the game keeps it - every value text, "5,15" ranges,
// ", "-separated choices - edits waiting for the game to load its tables, and a container's own table chosen (laid out
// with FakeGame's lists, globals and room).
public class LootTablesTests : FakeGame
{
    private const int TextLoader = 100_001, Chest = 100_002, LootScript = 7_000;
    private readonly FakeDs _ds = new();
    private readonly FakeWorld _world = new();
    private readonly ModContext _context = new("loot_tables_test");

    public LootTablesTests()
    {
        Ds = _ds;
        World = _world;
        _world.LendsIds = true;
        _world.Assets["o_inv_wine"] = 400;
        _world.Assets["scr_loot_from_tables"] = LootScript;
        _world.Add(TextLoader, 300);
        _world.Vars[TextLoader] = new() { ["number"] = 38 };
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        LootTables.RemoveMod(_context.Id);
        base.Dispose();
    }

    // The game's tables, as it loads them: a row a table, every value text.
    private void LoadTables()
    {
        var tables = DsMap.Create();
        var tomb = DsMap.Create();
        foreach (var (key, value) in new[] { ("tierMod", ""), ("slot1", "o_inv_old_coin"), ("slot1_count", "5,15"), ("slot1_chance", "16"),
            ("slot2", "valuable"), ("slot2_tags", "crypt"), ("slot2_chance", "5"), ("slot9", "o_inv_bone_foot, o_inv_bone"),
            ("slot9_count", "1,2"), ("slot9_chance", "50"), ("eq1", "weapon, armor"), ("eq1_rarity", "common, uncommon, rare"),
            ("eq1_dur", "5,60"), ("eq1_chance", "5") })
            tomb[key] = value;
        for (int n = 3; n <= 8; n++)
            tomb[$"slot{n}"] = "";
        tables.AddMap("cryptTomb3", tomb);
        Globals["drop_table"] = tables.Id;
    }

    [Fact]
    public void A_table_is_read_as_the_game_keeps_it()
    {
        LoadTables();
        var table = LootTables.Get("cryptTomb3")!;
        Assert.Equal(new[] { "cryptTomb3" }, LootTables.Names);
        Assert.Equal(new[] { "o_inv_old_coin" }, table.Slots[0].Items);
        Assert.Equal((5, 15), table.Slots[0].Count);
        Assert.Equal(16, table.Slots[0].Chance);
        Assert.Equal("crypt", table.Slots[1].Tags);
        Assert.Equal(new[] { "valuable" }, table.Slots[1].Items);
        Assert.Equal(new[] { "o_inv_bone_foot", "o_inv_bone" }, table.Slots[8].Items);
        Assert.True(table.Slots[2].IsEmpty);
        var eq = table.EquipmentSlots[0];
        Assert.Equal(new[] { "weapon", "armor" }, eq.Kinds);
        Assert.Equal(((5, 60), 5.0), (eq.Durability, eq.Chance));
        Assert.Equal(3, eq.Rarities.Count);
    }

    [Fact]
    public void An_item_goes_in_the_first_empty_slot_as_the_game_names_it()
    {
        LoadTables();
        var table = LootTables.Get("cryptTomb3")!;

        var wine = table.Add("wine", 33, 1, 2)!;
        Assert.Equal(3, wine.Number);
        var row = Globals["drop_table"].AsDsMap!.Value.GetMap("cryptTomb3")!.Value;
        Assert.Equal(("o_inv_wine", "33", "1,2"), (row["slot3"].AsString, row["slot3_chance"].AsString, row["slot3_count"].AsString));
        // (A kind, not an item: as it is.)
        Assert.Equal("gem", table.Add("gem", 10, tags: "rare")!.Items[0]);

        table.Slots[8].Clear();
        Assert.True(table.Slots[8].IsEmpty);
        Assert.Equal("", row["slot9_chance"].AsString);
    }

    [Fact]
    public void An_edit_made_before_the_tables_load_waits_for_them()
    {
        LootTables.Install(_context);
        LootTables.Edit(_context, "cryptTomb3", table => table.Slots[0].Chance = 100);
        Assert.False(LootTables.Loaded);

        LoadTables();
        RunCode("gml_Object_o_textLoader_Other_25", TextLoader);
        Assert.Equal(100, LootTables.Get("cryptTomb3")!.Slots[0].Chance);

        // (Loaded: an edit's made at once.)
        LootTables.EditAll(_context, name => name.StartsWith("cryptTomb"), table => table.TierMod = "4,5");
        Assert.Equal("4,5", LootTables.Get("cryptTomb3")!.TierMod);
    }

    [Fact]
    public void A_container_rolls_from_the_table_chosen_for_it_until_its_opened()
    {
        var loot = DsList.Create();
        _world.Add(Chest, 301);
        _world.Vars[Chest] = new() { ["loot_list"] = loot.Id, ["is_execute"] = false, ["loot_script"] = LootScript, ["loot_script_key"] = "graveSurface", ["loot_script_tier"] = 0 };
        var chest = Instance.FromId(Chest);

        Assert.Equal(("graveSurface", 0), Containers.LootTableOf(chest));
        Assert.True(Containers.SetLootTable(chest, "cryptBossChest", 5));
        Assert.Equal(("cryptBossChest", 5), Containers.LootTableOf(chest));

        _world.Vars[Chest]["is_execute"] = true;
        Assert.False(Containers.SetLootTable(chest, "cryptTomb", 3));
    }
}
