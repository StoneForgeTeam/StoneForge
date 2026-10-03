namespace StoneForge;

/// <summary>A kind of damage (<see cref="Combat.Damage(GameInstance, DamageType, double, GameInstance?, DamageOptions?)"/>).
/// The game's are in StoneForge.GameDamageTypes - <c>DamageType.Shock</c>, <c>DamageType.Fire</c>... - each resisted by
/// its own stat, with its own effects (fire burns, frost chills). A kind of your own inherits one of them and is
/// dealt as it, or <see cref="GameDamageTypes.Pure"/> (no resistance of the game's at all), and changes what it likes:
/// <code>
/// // Shock that hits the wet twice as hard.
/// public class Lightning : Shock
/// {
///     public Lightning() { Name = "Lightning"; }
///     protected override double Modify(DamageHit hit) => IsWet(hit.Target) ? hit.Amount * 2 : hit.Amount;
/// }
/// </code></summary>
public abstract partial class DamageType
{
    // One of the game's (the generated StoneForge.GameDamageTypes): dealt as <gameName>_Damage, resisted by <resistance>.
    private protected DamageType(string gameName, string resistance)
    {
        GameName = gameName;
        Resistance = resistance;
        Name = gameName;
    }

    // Pure: past every resistance (the game's scr_pure_damage).
    private protected DamageType()
    {
        Name = "Pure";
    }

    /// <summary>Its name ("Shock" - or yours, for a kind of your own: the combat log's breakdown says it, "9 lightning",
    /// where the game would say the kind it's dealt as).</summary>
    public string Name { get; protected set; }
    /// <summary>The game's kind it's dealt as ("Shock": its Shock_Damage); null for pure damage.</summary>
    public string? GameName { get; }
    /// <summary>The stat that resists it ("Shock_Resistance"); null for pure damage.</summary>
    public string? Resistance { get; }

    /// <summary>The amount it deals of a hit, before the game's calculation (the target's protection and
    /// resistance follow): for bonuses, or a resistance of your own. Default: as asked.</summary>
    protected internal virtual double Modify(DamageHit hit) => hit.Amount;

    /// <summary>Dealt: <see cref="DamageHit.Dealt"/> is the damage the hit did in all.</summary>
    protected internal virtual void OnDealt(DamageHit hit) { }

    public override string ToString() => Name;
}

/// <summary>A hit of some damage (<see cref="DamageType.Modify"/>, <see cref="DamageType.OnDealt"/>).</summary>
public sealed class DamageHit
{
    internal DamageHit(GameInstance target, GameInstance? source, DamageType type, double amount)
    {
        Target = target;
        Source = source;
        Type = type;
        Amount = amount;
    }

    /// <summary>Who it hits.</summary>
    public GameInstance Target { get; }
    /// <summary>Who dealt it (null: nobody's).</summary>
    public GameInstance? Source { get; }
    public DamageType Type { get; }
    /// <summary>The amount of this kind: as asked in <see cref="DamageType.Modify"/>, as modified after.</summary>
    public double Amount { get; internal set; }
    /// <summary>The damage the whole hit did (after the target's protection and resistances) - 0 until it's dealt.</summary>
    public int Dealt { get; internal set; }
}

/// <summary>How damage is dealt (<see cref="Combat.Damage(GameInstance, DamageType, double, GameInstance?, DamageOptions?)"/>).</summary>
public sealed class DamageOptions
{
    /// <summary>Armour piercing, 0-100: how much of the target's protection it ignores (default 0).</summary>
    public double ArmorPiercing { get; init; }
    /// <summary>Whether the combat log says so (default: yes).</summary>
    public bool Log { get; init; } = true;
    /// <summary>What dealt it, as the combat log says (default: the source's name - with no source, the damage's).</summary>
    public string? Name { get; init; }
}
