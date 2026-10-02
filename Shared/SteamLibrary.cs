using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace StoneForge;

/// <summary>Finding Stoneshard in the player's Steam libraries (shared by the patcher and DataDump).</summary>
internal static class SteamLibrary
{
    /// <summary>Stoneshard's folder in any Steam library, or null.</summary>
    public static string? FindStoneshard()
    {
        foreach (string library in Libraries())
        {
            string dir = Path.Combine(library, "steamapps", "common", "Stoneshard");
            if (File.Exists(Path.Combine(dir, "StoneShard.exe")))
                return dir;
        }
        return null;
    }

    // Steam's own folder (from the registry, else its default place) and every library its libraryfolders.vdf
    // lists ("path"  "D:\\SteamLibrary").
    private static IEnumerable<string> Libraries()
    {
        var roots = new List<string>();
        if (Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steam && steam.Length > 0)
            roots.Add(steam.Replace('/', '\\'));
        roots.Add(@"C:\Program Files (x86)\Steam");
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            yield return root;
            string folders = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(folders))
                continue;
            foreach (Match match in Regex.Matches(File.ReadAllText(folders), "\"path\"\\s+\"([^\"]+)\""))
                yield return match.Groups[1].Value.Replace(@"\\", @"\");
        }
    }
}
