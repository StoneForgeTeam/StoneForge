namespace StoneForge;

/// <summary>One item in the game - in the player's inventory, a chest, a shop (the game's o_inv_slot): a
/// weapon or armour, the game's or a mod's (<see cref="Type"/>).</summary>
public sealed class Item
{
    internal Item(Instance instance, ModItem? type, string name)
    {
        Instance = instance;
        Type = type;
        Name = name;
    }

    /// <summary>The game's instance (its o_inv_slot), for anything not covered here.</summary>
    public Instance Instance { get; }
    /// <summary>The mod item it is (null: one of the game's).</summary>
    public ModItem? Type { get; }
    /// <summary>Its name in the game's tables ("yourmod__Example Blade" for a mod's: <see cref="ModItem.Id"/> with __ for the :).</summary>
    public string Name { get; }

    /// <summary>Whether it's still in the game.</summary>
    public bool Exists => Items.Tracked.Contains(this);
    /// <summary>Whether the player has it on.</summary>
    public bool IsEquipped => Exists && Instance.Get("equipped").AsBool;
    /// <summary>Its condition, in points (as its tooltip shows: <see cref="Durability"/> / <see cref="MaxDurability"/>).</summary>
    public double Durability
    {
        get => Data("Duration").AsReal;
        set => SetData("Duration", Math.Clamp(value, 0, MaxDurability));
    }
    /// <summary>Its full condition, in points.</summary>
    public double MaxDurability => Data("MaxDuration").AsReal;
    /// <summary>Its condition, in % of full.</summary>
    public double DurabilityPercent
    {
        get => MaxDurability > 0 ? Durability * 100 / MaxDurability : 0;
        set => Durability = MaxDurability * Math.Clamp(value, 0, 100) / 100;
    }
    /// <summary>Its quality (the game's rarity).</summary>
    public ItemQuality Quality => (ItemQuality)Data("quality").AsInt;

    /// <summary>A value of its own (the game keeps an item's state in a map: "Duration", "quality",
    /// "identified"...; undefined if there's none).</summary>
    public GmValue Data(string key)
    {
        GmValue data = DataMap;
        return data.Kind == GmKind.Real ? Game.CallBuiltinTrusted("ds_map_find_value", default, default, data, key) : GmValue.Undefined;
    }

    /// <summary>A mod's own values on this item - kept and saved with it - under keys only that mod uses
    /// (<see cref="StoneForge.ModData"/>).</summary>
    public ModData ModData(ModContext context) => StoneForge.ModData.InMap(context, () => DataMap.AsDsMap);

    /// <summary>Sets a value of its own (kept with it, saved with it).</summary>
    public void SetData(string key, GmValue value)
    {
        GmValue data = DataMap;
        if (data.Kind == GmKind.Real)
            Game.CallBuiltinTrusted("ds_map_set", default, default, data, key, value);
    }

    private GmValue DataMap => Exists ? Instance.Get("data") : GmValue.Undefined;

    public override string ToString() => $"{Name} ({Instance})";
}
