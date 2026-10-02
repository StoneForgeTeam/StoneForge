using System.Globalization;

namespace StoneForge;

/// <summary>A mod's item: inherit from <see cref="Weapon"/> or <see cref="Armor"/>. It starts as a copy of one
/// of the game's items (<see cref="BasedOn"/>: its stats, sprites and sounds) under its own name; its
/// constructor changes what it likes (<c>Set</c> its stats, its pictures, its description), and its
/// <c>On...</c> methods react to what happens to it in game, each with the <see cref="Item"/> it happened to.
/// Add it with <see cref="Items.Add(ModContext, ModItem)"/>.</summary>
public abstract class ModItem
{
    private readonly Dictionary<string, string> _columns = new();

    private protected ModItem(string key, string basedOn)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("An item needs a key", nameof(key));
        Items.CheckText(key, nameof(key));
        ModIdentity.CheckKey(key, "item");
        Key = key;
        BasedOn = basedOn;
    }

    /// <summary>Its key in its mod ("Example Blade"): saves and the game's tables refer to it by this and the mod's
    /// ID, so don't change it once players have it. Shown as its name unless <see cref="DisplayName"/> is set.</summary>
    public string Key { get; }
    /// <summary>Its full ID, "yourmod:Example Blade" (set when it's added): give it by this with
    /// <see cref="Items.Give(string, ItemQuality, double?)"/>.</summary>
    public string Id { get; private set; } = "";
    // Its name in the game's tables and saves: "yourmod__Example Blade".
    internal string GameKey { get; private set; } = "";
    /// <summary>The game item it starts as (its name in the game's tables, e.g. "Drifter Sword").</summary>
    public string BasedOn { get; }
    /// <summary>The name shown in game (default: <see cref="Key"/>).</summary>
    public string? DisplayName { get; protected set; }
    /// <summary>Its description (default: the game item's).</summary>
    public string? Description { get; protected set; }
    /// <summary>Its inventory picture, a PNG in the mod's Assets folder: 27 pixels per inventory cell (a sword is 27x81,
    /// 1x3 cells). The game's have a frame per state of wear - a weapon's 3 (good, worn, broken), armour's 2 -
    /// side by side (<see cref="InventoryFrames"/>); with one frame it always looks the same. Default: the game
    /// item's.</summary>
    public string? InventorySprite { get; protected set; }
    public int InventoryFrames { get; protected set; } = 1;
    /// <summary>Its picture on the ground, a PNG in the mod's Assets folder (centred on its tile). Default: the game
    /// item's.</summary>
    public string? LootSprite { get; protected set; }
    /// <summary>Its picture in its equipment slot, a PNG in the mod's Assets folder - the game's are the inventory
    /// picture's size and frames, drawn for the slot. Default: <see cref="InventorySprite"/> when that's set, else
    /// the game item's.</summary>
    public string? EquippedSprite { get; protected set; }

    /// <summary>Its picture on the character while worn, a PNG strip in the mod's Assets folder: the game item's frames
    /// side by side, each the game's size (armour's 48x40, the body's) and lined up on the body as the game's.
    /// A chest piece's three are standing, holding a two-handed weapon and resting; a helmet's, boots' or
    /// cloak's two are standing and resting. Copy the game item's to start from (its s_char_ sprite). Default:
    /// the game item's.</summary>
    public string? WornSprite { get; protected set; }
    /// <summary><see cref="WornSprite"/> for female characters (the game has its own for some armour). Default:
    /// <see cref="WornSprite"/>.</summary>
    public string? WornSpriteFemale { get; protected set; }
    /// <summary>A second worn picture drawn over the rest (a cloak's hood or collar over the armour), as
    /// <see cref="WornSprite"/>; only for items whose game item has one (an s_char_upper_ sprite).</summary>
    public string? WornUpperSprite { get; protected set; }
    public string? WornUpperSpriteFemale { get; protected set; }
    /// <summary>Its picture on the player's corpse, as the game item's (48x40, a frame). Default: the game
    /// item's.</summary>
    public string? CorpseSprite { get; protected set; }

    /// <summary>A helmet's worn picture for one of the characters ("Verren", "Jonna", "Arna", "Dirwin", "Velmir",
    /// "Hilda", "Jorgrim", "Leosthenes", "Mahir"...), as <see cref="WornSprite"/>: the game draws helmets to fit
    /// each one's head. Characters without one wear <see cref="WornSprite"/>.</summary>
    protected void SetWornSprite(string character, string file) => _wornByCharacter[character] = file;
    internal IReadOnlyDictionary<string, string> WornByCharacter => _wornByCharacter;
    private readonly Dictionary<string, string> _wornByCharacter = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether it can turn up in random loot, as the game item can (by its tier, material and tags).</summary>
    public bool InRandomLoot { get; protected set; }
    /// <summary>Whether its tooltip says which mod it's from ("Mod: Example Mod", at its bottom left).</summary>
    public bool ShowModName { get; protected set; } = true;

    /// <summary>The mod that added it (for <see cref="ModContext.Log"/>, its files...); set by
    /// <see cref="Items.Add(ModContext, ModItem)"/>.</summary>
    protected ModContext Context { get; private set; } = null!;

    /// <summary>Every one of it in the game now (inventories, chests, shops), as far as the loader has seen.</summary>
    public IEnumerable<Item> InGame => Items.Tracked.Where(item => item.Type == this);

    // ---- what happens to it: override these ----

    /// <summary>A new one was made (given, looted, bought, crafted): not one loaded from a save.</summary>
    protected internal virtual void OnCreated(Item item) { }
    /// <summary>The player put it on (or a save was loaded with it on).</summary>
    protected internal virtual void OnEquip(Item item) { }
    /// <summary>The player took it off.</summary>
    protected internal virtual void OnUnequip(Item item) { }
    /// <summary>Every turn the player has it on.</summary>
    protected internal virtual void OnEquippedTurn(Item item) { }

    // ---- the loader ----

    internal abstract bool IsArmor { get; }
    internal string ColumnsText => string.Join("|", _columns.Select(c => c.Key + "=" + c.Value));
    internal void Attach(ModContext context)
    {
        Context = context;
        Id = context.ContentId(Key);
        GameKey = context.GameKey(Key);
    }

    private protected void SetColumn(string column, string value)
    {
        Items.CheckText(value, nameof(value));
        _columns[column] = value;
    }

    private protected static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
}
