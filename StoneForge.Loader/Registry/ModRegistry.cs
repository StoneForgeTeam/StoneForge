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

    /// <summary>Goes up each time a mod's entry or its being switched on changes (the Mods window, open, shows it again).</summary>
    internal static int Changes { get; private set; }

    // Its entry this run (the Mods window shows it).
    internal static void Update(string id, bool enabled)
    {
        Changes++;
        int index = All.FindIndex(m => m.Id == id);
        if (index >= 0)
            All[index] = All[index] with { Enabled = enabled };
    }

    // Hook conflicts found this session (Hooks.Conflicted), each on both mods' pages.
    private static readonly List<HookConflict> HookConflicts = new();

    internal static void AddConflict(HookConflict conflict) => HookConflicts.Add(conflict);

    /// <summary>The hook conflicts a mod is in, by the other mod (its name): each call, and whose result the game got.</summary>
    internal static List<(string With, List<string> Calls)> ConflictsOf(string id)
        => HookConflicts.Where(c => c.Earlier == id || c.Winner == id)
            .GroupBy(c => c.Earlier == id ? c.Winner : c.Earlier)
            .Select(g => (NameOf(g.Key), g.Select(c => c.IsScript ? $"{c.Name} - {NameOf(c.Winner)}'s result is used" : $"{c.Name} - skipped for both")
                .Distinct().ToList()))
            .ToList();

    /// <summary>Where a mod might conflict (Hooks.Overlaps, as the hooks are now), by each other mod (its name): the calls
    /// both hook before they run, at the same order.</summary>
    internal static List<(string With, List<string> Calls)> OverlapsOf(string id)
        => Hooks.Overlaps().Where(o => o.Mods.Contains(id))
            .SelectMany(o => o.Mods.Where(other => other != id).Select(other => (Other: other, Call: $"{o.Name} ({HookOrder.Name(o.Order)})")))
            .GroupBy(pair => pair.Other)
            .Select(g => (NameOf(g.Key), g.Select(pair => pair.Call).Distinct().ToList()))
            .ToList();

    private static string NameOf(string id) => All.FirstOrDefault(m => m.Id == id)?.Name ?? id;

    internal static void SetFault(string id, string? reason)
    {
        Changes++;
        int index = All.FindIndex(m => m.Id == id);
        if (index >= 0) All[index] = All[index] with { RuntimeError = reason };
    }

    /// <summary>Switches a mod on or off for the next start.</summary>
    internal static void SetEnabled(string id, bool enabled)
    {
        Changes++;
        if (enabled)
            Disabled.Remove(id);
        else
            Disabled.Add(id);
        Save();
    }

    /// <summary>Allows a trusted mod to run (or takes that back), for this start and the next.</summary>
    internal static void SetAllowed(string id, bool allowed)
    {
        Changes++;
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
