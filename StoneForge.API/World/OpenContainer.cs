namespace StoneForge;

/// <summary>A container open on screen (<see cref="Containers"/>): its window (o_container: a chest's, a barrel's...;
/// o_container_inventory: a bag's), kept by id, and what it belongs to.</summary>
public readonly record struct OpenContainer(Instance Window)
{
    /// <summary>What it's the window of: a container in the world (c_container's kinds), or a bag the player has (an
    /// item: a backpack, a casket, a quiver).</summary>
    public Instance Container => Instance.Of(Window.Get("parent"));

    /// <summary>The items in it now - ordinary item slots while it's open.</summary>
    public IReadOnlyList<InventoryItem> Items() => Inventory.ItemsOf(Window);

    /// <summary>Puts an item in it, as the game does - in its first free cell (<see cref="Inventory.Add"/>'s names). The
    /// item; null if there's no such item, or no room (the game drops it on the ground then). With
    /// <paramref name="setup"/>, the item's own values set as it's made.</summary>
    public InventoryItem? Add(string name, int stack = 1, ItemQuality quality = ItemQuality.Rolled, Action<InventoryItem>? setup = null)
        => ItemSlots.Make(Window, name, stack, quality, place: true, setup) is { IsNone: false } slot ? new InventoryItem(slot) : null;

    /// <summary>Puts a mod's weapon or armour in it - the one of type <typeparamref name="T"/> the mod added.</summary>
    public InventoryItem? Add<T>(ItemQuality quality = ItemQuality.Rolled, Action<InventoryItem>? setup = null) where T : ModItem
        => Add(ItemSlots.NameOf(StoneForge.Items.Get<T>()), 1, quality, setup);

    /// <summary>Puts a mod's consumable in it (<paramref name="stack"/> of it, for one that stacks).</summary>
    public InventoryItem? Add(Consumable consumable, int stack = 1, Action<InventoryItem>? setup = null)
        => Add(ItemSlots.NameOf(consumable), stack, ItemQuality.Rolled, setup);

    /// <summary>Takes an item out of it, as the game does.</summary>
    public void Remove(InventoryItem item) => ItemSlots.Destroy(item.Slot);

    /// <summary>Takes up to <paramref name="count"/> of an item out of it, by name: how many were taken.</summary>
    public int Remove(string name, int count = 1) => ItemSlots.Remove(Items(), name, count);

    /// <summary>Takes up to <paramref name="count"/> of a mod's weapon or armour out of it - the one of type
    /// <typeparamref name="T"/>: how many were taken.</summary>
    public int Remove<T>(int count = 1) where T : ModItem => Remove(ItemSlots.NameOf(StoneForge.Items.Get<T>()), count);

    /// <summary>Takes up to <paramref name="count"/> of a mod's consumable out of it: how many were taken.</summary>
    public int Remove(Consumable consumable, int count = 1) => Remove(ItemSlots.NameOf(consumable), count);

    /// <summary>Whether it's still open.</summary>
    public bool IsOpen => Window.Exists;
}
