using System.Text.Json;

namespace StoneForge.Loader;

/// <summary>Which mods are switched off (dotnet\mods.json, beside the loader - by mod ID), and every mod found this
/// run.</summary>
internal static class ModRegistry
{
    internal static readonly List<ModInfo> All = new();
    private static string ConfigPath => Path.Combine(Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!, "mods.json");
    private static HashSet<string>? _disabled;

    private sealed class Config
    {
        public List<string> Disabled { get; set; } = new();
    }

    internal static HashSet<string> Disabled
    {
        get
        {
            if (_disabled != null)
                return _disabled;
            _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(ConfigPath))
                    foreach (var name in JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath))?.Disabled ?? new())
                        _disabled.Add(name);
            }
            catch (Exception e) { Game.Log("mods.json unreadable, every mod on: " + e.Message); }
            return _disabled;
        }
    }

    // Its entry this run (the Mods window shows it).
    internal static void Update(string id, bool enabled)
    {
        int index = All.FindIndex(m => m.Id == id);
        if (index >= 0)
            All[index] = All[index] with { Enabled = enabled };
    }

    internal static void SetFault(string id, string? reason)
    {
        int index = All.FindIndex(m => m.Id == id);
        if (index >= 0) All[index] = All[index] with { RuntimeError = reason };
    }

    /// <summary>Switches a mod on or off for the next start.</summary>
    internal static void SetEnabled(string id, bool enabled)
    {
        if (enabled)
            Disabled.Remove(id);
        else
            Disabled.Add(id);
        var config = new Config { Disabled = Disabled.OrderBy(n => n).ToList() };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}
