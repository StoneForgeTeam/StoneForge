using System.Globalization;

namespace StoneForge;

/// <summary>A mod's consumable - food, a drink, a potion, a scroll: one of the game's (<see cref="BasedOn"/>, its id)
/// under the mod's own key - its stats, sprites, sounds and what it does - with what the mod changes, and its
/// <see cref="OnUse"/>. Inherit the game's from StoneForge.GameItems (<c>class Tonic : Wine</c>), or this with its id:
/// <code>
/// public class Tonic : Wine
/// {
///     public Tonic() : base("example_tonic")
///     {
///         DisplayName = "Example Tonic";
///         InventorySprite = "tonic_inv.png";
///         Set(ConsumableColumn.Health_Restoration, 20);
///     }
///     protected override void OnUse(Instance item) => Context.Log("Drunk!");
/// }
/// </code>
/// Add it with <see cref="Items.Add(ModContext, Consumable)"/>: it's then <see cref="Id"/> "yourmod:example_tonic".
/// Its key must be written as a string literal in the base(...) call: StoneForge's patcher reads it (and the mod's
/// ID, from its mod.json) from the source to give the game an object for it, o_inv_yourmod__example_tonic (a child of
/// the game item's), at the game's next start - so the game makes, saves, stacks and drops it as its own.</summary>
public abstract class Consumable
{
    private readonly Dictionary<string, string> _columns = new();

    protected Consumable(string key, string basedOn)
    {
        if (string.IsNullOrWhiteSpace(key) || !key.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
            throw new ArgumentException("A consumable's key is letters, digits and _ only", nameof(key));
        ModIdentity.CheckKey(key, "consumable");
        Key = key;
        BasedOn = basedOn;
    }

    /// <summary>Its key in its mod ("example_tonic"; saves refer to it by this and the mod's ID - don't change it
    /// once players have it).</summary>
    public string Key { get; }
    /// <summary>Its full ID, "yourmod:example_tonic" (set when it's added).</summary>
    public string Id { get; private set; } = "";
    // Its name in the game's data: its objects are o_inv_ / o_loot_ + this ("yourmod__example_tonic").
    internal string GameKey { get; private set; } = "";
    /// <summary>The game consumable it's based on (its id: "wine").</summary>
    public string BasedOn { get; }
    /// <summary>Its name in game (default: the game item's).</summary>
    public string? DisplayName { get; protected set; }
    /// <summary>Its description (default: the game item's).</summary>
    public string? Description { get; protected set; }
    /// <summary>Its inventory picture, a PNG in the mod's Assets folder: the game item's size (27 pixels a cell)
    /// and frames (side by side - some have a frame per look, or per charge left). Default: the game item's.</summary>
    public string? InventorySprite { get; protected set; }
    /// <summary>Its picture on the ground, a PNG in the mod's Assets folder (as the game item's). Default: the game
    /// item's.</summary>
    public string? LootSprite { get; protected set; }

    /// <summary>Whether its tooltip says which mod it's from ("Mod: Example Mod", at its bottom left).</summary>
    public bool ShowModName { get; protected set; } = true;

    /// <summary>The mod that added it; set by <see cref="Items.Add(ModContext, Consumable)"/>.</summary>
    protected ModContext Context { get; private set; } = null!;

    /// <summary>Used by the player (eaten, drunk, read), as the game item's use begins: its own effect - and the
    /// game item's, which follows - on top.</summary>
    protected internal virtual void OnUse(Instance item) { }

    /// <summary>One of its stats in the game's table (table_items_stats), from the game item's.</summary>
    protected void Set(ConsumableColumn column, double value) => SetColumn(column.ToString(), value.ToString(CultureInfo.InvariantCulture));
    protected void Set(ConsumableColumn column, string value) => SetColumn(column.ToString(), value);

    private void SetColumn(string column, string value)
    {
        Items.CheckText(value, nameof(value));
        _columns[column] = value;
    }

    internal string ColumnsText => string.Join("|", _columns.Select(c => c.Key + "=" + c.Value));
    internal void Attach(ModContext context)
    {
        Context = context;
        Id = context.ContentId(Key);
        GameKey = context.GameKey(Key);
    }
}
