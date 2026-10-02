namespace StoneForge;

/// <summary>Runtime gateway for generated GML bindings (a mod's <c>Gml</c> class). GML runs directly in the game,
/// outside the C# mod restrictions. Use the generated class instead of calling this yourself.</summary>
public static class GmlScripts
{
    // By mod folder name; its owner is the mod (its ID) loaded from that folder.
    private sealed record Folder(string Fingerprint, HashSet<string> Functions, HashSet<string> Owners);
    private static readonly Dictionary<string, Folder> Active = new(StringComparer.OrdinalIgnoreCase);

    internal static bool IsActive(string mod) => Active.TryGetValue(mod, out var folder) && folder.Owners.Any(o => !Hooks.IsSuspended(o));

    internal static void Activate(string mod, string owner, string fingerprint, IEnumerable<string> functions)
    {
        if (Active.TryGetValue(mod, out var folder))
        {
            if (folder.Fingerprint != fingerprint) throw new InvalidOperationException($"{mod}'s GML changed; restart the game.");
            folder.Owners.Add(owner);
            return;
        }
        Active.Add(mod, new Folder(fingerprint, new HashSet<string>(functions, StringComparer.Ordinal), new HashSet<string> { owner }));
    }

    internal static void RemoveMod(string owner)
    {
        foreach (var (mod, folder) in Active.ToArray())
            if (folder.Owners.Remove(owner) && folder.Owners.Count == 0)
                Active.Remove(mod);
    }

    /// <summary>Called by generated bindings. Refuses a mod that's off or paused, unknown functions and bindings
    /// that don't match the GML compiled at startup. Calls run on the game thread with global self.</summary>
    public static GmValue Call(string mod, string fingerprint, string function, params GmValue[] args)
    {
        Game.CheckRunning("GML " + mod);
        if (!IsActive(mod)) throw new InvalidOperationException($"{mod} is switched off, not loaded or paused - its GML can't be called.");
        var folder = Active[mod];
        if (folder.Fingerprint != fingerprint) throw new InvalidOperationException($"{mod}'s GML changed; restart the game.");
        if (!folder.Functions.Contains(function)) throw new InvalidOperationException($"{mod} has no GML function {function}.");
        return Game.CallScript(function, default, args);
    }
}
