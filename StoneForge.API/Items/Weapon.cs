namespace StoneForge;

/// <summary>A mod's weapon (<see cref="ModItem"/>): inherit from it, and in the constructor call
/// <c>base("Name", basedOn: "a game weapon")</c> and <see cref="Set(WeaponColumn, double)"/> what differs.</summary>
public abstract class Weapon : ModItem
{
    protected Weapon(string name, string basedOn) : base(name, basedOn) { }
    internal override bool IsArmor => false;

    /// <summary>The player attacked with it in hand - after the game has worked the attack out and dealt its
    /// damage, whatever the result (hit, crit, blocked, dodged, fumbled).</summary>
    protected internal virtual void OnAttack(Item item, Attack attack) { }
    /// <summary>It struck (a hit or a crit), after <see cref="OnAttack"/>.</summary>
    protected internal virtual void OnHit(Item item, Attack attack) { }

    /// <summary>Changes one of its stats (a column of the game's weapon table): <c>Set(WeaponColumn.Slashing_Damage, 30)</c>.</summary>
    protected void Set(WeaponColumn column, double value) => SetColumn(column.ToString(), Number(value));
    /// <summary>Changes a text column (<c>Set(WeaponColumn.rarity, "Unique")</c>); "" clears it.</summary>
    protected void Set(WeaponColumn column, string value) => SetColumn(column.ToString(), value);
}
