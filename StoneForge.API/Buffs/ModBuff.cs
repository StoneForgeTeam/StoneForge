namespace StoneForge;

/// <summary>A mod's buff or debuff, like the game's own effects: an icon by the unit's others, a name and
/// description on hover, a duration in turns, and stat changes while it lasts. Inherit from it: in the
/// constructor call <c>base("key", BuffKind...)</c>, set its <see cref="DisplayName"/>, <see cref="Description"/>
/// and <see cref="Icon"/>, and <see cref="Set(BuffStat, double)"/> its stats; override the <c>On...</c> methods
/// for anything else (damage each turn...). Add it with <see cref="Buffs.Add"/>, put it on a unit with
/// <see cref="Buffs.Apply"/>.</summary>
public abstract class ModBuff
{
    private readonly Dictionary<string, double> _stats = new();

    protected ModBuff(string key, BuffKind kind = BuffKind.Buff)
    {
        ModIdentity.CheckKey(key, "buff");
        Key = key;
        Kind = kind;
        DisplayName = key;
    }

    /// <summary>Its key in its mod ("shocked"): saves refer to it by this and the mod's ID, so don't change it once
    /// players have it.</summary>
    public string Key { get; }
    /// <summary>Its full ID, "yourmod:shocked" (set when it's added).</summary>
    public string Id { get; private set; } = "";
    public BuffKind Kind { get; }
    /// <summary>The name shown on hover (default: <see cref="Key"/>).</summary>
    public string DisplayName { get; protected set; }
    /// <summary>What it does, shown on hover.</summary>
    public string Description { get; protected set; } = "";
    /// <summary>Its icon: a PNG in the mod's Assets folder, 27x26 like the game's (centred on its slot).</summary>
    public string? Icon { get; protected set; }

    /// <summary>The mod that added it; set by <see cref="Buffs.Add"/>.</summary>
    protected ModContext Context { get; private set; } = null!;

    /// <summary>Changes one of the unit's stats while it lasts (added to its own: negative lowers it).</summary>
    protected void Set(BuffStat stat, double value) => _stats[stat.ToString()] = value;

    /// <summary>An animation on its unit while it lasts, looping (as the game's stun stars): one of the game's
    /// sprites - its unit-sized ones (48x40) line up as they are.</summary>
    protected void SetAura(Sprite sprite, FxOptions? options = null)
    {
        AuraSprite = (int)sprite;
        AuraOptions = options;
    }

    /// <summary>As <see cref="SetAura(Sprite, FxOptions?)"/>, with a PNG strip in the mod's Assets folder: its frames
    /// side by side, each drawn with its bottom middle at the unit's feet.</summary>
    protected void SetAura(string file, int frames, FxOptions? options = null)
    {
        AuraFile = file;
        AuraFrames = Math.Max(1, frames);
        AuraOptions = options;
    }

    internal int? AuraSprite { get; private set; }
    internal string? AuraFile { get; private set; }
    internal int AuraFrames { get; private set; } = 1;
    internal FxOptions? AuraOptions { get; private set; }

    internal IReadOnlyDictionary<string, double> Stats => _stats;
    internal void Attach(ModContext context)
    {
        Context = context;
        Id = context.ContentId(Key);
    }

    /// <summary>Every one of it on a unit now.</summary>
    public IEnumerable<Effect> Active => Buffs.ActiveOf(this);

    // ---- what happens to it: override these ----

    /// <summary>Put on a unit (<see cref="Buffs.Apply"/>) - not when a save is loaded with it on.</summary>
    protected internal virtual void OnApplied(Effect effect) { }
    /// <summary>At the start of each of its unit's turns while it lasts.</summary>
    protected internal virtual void OnTurn(Effect effect) { }
    /// <summary>It's gone: run out, removed, or its unit died.</summary>
    protected internal virtual void OnRemoved(Effect effect) { }
}
