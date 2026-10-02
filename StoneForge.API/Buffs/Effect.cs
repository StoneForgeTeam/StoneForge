namespace StoneForge;

/// <summary>One effect on a unit: one of a mod's buffs (<see cref="Type"/>), in the game.</summary>
public sealed class Effect
{
    internal Effect(Instance instance, ModBuff type)
    {
        Instance = instance;
        Type = type;
    }

    /// <summary>The game's instance (its o_stonemod_buff / o_stonemod_debuff).</summary>
    public Instance Instance { get; }
    public ModBuff Type { get; }
    /// <summary>The unit it's on.</summary>
    public GameInstance Target => GameInstance.Wrap<GameInstance>(Buffs.UnitOf(Instance.Get("target")));
    /// <summary>Who put it there (its target, if nobody else).</summary>
    public GameInstance Owner => GameInstance.Wrap<GameInstance>(Buffs.UnitOf(Instance.Get("owner")));
    /// <summary>Turns left.</summary>
    public int Duration
    {
        get => Instance.Get("duration").AsInt;
        set => Instance.Set("duration", Math.Max(0, value));
    }

    /// <summary>Takes it off its unit now.</summary>
    public void Remove() => Buffs.Remove(Instance);

    /// <summary>Damages its unit - straight off its health, as the game's returned damage is (the game's own
    /// scr_simple_damage). Only inside its <c>On...</c> methods.</summary>
    public void DealDamage(double amount)
    {
        if (Instance.Pointer == IntPtr.Zero)
            throw new InvalidOperationException("Effect.DealDamage works inside the buff's On... methods");
        if (amount > 0)
            Game.CallScript("scr_simple_damage", Instance, Instance.Get("target"), amount);
    }
}
