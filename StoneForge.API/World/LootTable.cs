namespace StoneForge;

/// <summary>One of the game's loot tables (<see cref="LootTables"/>): what a container rolls as it's first opened - item
/// slots and five equipment slots, each with its chance - and the tier range its items are of. The game's own roll reads
/// nine item slots; StoneForge keeps any more a table's given (<see cref="Add(string, double, int, int, string)"/> with
/// the nine taken) and rolls them just after it, the same way - so every mod's additions get in, however many there are.
/// Changed, every roll of it after is so, for the rest of the game's run: change it from a mod's Load
/// (<see cref="LootTables.Edit"/>).</summary>
public sealed class LootTable
{
    /// <summary>How many item slots the game's own roll reads (slot1 to slot9).</summary>
    public const int GameSlots = 9;

    private readonly DsMap _row;

    internal LootTable(string name, DsMap row)
    {
        Name = name;
        _row = row;
        EquipmentSlots = Enumerable.Range(1, 5).Select(n => new LootEquipmentSlot(row, n)).ToArray();
    }

    /// <summary>Its name: a container's loot key and the place's tier ("cryptTomb3"), or the key alone.</summary>
    public string Name { get; }

    /// <summary>Its item slots, in order: the game's nine, then any StoneForge keeps beyond them (<see cref="LootSlot.IsExtra"/>
    /// - number 10 on, in the same row as slot10, slot11..., which the game's roll doesn't read: StoneForge rolls them just
    /// after it).</summary>
    public IReadOnlyList<LootSlot> Slots => Enumerable.Range(1, SlotCount(_row)).Select(n => new LootSlot(_row, n)).ToArray();

    // How many item slots a row has: the game's nine, and its extra ones (slot10 on, as long as they go).
    internal static int SlotCount(DsMap row)
    {
        int count = GameSlots;
        while (row.Has("slot" + (count + 1)))
            count++;
        return count;
    }

    /// <summary>Its five equipment slots, in order.</summary>
    public IReadOnlyList<LootEquipmentSlot> EquipmentSlots { get; }

    /// <summary>The tiers its items are of, as the table has it: "" for the place's own, "4" for one, "4,5" for a range.</summary>
    public string TierMod
    {
        get => _row["tierMod"] is { Kind: GmKind.String } text ? text.AsString : "";
        set => _row["tierMod"] = value;
    }

    /// <summary>Puts an item in its first empty slot: <paramref name="min"/> to <paramref name="max"/> of it,
    /// <paramref name="chance"/>% of rolls. One of the game's items by its o_inv_ object's name less "o_inv_" ("wine"), or
    /// a kind of item the game picks one of ("gem", with <paramref name="tags"/> to narrow it), or several of either
    /// separated by commas to choose one from. The game's nine taken, it's one of StoneForge's beyond them, rolled just
    /// after the game's own - so it always goes in. The slot.</summary>
    public LootSlot Add(string item, double chance, int min = 1, int max = 1, string tags = "")
    {
        var slots = Slots;
        LootSlot slot = slots.FirstOrDefault(s => s.IsEmpty) ?? new LootSlot(_row, slots.Count + 1);
        slot.Set(item, chance, min, max, tags);
        return slot;
    }

    /// <summary>Puts a mod's consumable in its first empty slot (see <see cref="Add(string, double, int, int, string)"/>).</summary>
    public LootSlot Add(Consumable consumable, double chance, int min = 1, int max = 1)
        => Add(ItemSlots.NameOf(consumable), chance, min, max);

    public override string ToString() => $"{Name}: {string.Join("; ", Slots.Where(s => !s.IsEmpty))}; {string.Join("; ", EquipmentSlots.Where(s => !s.IsEmpty))}";

    // An item as a loot table names it: its object ("o_inv_wine") if the game has one by that name; else as it is (a kind:
    // "gem").
    internal static string GameName(string item)
    {
        if (item.StartsWith("o_inv_", StringComparison.Ordinal))
            return item;
        string key = ModIdentity.ToGameKey(item);
        return Gm.AssetGetIndex("o_inv_" + key) >= 0 ? "o_inv_" + key : item;
    }

    // "5,15" (or "180, 230"), "3" or "": a range.
    internal static (int Min, int Max) Range(string text, (int Min, int Max) none)
    {
        var parts = text.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !int.TryParse(parts[0], out int min))
            return none;
        return parts.Length > 1 && int.TryParse(parts[1], out int max) ? (min, max) : (min, min);
    }
}
