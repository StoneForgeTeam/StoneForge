using StoneForge.Gml;

namespace StoneForge;

// The mods' GML: each mod folder with GML\**\*.gml, known by the folder's name (its bindings: <Folder>.Gml).
internal sealed class GmlCatalog
{
    // By the mod folder's full path.
    internal readonly Dictionary<string, GmlProject> Projects = new(StringComparer.OrdinalIgnoreCase);

    internal static GmlCatalog Read(string mods)
    {
        var catalog = new GmlCatalog();
        if (!Directory.Exists(mods)) return catalog;
        foreach (var folder in Directory.GetDirectories(mods).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var project = ReadFolder(folder);
            if (project != null) catalog.Projects.Add(Path.GetFullPath(folder), project);
        }
        return catalog;
    }

    // One mod folder's GML (null: it has none).
    internal static GmlProject? ReadFolder(string folder)
    {
        var scripts = Files(folder);
        if (scripts.Length == 0) return null;
        try
        {
            return GmlProject.Parse(Path.GetFileName(Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)),
                scripts.Select(f => new KeyValuePair<string, string>(Path.GetRelativePath(folder, f).Replace('\\', '/'), File.ReadAllText(f))));
        }
        catch (Exception e) { throw new InvalidOperationException(Path.GetFileName(folder) + ": " + e.Message, e); }
    }

    internal static string[] Files(string folder)
    {
        string gml = Path.Combine(folder, "GML");
        return Directory.Exists(gml) ? Directory.GetFiles(gml, "*.gml", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal).ToArray() : Array.Empty<string>();
    }

    internal string Fingerprint => GmlProject.Hash(string.Join("\n", Projects.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).Select(p => p.Name + ":" + p.Fingerprint)));
}
