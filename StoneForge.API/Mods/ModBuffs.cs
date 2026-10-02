namespace StoneForge;

/// <summary>Buff operations accessed through <see cref="ModContext.Buffs"/>. Registrations belong to that
/// context; applying and querying effects retain the shared semantics of <see cref="StoneForge.Buffs"/>.</summary>
public sealed class ModBuffs
{
    private readonly ModContext _context;
    internal ModBuffs(ModContext context) => _context = context;

    /// <inheritdoc cref="StoneForge.Buffs.Add"/>
    public void Add(ModBuff buff) => StoneForge.Buffs.Add(_context, buff);
    /// <inheritdoc cref="StoneForge.Buffs.Apply"/>
    public Effect? Apply(ModBuff buff, GameInstance target, int turns, GameInstance? source = null)
        => StoneForge.Buffs.Apply(buff, target, turns, source);
    /// <inheritdoc cref="StoneForge.Buffs.ApplyGame"/>
    public bool ApplyGame(string effectObject, GameInstance target, int turns, GameInstance? source = null)
        => StoneForge.Buffs.ApplyGame(effectObject, target, turns, source);
    /// <inheritdoc cref="StoneForge.Buffs.Has"/>
    public bool Has(GameInstance target, ModBuff buff) => StoneForge.Buffs.Has(target, buff);
}
