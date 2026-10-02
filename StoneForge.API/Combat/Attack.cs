namespace StoneForge;

/// <summary>An attack that has just happened - a melee blow or a shot - as an item sees it
/// (<see cref="Weapon.OnAttack"/>, <see cref="Armor.OnAttacked"/>...). Its damage is already dealt.</summary>
public sealed class Attack
{
    internal Attack(Instance attacker, Instance target, AttackResult result, double damage, bool ranged, bool byPlayer, bool onPlayer)
    {
        Attacker = GameInstance.Wrap<GameInstance>(attacker);
        Target = GameInstance.Wrap<GameInstance>(target);
        Result = result;
        Damage = damage;
        IsRanged = ranged;
        ByPlayer = byPlayer;
        OnPlayer = onPlayer;
    }

    /// <summary>Who attacked (the player, for a weapon's).</summary>
    public GameInstance Attacker { get; }
    /// <summary>Who was attacked (the player, for armour's).</summary>
    public GameInstance Target { get; }
    public AttackResult Result { get; }
    /// <summary>Whether it struck: a hit or a crit.</summary>
    public bool IsHit => Result is AttackResult.Hit or AttackResult.Crit;
    /// <summary>The damage it dealt (after armour, blocking...).</summary>
    public double Damage { get; }
    /// <summary>A shot or a throw, not a melee blow.</summary>
    public bool IsRanged { get; }
    public bool ByPlayer { get; }
    public bool OnPlayer { get; }
    /// <summary>Whether the target has no health left.</summary>
    public bool Killed => Target.Instance.Get("HP").AsReal <= 0;

    /// <summary>Deals the target some more damage now, from the attacker - straight off its health, as the
    /// game's returned damage is (no armour, no roll; the game's own scr_simple_damage).</summary>
    public void DealExtraDamage(double amount)
    {
        if (amount > 0)
            Game.CallScript("scr_simple_damage", Attacker.Instance, Target.Instance, amount);
    }
}
