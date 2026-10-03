namespace StoneForge;

/// <summary>A mod's passive skill: always on once learnt - with an ability point, beside the mod's active skills on
/// its tab of the skills menu. It changes the character's stats (<see cref="Set(BuffStat, double)"/>, as the game's
/// passives do) and reacts to what happens in a fight (<see cref="OnHit"/>, <see cref="OnHitTaken"/>,
/// <see cref="OnKill"/>...):
/// <code>
/// public class KeenEye : ModPassive
/// {
///     public KeenEye() : base("keen_eye")
///     {
///         DisplayName = "Keen Eye";
///         Description = "+5% Crit Chance. Kills restore 5 energy.";
///         Icon = "keen_eye.png";
///         Set(BuffStat.CRT, 5);
///     }
///     protected override void OnKill(Attack attack) => ...;
/// }
/// </code>
/// Add it with <see cref="Skills.Add"/>. Its key must be written as a string literal in the base(...) call:
/// StoneForge's patcher reads it from the source to give the game an object for it (o_pass_skill_&lt;key&gt;, one of
/// the game's passives) at the game's next start.</summary>
public abstract class ModPassive : ModSkillBase
{
    private readonly Dictionary<string, double> _stats = new();

    protected ModPassive(string key) : base(key, "passive") { }

    /// <summary>Changes one of the character's stats while it's learnt (added to the character's own: negative lowers
    /// it), as the game's passives do - in the character sheet, and wherever the game uses the stat.</summary>
    protected void Set(BuffStat stat, double value) => _stats[stat.ToString()] = value;
    internal IReadOnlyDictionary<string, double> Stats => _stats;

    /// <summary>Whether the player has learnt it.</summary>
    public bool IsLearnt => Skills.IsLearnt(this);

    // ---- in a fight, once it's learnt: the player's weapon attacks (blows and shots) ----

    /// <summary>The player attacked (whatever came of it: <see cref="Attack.Result"/>).</summary>
    protected internal virtual void OnAttack(Attack attack) { }
    /// <summary>The player's attack hit (or crit).</summary>
    protected internal virtual void OnHit(Attack attack) { }
    /// <summary>The player's hit killed its target.</summary>
    protected internal virtual void OnKill(Attack attack) { }
    /// <summary>The player was attacked (whatever came of it).</summary>
    protected internal virtual void OnAttacked(Attack attack) { }
    /// <summary>An attack hit the player.</summary>
    protected internal virtual void OnHitTaken(Attack attack) { }
}
