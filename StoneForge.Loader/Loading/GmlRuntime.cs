namespace StoneForge.Loader;

internal static class GmlRuntime
{
    // Snapshot once. Changing GML or the prepared-state file in a running game cannot authorize new code.
    // (Mod folder name -> the fingerprint of the GML the patcher compiled into data.win.)
    private static readonly Lazy<Dictionary<string, string>> Prepared = new(() =>
    {
        string dotnet = Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!;
        string path = Path.Combine(dotnet, "stoneforge-gml.txt");
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;
        var lines = File.ReadAllLines(path);
        var data = new FileInfo(Path.Combine(dotnet, "..", "data.win"));
        if (!data.Exists || lines.Length == 0 || lines[0] != $"{data.Length}|{data.LastWriteTimeUtc.Ticks}") return result;
        foreach (string line in lines.Skip(1))
        {
            var parts = line.Split('|');
            if (parts.Length == 2) result[parts[0]] = parts[1];
        }
        return result;
    });

    internal static void Snapshot() => _ = Prepared.Value;

    // A mod folder's GML made callable through its bindings - if it's the GML the game was prepared with.
    internal static void Activate(string folder, string owner)
    {
        var project = GmlCatalog.ReadFolder(folder);
        if (project == null) return;
        if (!Prepared.Value.TryGetValue(project.Name, out var fingerprint) || fingerprint != project.Fingerprint)
            throw new InvalidOperationException($"{project.Name}'s GML changed or wasn't prepared; restart the game.");
        GmlScripts.Activate(project.Name, owner, project.Fingerprint, project.Functions.Select(f => project.InternalName(f.Name)));
    }

    internal static bool ContainsGml(string folder) => GmlCatalog.Files(folder).Length > 0;
}
