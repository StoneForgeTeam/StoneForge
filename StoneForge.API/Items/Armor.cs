namespace StoneForge;

/// <summary>A mod's armour - helmets, chests, shields, rings... (<see cref="ModItem"/>): inherit from it, and in
/// the constructor call <c>base("Name", basedOn: "a game armour")</c> and <see cref="Set(ArmorColumn, double)"/>
/// what differs.</summary>
public abstract class Armor : ModItem
{
    protected Armor(string name, string basedOn) : base(name, basedOn) { }
    internal override bool IsArmor => true;

    /// <summary>The player was attacked wearing it - after the game has worked the attack out and dealt its
    /// damage, whatever the result (hit, crit, blocked, dodged, fumbled).</summary>
    protected internal virtual void OnAttacked(Item item, Attack attack) { }
    /// <summary>The attack struck the player (a hit or a crit), after <see cref="OnAttacked"/>.</summary>
    protected internal virtual void OnHitTaken(Item item, Attack attack) { }

    protected void Set(ArmorColumn column, double value) => SetColumn(column.ToString(), Number(value));
    protected void Set(ArmorColumn column, string value) => SetColumn(column.ToString(), value);
}
