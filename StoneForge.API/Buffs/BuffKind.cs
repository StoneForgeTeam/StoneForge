namespace StoneForge;

/// <summary>Whether a mod's effect is good or bad for whoever has it: shown as the game's magical buffs and
/// debuffs are (a debuff's duration is shortened by its target's Fortitude, as the game's own are).</summary>
public enum BuffKind
{
    Buff,
    Debuff,
}
