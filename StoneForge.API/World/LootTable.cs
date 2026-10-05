namespace StoneForge;

/// <summary>One of the game's loot tables (<see cref="LootTables"/>): what a container rolls as it's first opened - nine
/// item slots and five equipment slots, each with its chance - and the tier range its items are of. Changed, every roll
/// of it after is so, for the rest of the game's run: change it from a mod's Load (<see cref="LootTables.Edit"/>).</summary>
public sealed class LootTable
{
    private readonly DsMap _row;

    internal LootTable(string name, DsMap row)
    {
        Name = name;
        _row = row;
        Slots = Enumerable.Range(1, 9).Select(n => new LootSlot(row, n)).ToArray();
        EquipmentSlots = Enumerable.Range(1, 5).Select(n => new LootEquipmentSlot(row, n)).ToArray();
    }

    /// <summary>Its name: a container's loot key and the place's tier ("cryptTomb3"), or the key alone.</summary>
    public string Name { get; }

    /// <summary>Its nine item slots, in order.</summary>
    public IReadOnlyList<LootSlot> Slots { get; }

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
    /// separated by commas to choose one from. The slot; null if all nine are taken.</summary>
    public LootSlot? Add(string item, double chance, int min = 1, int max = 1, string tags = "")
    {
        if (Slots.FirstOrDefault(slot => slot.IsEmpty) is not { } free)
            return null;
        free.Set(item, chance, min, max, tags);
        return free;
    }

    /// <summary>Puts a mod's consumable in its first empty slot (see <see cref="Add(string, double, int, int, string)"/>).</summary>
    public LootSlot? Add(Consumable consumable, double chance, int min = 1, int max = 1)
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
