using System.IO;
using System.Security;
using Microsoft.Win32;

namespace StoneForge;

/// <summary>Find GOG installations, including offline-installer copies registered without Galaxy.</summary>
internal static class GogLibrary
{
    public static string? FindStoneshard()
        => FindAllStoneshard().FirstOrDefault();

    internal static IEnumerable<string> FindAllStoneshard()
        => FindAllStoneshard(new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser }
            .SelectMany(hive => new[] { RegistryView.Registry32, RegistryView.Registry64 }
                .SelectMany(view => Installations(hive, view))));

    internal static string? FindStoneshard(IEnumerable<string?> candidates)
        => FindAllStoneshard(candidates).FirstOrDefault();

    internal static IEnumerable<string> FindAllStoneshard(IEnumerable<string?> candidates)
    {
        var paths = new List<string>();
        foreach (string? path in candidates)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
                    && File.Exists(Path.Combine(path, "StoneShard.exe")) && File.Exists(Path.Combine(path, "data.win")))
                    paths.Add(Path.GetFullPath(path));
            }
            catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException or SecurityException) { }
        }
        return paths;
    }

    private static IEnumerable<string?> Installations(RegistryHive hive, RegistryView view)
    {
        var paths = new List<string?>();
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view);
            using var games = root.OpenSubKey(@"SOFTWARE\GOG.com\Games");
            if (games == null)
                return paths;
            foreach (string name in games.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var game = games.OpenSubKey(name);
                    paths.Add(game?.GetValue("path") as string);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SecurityException) { }
        return paths;
    }
}
