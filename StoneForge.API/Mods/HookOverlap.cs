namespace StoneForge;

/// <summary>Mods with before hooks on the same script or code entry at the same <see cref="HookOrder"/> - which runs first
/// is only their load order. Not a conflict yet: one only if two of them replace the same call (<see cref="HookConflict"/>),
/// which hooks that only watch, or replace only sometimes, may never do.</summary>
public sealed record HookOverlap(string Name, bool IsScript, int Order, IReadOnlyList<string> Mods)
{
    /// <summary>What it means, for the log and the Mods window.</summary>
    public string Describe(Func<string, string> modName)
    {
        string mods = string.Join(" and ", Mods.Select(modName)), all = Mods.Count == 2 ? "both" : "all";
        return IsScript
            ? $"{mods} {all} hook {Name} before it runs ({HookOrder.Name(Order)}): if more than one replaces it, the one loaded last's result is used"
            : $"{mods} {all} hook {Name} before it runs ({HookOrder.Name(Order)}): if more than one skips it, the game's code doesn't run for any";
    }
}
