using System.IO;
using System.Text.Json;

namespace StoneForge;

/// <summary>Find installed copies through Epic's local manifests, without starting its launcher.</summary>
internal static class EpicLibrary
{
    public static string? FindStoneshard(string? programData = null)
        => FindAllStoneshard(programData).FirstOrDefault();

    internal static IEnumerable<string> FindAllStoneshard(string? programData = null)
    {
        string root = Path.Combine(programData ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic");
        string manifests = Path.Combine(root, "EpicGamesLauncher", "Data", "Manifests");
        return Files(manifests).SelectMany(file => Read(file, list: false))
            .Concat(Read(Path.Combine(root, "UnrealEngineLauncher", "LauncherInstalled.dat"), list: true));
    }

    private static string[] Files(string directory)
    {
        try { return Directory.GetFiles(directory, "*.item").OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> Read(string file, bool list)
    {
        var locations = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (!list)
            {
                if (Location(document.RootElement) is string location)
                    locations.Add(location);
            }
            else if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("InstallationList", out var entries) && entries.ValueKind == JsonValueKind.Array)
                foreach (var entry in entries.EnumerateArray())
                    if (Location(entry) is string location)
                        locations.Add(location);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { }
        return locations;
    }

    private static string? Location(JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("InstallLocation", out var value)
            || value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } location
            || !Path.IsPathFullyQualified(location))
            return null;
        // Match the actual game files, not a translated display name or an opaque catalog ID.
        return File.Exists(Path.Combine(location, "StoneShard.exe")) && File.Exists(Path.Combine(location, "data.win"))
            ? Path.GetFullPath(location) : null;
    }
}
