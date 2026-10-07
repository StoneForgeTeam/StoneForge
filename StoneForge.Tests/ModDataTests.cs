using StoneForge;

// A mod's own values (ModData): in an item's data, the save data and an instance's variables, under keys only that mod
// uses - two mods' "kills" two values, neither the game's (laid out with FakeGame's lists, globals and room).
public class ModDataTests : FakeGame
{
    private const int Slot = 100_001, Unit = 100_002;
    private readonly FakeDs _ds = new();
    private readonly FakeWorld _world = new();
    private readonly ModContext _first = new("moddata_a");
    private readonly ModContext _second = new("moddata_b");
    private readonly DsMap _data;

    public ModDataTests()
    {
        Ds = _ds;
        World = _world;
        _data = DsMap.Create();
        _data["Duration"] = 80;
        _world.Add(Slot, 300);
        _world.Vars[Slot] = new() { ["data"] = _data.Id };
        _world.Add(Unit, 301);
        _world.Vars[Unit] = new();
    }

    [Fact]
    public void Two_mods_keep_their_own_values_on_one_item()
    {
        var item = new InventoryItem(Instance.FromId(Slot));
        var mine = item.ModData(_first);
        var theirs = item.ModData(_second);

        mine["kills"] = 3;
        theirs["kills"] = 7;
        theirs["owner"] = "Ada";

        Assert.Equal((3.0, 7.0), (mine["kills"].AsReal, theirs["kills"].AsReal));
        Assert.Equal(new[] { "kills" }, mine.Keys);
        Assert.Equal(new[] { "kills", "owner" }, theirs.Keys.OrderBy(k => k));
        // (In the item's own data, by the mod's id - beside the game's, which neither touches.)
        Assert.Equal((3.0, 7.0, 80.0), (_data["moddata_a:kills"].AsReal, _data["moddata_b:kills"].AsReal, _data["Duration"].AsReal));
        Assert.False(mine.Has("owner"));

        theirs.Remove("kills");
        Assert.False(theirs.Has("kills"));
        Assert.True(mine.Has("kills"));
        Assert.Throws<ArgumentException>(() => mine[""]);
    }

    [Fact]
    public void A_mods_values_in_the_save_data_are_its_own()
    {
        var save = DsMap.Create();
        Globals["saveDataMap"] = save.Id;

        SaveData.ModData(_first)["stash"] = "a";
        SaveData.ModData(_second)["stash"] = "b";

        Assert.Equal(("a", "b"), (SaveData.ModData(_first)["stash"].AsString, SaveData.ModData(_second)["stash"].AsString));
        var mods = save.GetMap("stoneforge_mods")!.Value;
        Assert.Equal("a", mods["moddata_a:stash"].AsString);

        // (No game: nothing kept, nothing read.)
        Globals.Remove("saveDataMap");
        SaveData.ModData(_first)["stash"] = "c";
        Assert.True(SaveData.ModData(_first)["stash"].IsUndefined);
    }

    [Fact]
    public void A_mods_variables_on_an_instance_are_named_for_it()
    {
        var unit = Instance.FromId(Unit);
        unit.ModData(_first)["slot"] = 2;

        Assert.Equal(2, unit.ModData(_first)["slot"].AsReal);
        Assert.True(unit.ModData(_second)["slot"].IsUndefined);
        Assert.Equal(2, _world.Vars[Unit]["moddata_a__slot"].AsReal);
        // (A variable's name: letters, digits and _.)
        Assert.Throws<ArgumentException>(() => unit.ModData(_first)["my slot"] = 1);
    }
}
