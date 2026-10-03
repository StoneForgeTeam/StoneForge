using System.Text.Json;

namespace StoneForge.Loader;

/// <summary>Which mods are switched off, and which trusted mods (full access) the player has allowed (dotnet\mods.json,
/// beside the loader - by mod ID), and every mod found this run.</summary>
internal static class ModRegistry
{
    internal static readonly List<ModInfo> All = new();
    private static string ConfigPath => Path.Combine(Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!, "mods.json");
    private static HashSet<string>? _disabled;
    private static HashSet<string>? _allowed;

    private sealed class Config
    {
        public List<string> Disabled { get; set; } = new();
        public List<string> Allowed { get; set; } = new();
    }

    private static void Read()
    {
        _disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(ConfigPath) && JsonSerializer.Deserialize<Config>(File.ReadAllText(ConfigPath)) is { } config)
            {
                _disabled.UnionWith(config.Disabled ?? new());
                _allowed.UnionWith(config.Allowed ?? new());
            }
        }
        catch (Exception e) { Game.Log("mods.json unreadable, every mod on (trusted ones not allowed): " + e.Message); }
    }

    /// <summary>The trusted mods (full access) the player has allowed to run.</summary>
    internal static HashSet<string> Allowed
    {
        get
        {
            if (_allowed == null)
                Read();
            return _allowed!;
        }
    }

    /// <summary>Whether a mod may run: switched on, and - if it's trusted - allowed.</summary>
    internal static bool MayRun(string id, bool trusted) => !Disabled.Contains(id) && (!trusted || Allowed.Contains(id));

    internal static HashSet<string> Disabled
    {
        get
        {
            if (_disabled == null)
                Read();
            return _disabled!;
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
        Save();
    }

    /// <summary>Allows a trusted mod to run (or takes that back), for this start and the next.</summary>
    internal static void SetAllowed(string id, bool allowed)
    {
        if (allowed)
            Allowed.Add(id);
        else
            Allowed.Remove(id);
        Save();
    }

    private static void Save()
    {
        var config = new Config { Disabled = Disabled.OrderBy(n => n).ToList(), Allowed = Allowed.OrderBy(n => n).ToList() };
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
    }
}
