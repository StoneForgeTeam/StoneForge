namespace StoneForge;

/// <summary>An item as a slot (o_inv_slot and its kinds, kept by id): one the player carries (<see cref="Inventory"/>), or
/// one in an open container (<see cref="OpenContainer"/>). Each item has its own data - its condition, quality, whether
/// it's identified, and any values a mod gives it (<see cref="SetData"/>) - kept with it wherever it goes, and saved with
/// it.</summary>
/// <example><code>
/// if (Inventory.Add&lt;MyBlade&gt;() is { } blade)
/// {
///     blade.Durability = 50;
///     blade.SetData("mymod:kills", 0);
/// }
/// </code></example>
public readonly record struct InventoryItem(Instance Slot)
{
    /// <summary>The item's name as the game keeps it (its data's idName: "Wine", "Linen Shirt"...); its object's name
    /// if it has none.</summary>
    public string Name => Items.IdName(Slot) ?? Game.CallBuiltin("object_get_name", Slot.Get("object_index")).AsString ?? "";

    /// <summary>Whether the player has it on - worn, or in hand.</summary>
    public bool IsEquipped => Slot.Get("equipped").AsBool;

    /// <summary>How many there are in its stack (1 for an item that doesn't stack).</summary>
    public int Stack => Slot.Get("stack") is { Kind: GmKind.Real } stack ? Math.Max(1, stack.AsInt) : 1;

    /// <summary>What holds it: the player's inventory (<see cref="Inventory.Owner"/>), or an open container's window.</summary>
    public Instance Owner => Instance.Of(Slot.Get("owner"));

    /// <summary>Whether it's still there - wherever it is.</summary>
    public bool Exists => Slot.Exists;

    /// <summary>Its condition, in points (as its tooltip shows: <see cref="Durability"/> / <see cref="MaxDurability"/>);
    /// set, it's kept between none and full. 0 for an item without one.</summary>
    public double Durability
    {
        get => Data("Duration") is { Kind: GmKind.Real } duration ? duration.AsReal : 0;
        set => SetData("Duration", Math.Clamp(value, 0, MaxDurability));
    }

    /// <summary>Its full condition, in points (0 for an item without one).</summary>
    public double MaxDurability => Data("MaxDuration") is { Kind: GmKind.Real } max ? max.AsReal : 0;

    /// <summary>Its condition, in % of full.</summary>
    public double DurabilityPercent
    {
        get => MaxDurability > 0 ? Durability * 100 / MaxDurability : 0;
        set => Durability = MaxDurability * Math.Clamp(value, 0, 100) / 100;
    }

    /// <summary>Its quality (the game's rarity).</summary>
    public ItemQuality Quality => (ItemQuality)Data("quality").AsInt;

    /// <summary>Whether it's identified (an unidentified one shows as "?" until it is).</summary>
    public bool IsIdentified
    {
        get => Data("identified") is not { Kind: GmKind.Real or GmKind.Bool } identified || identified.AsBool;
        set => SetData("identified", value);
    }

    /// <summary>A value of its own: the game's ("Duration", "quality", "identified"...) or a mod's (<see cref="SetData"/>);
    /// undefined if it has none.</summary>
    public GmValue Data(string key)
        => Slot.Get("data") is { Kind: GmKind.Real } data ? Game.CallBuiltin("ds_map_find_value", data, key) : GmValue.Undefined;

    /// <summary>Sets a value of its own - kept with it, and saved with it, wherever it goes (a chest, the ground, a save).
    /// A mod's own keys are best named for it ("mymod:kills"), not to meet the game's or another mod's.</summary>
    public void SetData(string key, GmValue value)
    {
        if (Slot.Get("data") is { Kind: GmKind.Real } data)
            Game.CallBuiltin("ds_map_set", data, key, value);
    }
}
