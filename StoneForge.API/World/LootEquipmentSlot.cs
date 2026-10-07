using System.Globalization;

namespace StoneForge;

/// <summary>One of a loot table's five equipment slots (<see cref="LootTable.EquipmentSlots"/>): a weapon, armour or
/// jewelry the game picks for it - of these kinds ("weapon", "armor", "jewelry", "dagger", "2HStaff"...), with these
/// tags ("aldor", "magic"...) and rarities ("common", "uncommon", "rare", "unique") - its condition between two %, and how
/// likely it is each roll. Mod weapons and armour join the pick as the game's do (<see cref="ModItem"/>'s random loot).</summary>
public sealed class LootEquipmentSlot
{
    private readonly DsMap _row;
    private readonly string _key;

    internal LootEquipmentSlot(DsMap row, int number)
    {
        _row = row;
        Number = number;
        _key = "eq" + number;
    }

    /// <summary>Its number in the table, 1 to 5.</summary>
    public int Number { get; }

    /// <summary>Whether it gives nothing.</summary>
    public bool IsEmpty => Kinds.Count == 0;

    /// <summary>The kinds of equipment it may give ("weapon", "armor", "jewelry", or a weapon's type: "dagger"...).</summary>
    public IReadOnlyList<string> Kinds
    {
        get => List(_key);
        set => _row[_key] = string.Join(", ", value);
    }

    /// <summary>What narrows the pick ("aldor", "magic", "unique"...).</summary>
    public string Tags
    {
        get => Text(_key + "_tags");
        set => _row[_key + "_tags"] = value;
    }

    /// <summary>The rarities it may be ("common", "uncommon", "rare", "unique").</summary>
    public IReadOnlyList<string> Rarities
    {
        get => List(_key + "_rarity");
        set => _row[_key + "_rarity"] = string.Join(", ", value);
    }

    /// <summary>Its condition, between <c>Min</c> and <c>Max</c> % of full.</summary>
    public (int Min, int Max) Durability
    {
        get => LootTable.Range(Text(_key + "_dur"), (100, 100));
        set => _row[_key + "_dur"] = $"{value.Min},{value.Max}";
    }

    /// <summary>How likely it gives anything each roll, in % (0 to 100).</summary>
    public double Chance
    {
        get => double.TryParse(Text(_key + "_chance"), NumberStyles.Float, CultureInfo.InvariantCulture, out double chance) ? chance : 0;
        set => _row[_key + "_chance"] = Math.Clamp(value, 0, 100).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Makes it give nothing.</summary>
    public void Clear()
    {
        foreach (string suffix in new[] { "", "_tags", "_rarity", "_dur", "_chance" })
            _row[_key + suffix] = "";
    }

    public override string ToString() => IsEmpty ? $"eq {Number}: nothing" : $"eq {Number}: {string.Join(" or ", Kinds)} ({string.Join("/", Rarities)}), {Chance}%";

    private IReadOnlyList<string> List(string key) => Text(key).Split(",", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string Text(string key) => _row[key] is { Kind: GmKind.String } text ? text.AsString : _row[key].IsUndefined ? "" : _row[key].ToString();
}
