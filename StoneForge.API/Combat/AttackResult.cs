namespace StoneForge;

/// <summary>How an attack went (the game's own outcomes).</summary>
public enum AttackResult
{
    Hit,
    Crit,
    /// <summary>Blocked (some damage may still get through).</summary>
    Block,
    /// <summary>Dodged: missed.</summary>
    Dodge,
    /// <summary>Fumbled.</summary>
    Fumble,
}
