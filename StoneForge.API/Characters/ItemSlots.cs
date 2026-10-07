namespace StoneForge;

// Items made, taken away and saved as the game does it (for Inventory, OpenContainer and Containers): by name - one of
// the game's items by its o_inv_ object's name less "o_inv_" ("wine"), or a weapon or armour by its name (the game's, or
// a mod's) - for an owner (an inventory, a container's window, or a container in the world).
internal static class ItemSlots
{
    // An item made for an owner (scr_inventory_add_item, scr_inventory_add_weapon - as the owner), put in its first free
    // cell with place (one that doesn't fit is dropped on the ground, as the game does: none then); without, left in no
    // cell - to be saved somewhere (Save). With setup, the item's own values set as it's made. None if there's no such
    // item.
    public static Instance Make(Instance owner, string name, int stack, ItemQuality quality, bool place, Action<InventoryItem>? setup = null)
    {
        string key = ModIdentity.ToGameKey(name);
        int obj = Gm.AssetGetIndex("o_inv_" + key);
        GmValue made = obj >= 0 && Game.CallBuiltin("object_exists", obj).AsBool
            ? Game.CallScript("scr_inventory_add_item", owner, obj, owner, stack > 1 ? stack : -4, true, -4, place)
            : Game.CallScript("scr_inventory_add_weapon", owner, key, (int)quality, true, place, true);
        if (GroundItems.InstanceOf(made) is not { IsNone: false } slot || !slot.Exists)
            return default;
        setup?.Invoke(new InventoryItem(slot));
        return slot;
    }

    // An item's saved entry, as the game's save makes one (scr_save_item, scr_save_item_single): a list of its own - its
    // name, its data (a copy), no cell (it's put in the first free one as it loads), its look, charge and stack.
    public static GmValue Save(Instance slot)
    {
        GmValue obj = slot.Get("object_index");
        string name = obj.AsInt == (int)GameObjectId.o_inv_slot
            ? Items.IdName(slot) ?? ""
            : Game.CallBuiltin("object_get_name", obj).AsString ?? "";
        // (ds_map_clone: the game's own script, not a built-in.)
        GmValue data = Game.CallScript("ds_map_clone", default, slot.Get("data"));
        return Game.CallScript("scr_save_item_single", default, name, data, -4, -4, slot.Get("i_index"), slot.Get("charge"),
            slot.Get("stack"), false, false, "N/A");
    }

    // An item taken away as the game does it (scr_item_destroy: its own leaving first - taken off, if it was on).
    public static void Destroy(Instance slot)
    {
        if (!slot.IsNone && slot.Exists)
            Game.CallScript("scr_item_destroy", default, slot, true);
    }

    // Up to count of the items by a name an owner has, taken away - from their stacks, then whole: how many.
    public static int Remove(IReadOnlyList<InventoryItem> items, string name, int count)
    {
        string key = ModIdentity.ToGameKey(name);
        int removed = 0;
        foreach (InventoryItem item in items)
        {
            if (removed >= count)
                break;
            if (!Matches(item.Name, key))
                continue;
            int stack = item.Stack, take = Math.Min(stack, count - removed);
            if (take < stack)
                item.Slot.Set("stack", stack - take);
            else
                Destroy(item.Slot);
            removed += take;
        }
        return removed;
    }

    // A mod's item or consumable by its name in the game - once it's added.
    public static string NameOf(ModItem item) => item.GameKey is { Length: > 0 } key ? key
        : throw new InvalidOperationException($"{item.GetType().Name} hasn't been added: add it first (Items.Add, in the mod's Load).");

    public static string NameOf(Consumable consumable) => consumable.GameKey is { Length: > 0 } key ? key
        : throw new InvalidOperationException($"{consumable.GetType().Name} hasn't been added: add it first (Items.Add, in the mod's Load).");

    // Whether an item's name is this one (as the game keys it: "wine", "Linen Shirt").
    public static bool Matches(string itemName, string key) => string.Equals(ModIdentity.ToGameKey(itemName), key, StringComparison.OrdinalIgnoreCase);
}
