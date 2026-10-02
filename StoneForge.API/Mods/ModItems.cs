namespace StoneForge;

/// <summary>Item operations accessed through <see cref="ModContext.Items"/>. Registrations belong to that
/// context; names and queries retain the shared game-wide semantics of <see cref="StoneForge.Items"/>.</summary>
public sealed class ModItems
{
    private readonly ModContext _context;
    internal ModItems(ModContext context) => _context = context;

    /// <inheritdoc cref="StoneForge.Items.Add(ModContext, ModItem)"/>
    public void Add(ModItem item) => StoneForge.Items.Add(_context, item);
    /// <inheritdoc cref="StoneForge.Items.Add(ModContext, Consumable)"/>
    public void Add(Consumable consumable) => StoneForge.Items.Add(_context, consumable);
    /// <inheritdoc cref="StoneForge.Items.Give(string, ItemQuality, double?)"/>
    public bool Give(string name, ItemQuality quality = ItemQuality.Rolled, double? durabilityPercent = null)
        => StoneForge.Items.Give(name, quality, durabilityPercent);
    /// <inheritdoc cref="StoneForge.Items.Give(ModItem, ItemQuality, double?)"/>
    public bool Give(ModItem item, ItemQuality quality = ItemQuality.Rolled, double? durabilityPercent = null)
        => StoneForge.Items.Give(item, quality, durabilityPercent);
    /// <inheritdoc cref="StoneForge.Items.Give(Consumable, int)"/>
    public bool Give(Consumable consumable, int count = 1) => StoneForge.Items.Give(consumable, count);
    /// <inheritdoc cref="StoneForge.Items.Exists"/>
    public bool Exists(string name) => StoneForge.Items.Exists(name);
    /// <inheritdoc cref="StoneForge.Items.Stat(string, string)"/>
    public GmValue Stat(string name, string column) => StoneForge.Items.Stat(name, column);
    /// <summary>Reads a weapon table column.</summary>
    public GmValue Stat(string name, WeaponColumn column) => StoneForge.Items.Stat(name, column);
    /// <summary>Reads an armour table column.</summary>
    public GmValue Stat(string name, ArmorColumn column) => StoneForge.Items.Stat(name, column);
}
