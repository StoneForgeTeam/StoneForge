namespace StoneForge;

/// <summary>Skill registration accessed through <see cref="ModContext.Skills"/>. Skills belong to that
/// context and use the same registry and unload behavior as <see cref="StoneForge.Skills"/>.</summary>
public sealed class ModSkills
{
    private readonly ModContext _context;
    internal ModSkills(ModContext context) => _context = context;

    /// <inheritdoc cref="StoneForge.Skills.Add"/>
    public void Add(ModSkillBase skill) => StoneForge.Skills.Add(_context, skill);
}
