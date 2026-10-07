namespace StoneForge;

/// <summary>A skill being cast - a mod's (<see cref="ModSkill.OnCast"/>), or any (<see cref="Skills.OnUsed"/>): who cast
/// it, at what, and whether it's a miracle (a spell's crit).</summary>
public sealed class SkillCast
{
    internal SkillCast(Instance skill, Instance caster, Instance target, bool isCrit)
    {
        Skill = GameInstance.Wrap<GameInstance>(skill);
        Caster = GameInstance.Wrap<GameInstance>(caster);
        Target = GameInstance.Wrap<GameInstance>(target);
        IsCrit = isCrit;
        if (!target.IsNone && target.Exists)
        {
            X = target.Get("x").AsReal;
            Y = target.Get("y").AsReal;
        }
        else if (!caster.IsNone && caster.Exists)
        {
            X = caster.Get("x").AsReal;
            Y = caster.Get("y").AsReal;
        }
    }

    /// <summary>The skill's instance in the game (o_skill_&lt;key&gt;).</summary>
    public GameInstance Skill { get; }
    /// <summary>Who cast it (the player).</summary>
    public GameInstance Caster { get; }
    /// <summary>What it was cast at: a unit, or the game's mark on the tile aimed at (none for a skill without a
    /// target - then it's the caster).</summary>
    public GameInstance Target { get; }
    /// <summary>Where (the target's position - for a skill without one, the caster's).</summary>
    public double X { get; }
    public double Y { get; }
    /// <summary>A miracle: a spell's critical cast.</summary>
    public bool IsCrit { get; }
}
