namespace StoneForge;

/// <summary>What the player carries - the items whose owner is the player's inventory (o_inventory), worn and in hand
/// included, as the game's save counts them (scr_save_item) - and the player gaining an item, losing one, or putting
/// one on or taking it off (<see cref="OnAdded"/>, <see cref="OnRemoved"/>, <see cref="OnEquipped"/>). What's in a bag
/// isn't here: a bag's contents are saved in it while it's closed (<see cref="Containers"/>). Game thread only, in a
/// game.</summary>
/// <example><code>
/// Inventory.OnAdded(context, item => context.Log($"Picked up {item.Name} x{item.Stack}"));
/// </code></example>
public static class Inventory
{
    /// <summary>The player's inventory (o_inventory): the owner of what they carry; none with no game.</summary>
    public static Instance Owner => Instances.All(GameObjectId.o_inventory).FirstOrDefault();

    /// <summary>Every item the player carries; none with no game.</summary>
    public static IReadOnlyList<InventoryItem> Items() => ItemsOf(Owner);

    /// <summary>Gives the player an item, as the game does - in their first free cell: one of the game's items by its
    /// o_inv_ object's name less "o_inv_" ("wine"), or a weapon or armour by its name (the game's, or a mod's) at
    /// <paramref name="quality"/>; <paramref name="stack"/> of it, for an item that stacks. The item; null if there's no
    /// such item, no game, or no room (the game drops it at the player's feet then). With <paramref name="setup"/>, the
    /// item's own values set as it's made (its <see cref="InventoryItem.Durability"/>, a mod's
    /// <see cref="InventoryItem.SetData"/>...).</summary>
    public static InventoryItem? Add(string name, int stack = 1, ItemQuality quality = ItemQuality.Rolled, Action<InventoryItem>? setup = null)
        => Owner is { IsNone: false } owner && ItemSlots.Make(owner, name, stack, quality, place: true, setup) is { IsNone: false } slot
            ? new InventoryItem(slot) : null;

    /// <summary>Gives the player a mod's weapon or armour - the one of type <typeparamref name="T"/> it added.</summary>
    public static InventoryItem? Add<T>(ItemQuality quality = ItemQuality.Rolled, Action<InventoryItem>? setup = null) where T : ModItem
        => Add(StoneForge.Items.Get<T>(), quality, setup);

    /// <summary>Gives the player a mod's weapon or armour.</summary>
    public static InventoryItem? Add(ModItem item, ItemQuality quality = ItemQuality.Rolled, Action<InventoryItem>? setup = null)
        => Add(ItemSlots.NameOf(item), 1, quality, setup);

    /// <summary>Gives the player a mod's consumable (<paramref name="stack"/> of it, for one that stacks).</summary>
    public static InventoryItem? Add(Consumable consumable, int stack = 1, Action<InventoryItem>? setup = null)
        => Add(ItemSlots.NameOf(consumable), stack, ItemQuality.Rolled, setup);

    /// <summary>Takes an item away from the player, as the game does (taken off first, if it's on).</summary>
    public static void Remove(InventoryItem item) => ItemSlots.Destroy(item.Slot);

    /// <summary>Takes up to <paramref name="count"/> of an item away from the player, by name - from its stacks, then
    /// whole: how many were taken.</summary>
    public static int Remove(string name, int count = 1) => ItemSlots.Remove(Items(), name, count);

    /// <summary>Takes up to <paramref name="count"/> of a mod's weapon or armour - the one of type
    /// <typeparamref name="T"/> - away from the player: how many were taken.</summary>
    public static int Remove<T>(int count = 1) where T : ModItem => Remove(ItemSlots.NameOf(StoneForge.Items.Get<T>()), count);

    /// <summary>Takes up to <paramref name="count"/> of a mod's consumable away from the player: how many were taken.</summary>
    public static int Remove(Consumable consumable, int count = 1) => Remove(ItemSlots.NameOf(consumable), count);

    /// <summary>Runs as the player comes to carry an item - picked up, bought, taken from a chest, made, given - once a
    /// frame, after the game's done with it. Not the items a game loads with.</summary>
    public static void OnAdded(ModContext context, Action<InventoryItem> handler)
        => Watch(context, (was, now) =>
        {
            foreach (var (slot, _) in now)
                if (!was.ContainsKey(slot))
                    handler(new InventoryItem(slot));
        });

    /// <summary>Runs as an item stops being the player's - dropped, sold, put in a chest, used up, destroyed - once a
    /// frame, after the game's done with it (an item used up is gone by then: only its <see cref="InventoryItem.Slot"/>
    /// is left to compare).</summary>
    public static void OnRemoved(ModContext context, Action<InventoryItem> handler)
        => Watch(context, (was, now) =>
        {
            foreach (var (slot, _) in was)
                if (!now.ContainsKey(slot))
                    handler(new InventoryItem(slot));
        });

    /// <summary>Runs as the player puts an item on or takes it off: the item, and whether it's on now.</summary>
    public static void OnEquipped(ModContext context, Action<InventoryItem, bool> handler)
        => Watch(context, (was, now) =>
        {
            foreach (var (slot, equipped) in now)
                if (was.TryGetValue(slot, out bool wasEquipped) && wasEquipped != equipped)
                    handler(new InventoryItem(slot), equipped);
        });

    // The items an owner has: its item slots (an inventory, an open container's window).
    internal static IReadOnlyList<InventoryItem> ItemsOf(Instance owner)
    {
        if (owner.IsNone)
            return Array.Empty<InventoryItem>();
        var items = new List<InventoryItem>();
        foreach (Instance slot in Instances.All(GameObjectId.o_inv_slot))
            if (Instance.Of(slot.Get("owner")).Equals(owner))
                items.Add(new InventoryItem(slot));
        return items;
    }

    // What the player carries, compared each frame with the last (a new inventory - a game loaded or begun - starts afresh).
    private static void Watch(ModContext context, Action<Dictionary<Instance, bool>, Dictionary<Instance, bool>> changed)
        => new ItemSlotWatch(context, () => Owner is { IsNone: false } owner ? new[] { owner } : Array.Empty<Instance>(),
            (_, was, now) => changed(was, now));
}
