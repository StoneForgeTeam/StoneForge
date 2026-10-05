using System.Text.Json.Nodes;
using StoneForge;

// Containers (Containers): a closed one's items read and set in the game's own save format - each item a list of its own,
// held in the container's by id, its data a map in it - refused while it's open; and their opening, closing and items
// coming and going as events (laid out with FakeGame's room and lists).
public class ContainersTests : FakeGame
{
    private const int Chest = 100_001, Window = 100_002, Wine = 100_010, Ring = 100_011;
    private readonly FakeWorld _world = new();
    private readonly FakeDs _ds = new();
    private readonly ModContext _context = new("containers_test");
    private readonly DsList _loot;

    public ContainersTests()
    {
        World = _world;
        Ds = _ds;
        _world.LendsIds = true;
        _world.ExistsByObject = true;
        _world.Add(Chest, 300);
        // (A chest opened before, closed: one wine in it, as the game's save of it.)
        _loot = DsList.Create();
        _loot.Add(SavedItem("Wine", 2).Id);
        _world.Vars[Chest] = new() { ["loot_list"] = _loot.Id, ["is_execute"] = true };
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    // An item as the game saves it (scr_save_item_single): its name, its data (a map, marked), where it lay, its look,
    // charge, stack, whether it was on, deactivated, its equipment slot.
    private static DsList SavedItem(string name, int stack)
    {
        var item = DsList.Create();
        item.Add(name);
        var data = DsMap.Create();
        data["idName"] = name;
        item.AddMap(data);
        foreach (GmValue value in new GmValue[] { 0, 3, 0, 1, stack, false, false, "N/A" })
            item.Add(value);
        return item;
    }

    private void OpenWindow()
    {
        _world.Add(Window, (int)GameObjectId.o_container_parent);
        _world.Vars[Window] = new() { ["parent"] = Chest };
    }

    [Fact]
    public void A_closed_containers_items_are_read_and_set_in_the_games_format()
    {
        var contents = JsonNode.Parse(Containers.ContentsJson(Instance.FromId(Chest))!)!.AsArray();
        Assert.Single(contents);
        Assert.Equal("Wine", contents[0]![0]!.GetValue<string>());
        Assert.Equal("Wine", contents[0]![1]!["idName"]!.GetValue<string>());
        Assert.Equal(2, contents[0]![6]!.GetValue<double>());

        // (Another game's: the ring, and the wine taken.)
        _world.Vars[Chest]["is_execute"] = false;
        string theirs = new JsonArray(JsonNode.Parse(DsListJson(SavedItem("Ring", 1)))).ToJsonString();
        Assert.True(Containers.SetContents(Instance.FromId(Chest), theirs));

        Assert.Equal(1, _loot.Count);
        var ring = DsList.From(_loot[0])!.Value;
        Assert.Equal("Ring", ring[0].AsString);
        Assert.True(ring.IsMap(1));
        Assert.Equal("Ring", ring.GetMap(1)!.Value["idName"].AsString);
        // (Never opened: counts as opened now - its loot is this, not rolled.)
        Assert.True(Containers.HasBeenOpened(Instance.FromId(Chest)));
        Assert.False(Containers.SetContents(Instance.FromId(Chest), "{}"));
    }

    private static string DsListJson(DsList list) => list.ToJsonNode().ToJsonString();

    [Fact]
    public void An_open_containers_saved_items_are_left_alone()
    {
        OpenWindow();
        Assert.True(Containers.IsOpen(Instance.FromId(Chest)));
        Assert.Null(Containers.ContentsJson(Instance.FromId(Chest)));
        Assert.False(Containers.SetContents(Instance.FromId(Chest), "[]"));
    }

    [Fact]
    public void Opening_closing_and_items_coming_and_going_are_told()
    {
        var opened = new List<Instance>();
        var closed = new List<Instance>();
        var added = new List<Instance>();
        var removed = new List<Instance>();
        Containers.OnOpened(_context, open => opened.Add(open.Container));
        Containers.OnClosed(_context, closed.Add);
        Containers.OnItemAdded(_context, (_, item) => added.Add(item.Slot));
        Containers.OnItemRemoved(_context, (_, item) => removed.Add(item.Slot));

        // Opened, with the wine in it (its window made, then filled): not news that it has the wine.
        OpenWindow();
        RunCode("gml_Object_o_container_parent_Create_0", Window);
        _world.Add(Wine, (int)GameObjectId.o_inv_slot);
        _world.Vars[Wine] = new() { ["owner"] = Window };
        RunFrame();
        Assert.Equal(new[] { Instance.FromId(Chest) }, opened);
        Assert.Empty(added);

        // The ring put in, the wine taken out.
        _world.Add(Ring, (int)GameObjectId.o_inv_slot);
        _world.Vars[Ring] = new() { ["owner"] = Window };
        _world.Vars[Wine]["owner"] = 100_099;
        RunFrame();
        Assert.Equal(new[] { Instance.FromId(Ring) }, added);
        Assert.Equal(new[] { Instance.FromId(Wine) }, removed);

        // Closed: its window's alarm 0 saves and goes.
        RunCode("gml_Object_o_container_parent_Alarm_0", Window, () => _world.Active.Remove(Window));
        Assert.Equal(new[] { Instance.FromId(Chest) }, closed);
    }

    // The game's item scripts, as they behave: an item made for an owner (a slot with its data), saved as an entry,
    // taken away.
    private readonly List<int> _destroyed = new();
    private int _nextSlot = 100_050;

    private void ItemScripts()
    {
        var scripts = new FakeScripts();
        GameScripts = scripts;
        _world.Assets["o_inv_wine"] = 400;
        _world.Parents[400] = (int)GameObjectId.o_inv_slot;
        scripts.Add("scr_inventory_add_item", a =>
        {
            int id = _nextSlot++;
            _world.Add(id, 400);
            var data = DsMap.Create();
            data["idName"] = "wine";
            _world.Vars[id] = new() { ["object_index"] = 400, ["owner"] = a[1], ["data"] = data.Id, ["stack"] = a[2].AsReal > 0 ? a[2] : 1, ["i_index"] = 0, ["charge"] = 0 };
            return id;
        });
        // (A weapon or armour, by its name: made for the owner it's run as.)
        scripts.Add("scr_inventory_add_weapon", a =>
        {
            int id = _nextSlot++;
            _world.Add(id, (int)GameObjectId.o_inv_slot);
            var data = DsMap.Create();
            data["idName"] = a[0];
            data["Duration"] = 100;
            data["MaxDuration"] = 100;
            _world.Vars[id] = new() { ["object_index"] = (int)GameObjectId.o_inv_slot, ["owner"] = Window, ["data"] = data.Id, ["stack"] = 1, ["i_index"] = 0, ["charge"] = 0 };
            return id;
        });
        // (The game's own script, not a built-in: a map's copy.)
        scripts.Add("ds_map_clone", a =>
        {
            var source = a[0].AsDsMap!.Value;
            var copy = DsMap.Create();
            foreach (GmValue key in source.Keys)
                copy[key] = source[key];
            return copy.Id;
        });
        scripts.Add("scr_save_item_single", a =>
        {
            var item = DsList.Create();
            item.Add(a[0]);
            item.AddMap(a[1].AsDsMap!.Value);
            for (int i = 2; i < a.Length; i++)
                item.Add(a[i]);
            return item.Id;
        });
        scripts.Add("scr_item_destroy", a =>
        {
            _destroyed.Add(a[0].AsInt);
            _world.Active.Remove(a[0].AsInt);
            return GmValue.Undefined;
        });
    }

    [Fact]
    public void Items_are_put_in_and_taken_out_of_a_closed_container_as_the_game_saves_them()
    {
        ItemScripts();
        var chest = Instance.FromId(Chest);

        // Three wine put in: made, saved as an entry, and the made one gone.
        Assert.True(Containers.AddItem(chest, "wine", 3));
        Assert.Equal(2, _loot.Count);
        var added = DsList.From(_loot[1])!.Value;
        Assert.Equal(("o_inv_wine", "wine", 3.0), (added[0].AsString, added.GetMap(1)!.Value["idName"].AsString, added[6].AsReal));
        Assert.Equal(new[] { 100_050 }, _destroyed);

        // Four taken out: the two first (the whole entry), then one of the three.
        Assert.Equal(4, Containers.RemoveItem(chest, "Wine", 4));
        Assert.Equal(1, _loot.Count);
        Assert.Equal(1, DsList.From(_loot[0])!.Value[6].AsReal);
        Assert.Equal(1, Containers.RemoveItem(chest, "wine", 5));
        Assert.Equal(0, _loot.Count);
    }

    [Fact]
    public void An_open_containers_items_are_put_in_and_taken_out_as_slots()
    {
        ItemScripts();
        OpenWindow();
        var open = new OpenContainer(Instance.FromId(Window));

        // (Put in its first free cell: made for its window.)
        InventoryItem wine = open.Add("wine", 2)!.Value;
        Assert.Equal(Instance.FromId(Window), wine.Owner);
        Assert.Equal(2, wine.Stack);

        Assert.Equal(1, open.Remove("wine"));
        Assert.Equal(1, wine.Stack);
        Assert.Equal(1, Containers.RemoveItem(Instance.FromId(Chest), "wine", 3));
        Assert.Contains(wine.Slot.Id, _destroyed);
    }

    private sealed class Blade : Weapon { public Blade() : base("sf_chest_blade", "sword") {} }

    [Fact]
    public void A_mods_weapon_goes_in_by_its_type()
    {
        ItemScripts();
        var context = new ModContext("containers_items_test");
        try
        {
            context.Items.Add(new Blade());
            Assert.True(Containers.AddItem<Blade>(Instance.FromId(Chest)));
            var saved = DsList.From(_loot[_loot.Count - 1])!.Value;
            Assert.Equal("containers_items_test__sf_chest_blade", saved[0].AsString);
            Assert.Equal(1, Containers.RemoveItem<Blade>(Instance.FromId(Chest)));

            // Its own values, set as it's made - saved with it.
            Assert.True(Containers.AddItem<Blade>(Instance.FromId(Chest), setup: blade =>
            {
                blade.Durability = 50;
                blade.SetData("mymod:kills", 3);
                Assert.False(blade.IsIdentified is false && blade.Data("identified").IsUndefined is false);
            }));
            var data = DsList.From(_loot[_loot.Count - 1])!.Value.GetMap(1)!.Value;
            Assert.Equal((50.0, 3.0), (data["Duration"].AsReal, data["mymod:kills"].AsReal));
        }
        finally
        {
            Hooks.RemoveMod(context.Id);
            Items.RemoveMod(context.Id);
        }
    }

    [Fact]
    public void Items_put_in_a_never_opened_container_join_its_loot_as_it_first_opens()
    {
        ItemScripts();
        var loaded = new List<(int List, int Window)>();
        ((FakeScripts)GameScripts!).Add("scr_loadContainerContent", a =>
        {
            loaded.Add((a[0].AsInt, a[3].AsInt));
            return GmValue.Undefined;
        });
        var loader = new ModContext("containers_loader_test");
        try
        {
            Containers.Install(loader);
            _world.Vars[Chest]["is_execute"] = false;
            _loot.Clear();

            // Stashed: waiting in it - and it's still unopened (its loot to be rolled).
            Assert.True(Containers.AddItem(Instance.FromId(Chest), "wine"));
            Assert.False(Containers.HasBeenOpened(Instance.FromId(Chest)));
            Assert.Equal(1, _loot.Count);

            // First opened: its window made by it as the loot's rolled (its parent only set after); on the next frame the
            // waiting items go in.
            _world.Add(Window, (int)GameObjectId.o_container_parent);
            _world.Vars[Window] = new();
            RunCode("gml_Object_o_container_parent_Create_0", Window, other: Chest);
            _world.Vars[Window]["parent"] = Chest;
            Assert.Empty(loaded);
            RunFrame();
            Assert.Equal(new[] { (_loot.Id, Window) }, loaded);
            Assert.Equal(0, _loot.Count);
        }
        finally
        {
            Hooks.RemoveMod(loader.Id);
        }
    }
}
