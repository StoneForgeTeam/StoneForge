namespace StoneForge;

/// <summary>One of a loot table's item slots (<see cref="LootTable.Slots"/>): what it may give - one of the game's
/// items, or a kind of item the game picks one of ("gem", "valuable", "treatise"...; <see cref="Tags"/> narrow it), or
/// several to choose one from at random - how many, and how likely, each time the table is rolled. Changed, every roll of
/// the table after gives it so (for the rest of the game's run).</summary>
public sealed class LootSlot
{
    private readonly DsMap _row;
    private readonly string _key;

    internal LootSlot(DsMap row, int number)
    {
        _row = row;
        Number = number;
        _key = "slot" + number;
    }

    /// <summary>Its number in the table: 1 to 9 the game's own, 10 on StoneForge's (<see cref="IsExtra"/>).</summary>
    public int Number { get; }

    /// <summary>Whether it's one of StoneForge's beyond the game's nine: rolled just after the game's own roll, the same
    /// way (by the game's own loot script).</summary>
    public bool IsExtra => Number > LootTable.GameSlots;

    /// <summary>Whether it gives nothing.</summary>
    public bool IsEmpty => Items.Count == 0;

    /// <summary>What it may give, as the game names them - an item's object ("o_inv_wine") or a kind ("gem"); one is
    /// chosen at random each roll.</summary>
    public IReadOnlyList<string> Items
    {
        get => Text(_key).Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        set => _row[_key] = string.Join(", ", value);
    }

    /// <summary>How likely it gives anything each roll, in % (0 to 100).</summary>
    public double Chance
    {
        get => double.TryParse(Text(_key + "_chance"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double chance) ? chance : 0;
        set => _row[_key + "_chance"] = Math.Clamp(value, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>How many it gives when it does: between <c>Min</c> and <c>Max</c> (1 if the table says nothing).</summary>
    public (int Min, int Max) Count
    {
        get => LootTable.Range(Text(_key + "_count"), (1, 1));
        set => _row[_key + "_count"] = value.Min == value.Max ? $"{value.Min}" : $"{value.Min},{value.Max}";
    }

    /// <summary>What narrows a kind of item down ("crypt", "common uncommon rare"...); none for an item.</summary>
    public string Tags
    {
        get => Text(_key + "_tags");
        set => _row[_key + "_tags"] = value;
    }

    /// <summary>Makes it give one of these items or kinds, <paramref name="min"/> to <paramref name="max"/> of it,
    /// <paramref name="chance"/>% of rolls. An item is named as <see cref="LootTable.Add(string, double, int, int, string)"/>
    /// takes it.</summary>
    public void Set(string item, double chance, int min = 1, int max = 1, string tags = "")
    {
        Items = item.Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(LootTable.GameName).ToArray();
        Chance = chance;
        Count = (Math.Max(1, min), Math.Max(Math.Max(1, min), max));
        Tags = tags;
    }

    /// <summary>Makes it give nothing.</summary>
    public void Clear()
    {
        foreach (string suffix in new[] { "", "_chance", "_count", "_tags" })
            _row[_key + suffix] = "";
    }

    public override string ToString() => IsEmpty ? $"slot {Number}: nothing" : $"slot {Number}: {string.Join(" or ", Items)} x{Count.Min}-{Count.Max}, {Chance}%";

    private string Text(string key) => _row[key] is { Kind: GmKind.String } text ? text.AsString : _row[key].IsUndefined ? "" : _row[key].ToString();
}
