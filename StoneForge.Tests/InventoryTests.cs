using StoneForge;

// What the player carries (Inventory): the items owned by the player's inventory, and their coming, going and being
// put on, compared frame by frame - a new inventory (a game loaded) starting afresh (laid out with FakeGame's room).
public class InventoryTests : FakeGame
{
    private const int InventoryId = 100_001, ChestId = 100_002, Sword = 100_010, Wine = 100_011, Ring = 100_012, NewInventory = 100_003;
    private readonly FakeWorld _world = new();
    private readonly ModContext _context = new("inventory_test");

    public InventoryTests()
    {
        World = _world;
        _world.LendsIds = true;
        _world.Add(InventoryId, (int)GameObjectId.o_inventory);
        _world.Add(ChestId, 300);
        Slot(Sword, InventoryId, equipped: true);
        Slot(Wine, InventoryId);
        Slot(Ring, ChestId);
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    private void Slot(int id, int owner, bool equipped = false)
    {
        if (!_world.Objects.ContainsKey(id))
            _world.Add(id, (int)GameObjectId.o_inv_slot);
        _world.Vars[id] = new() { ["owner"] = owner, ["equipped"] = equipped, ["stack"] = 1 };
    }

    [Fact]
    public void The_items_carried_are_those_the_players_inventory_owns()
        => Assert.Equal(new[] { Instance.FromId(Sword), Instance.FromId(Wine) }, Inventory.Items().Select(i => i.Slot));

    [Fact]
    public void Gaining_losing_and_putting_on_an_item_are_told_once_a_frame()
    {
        var added = new List<Instance>();
        var removed = new List<Instance>();
        var equipped = new List<(Instance, bool)>();
        Inventory.OnAdded(_context, item => added.Add(item.Slot));
        Inventory.OnRemoved(_context, item => removed.Add(item.Slot));
        Inventory.OnEquipped(_context, (item, on) => equipped.Add((item.Slot, on)));

        // (The first look: where it starts.)
        RunFrame();
        Assert.Empty(added);

        // The ring taken from the chest, the wine put in it, the sword taken off.
        Slot(Ring, InventoryId, equipped: true);
        Slot(Wine, ChestId);
        Slot(Sword, InventoryId, equipped: false);
        RunFrame();
        Assert.Equal(new[] { Instance.FromId(Ring) }, added);
        Assert.Equal(new[] { Instance.FromId(Wine) }, removed);
        Assert.Equal(new[] { (Instance.FromId(Sword), false) }, equipped);

        // (A game loaded: a new inventory, with what it has - not news.)
        _world.Active.Remove(InventoryId);
        _world.Add(NewInventory, (int)GameObjectId.o_inventory);
        Slot(Wine, NewInventory);
        RunFrame();
        Assert.Single(added);
        Assert.Single(removed);
    }
}
