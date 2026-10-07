namespace StoneForge;

/// <summary>Two mods' before hooks both replacing the same call - a script (its result: <see cref="Winner"/>'s, the one
/// that ran later) or a code entry (the game's code skipped for both). Found as it happens, once per pair and name.</summary>
public sealed record HookConflict(string Name, bool IsScript, string Earlier, string Winner)
{
    /// <summary>What happened, for the log and the Mods window.</summary>
    public string Describe(Func<string, string> modName)
        => IsScript
            ? $"{modName(Earlier)} and {modName(Winner)} both replace {Name}: the game's own code is skipped, and "
              + $"{modName(Winner)}'s result is used (its hook runs later - HookOrder sets which runs last)"
            : $"{modName(Earlier)} and {modName(Winner)} both skip {Name}: the game's own code doesn't run for either";
}
