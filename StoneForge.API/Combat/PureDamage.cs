namespace StoneForge.GameDamageTypes
{
    /// <summary>Pure damage: straight off the target's health, past its protection and every resistance (the game's
    /// scr_pure_damage - what its own "takes N damage" effects deal). Inherit it for a kind of damage the game doesn't
    /// have, resisted as you decide in <see cref="DamageType.Modify"/>.</summary>
    public class Pure : global::StoneForge.DamageType
    {
        public Pure() { }
    }
}

namespace StoneForge
{
    public abstract partial class DamageType
    {
        /// <summary>Pure damage: past protection and every resistance.</summary>
        public static readonly GameDamageTypes.Pure Pure = new();
    }
}
