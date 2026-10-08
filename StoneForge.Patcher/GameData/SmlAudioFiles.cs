using System.Text.Json;
using System.Text.RegularExpressions;

namespace StoneForge.Patcher;

// Audio groups, fonts and shader exports are rebuilt from preserved originals. The helper sees staging files only.
internal sealed class SmlAudioFiles : IDisposable
{
    private sealed record Entry(bool Original, string Written);
    private readonly GameFolder _game;
    private readonly string _work;
    private readonly Dictionary<string, Entry> _previous;
    private readonly Dictionary<string, string> _clean = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _patched = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _observed = new(StringComparer.OrdinalIgnoreCase);
    private bool _retainWork;
    private string State => Path.Combine(_game.Dotnet, "msl-audio.json");
    private string BaseDirectory => Path.Combine(_game.Dotnet, "msl-audio-base");
    private static bool IsGroup(string name) => Regex.IsMatch(name, @"^audiogroup[0-9]+\.dat$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static bool IsResource(string name)
    {
        string[] parts = name.Split('/');
        return ((parts.Length == 2 && parts[0] == "fonts") || (parts.Length == 3 && parts[0] == "shaders")) &&
            parts.All(p => !string.IsNullOrWhiteSpace(p) && p is not ("." or "..") && p.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }
    private string Target(string name) => IsGroup(name) ? Path.Combine(_game.Dir, name) : Path.Combine(_game.UserData, name);
    private static IEnumerable<string> Groups(string directory) => Directory.Exists(directory)
        ? Directory.EnumerateFiles(directory).Where(p => IsGroup(Path.GetFileName(p))) : Array.Empty<string>();

    private static Dictionary<string, Entry> ReadState(GameFolder game)
    {
        string state = Path.Combine(game.Dotnet, "msl-audio.json");
        var result = File.Exists(state) ? JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(state))
            ?? throw new InvalidDataException("Invalid MSL audio state.") : new();
        if (result.Any(p => (!IsGroup(p.Key) && !IsResource(p.Key)) || p.Value == null ||
            p.Value.Written?.Length != 64 || !p.Value.Written.All(Uri.IsHexDigit))) throw new InvalidDataException("Invalid MSL resource state.");
        try { return result.ToDictionary(p => p.Key, p => p.Value, StringComparer.OrdinalIgnoreCase); }
        catch (ArgumentException error) { throw new InvalidDataException("Duplicate MSL resource state entries.", error); }
    }

    internal static string Fingerprint(GameFolder game, bool enhanced)
    {
        string state = Path.Combine(game.Dotnet, "msl-audio.json");
        if (!enhanced && !File.Exists(state)) return "";
        var managed = ReadState(game);
        var files = Groups(game.Dir).Concat(managed.Keys.Select(n => IsGroup(n) ? Path.Combine(game.Dir, n) : Path.Combine(game.UserData, n)).Where(File.Exists))
            .Concat(managed.Keys.Select(n => Path.Combine(game.Dotnet, "msl-audio-base", n)).Where(File.Exists))
            .Concat(File.Exists(state) ? new[] { state } : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.Ordinal);
        return string.Join("|", files.Select(p => Path.GetRelativePath(game.Dir, p) + ":" + SmlRuntimeSelection.Hash(p)));
    }

    internal SmlAudioFiles(GameFolder game, bool enhanced = false)
    {
        _game = game;
        _previous = ReadState(game);
        _work = Path.Combine(game.Dotnet, "msl-work", "audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_work, "clean"));
        if (!enhanced && _previous.Count == 0) return;
        try
        {
            foreach (string name in Groups(game.Dir).Select(Path.GetFileName).Cast<string>().Concat(_previous.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string live = Target(name);
                _observed[name] = File.Exists(live) ? SmlRuntimeSelection.Hash(live) : null;
                if (!File.Exists(live)) continue; // An external update/removal becomes the new base.
                string source = live;
                if (_previous.TryGetValue(name, out var entry) && SmlRuntimeSelection.Hash(live) == entry.Written)
                {
                    if (!entry.Original) continue;
                    source = Path.Combine(BaseDirectory, name);
                    if (!File.Exists(source)) throw new FileNotFoundException("Preserved MSL audio group missing. Restore clean game audio before rebuilding.", source);
                }
                string clean = Path.Combine(_work, "clean", name);
                Directory.CreateDirectory(Path.GetDirectoryName(clean)!);
                File.Copy(source, clean);
                _clean[name] = clean;
            }
        }
        catch { Dispose(); throw; }
    }

    internal void StageTo(string directory)
    {
        foreach (var pair in _clean.Where(p => IsGroup(p.Key))) File.Copy(pair.Value, Path.Combine(directory, pair.Key));
    }

    internal void Collect(string directory)
    {
        Directory.CreateDirectory(Path.Combine(_work, "patched"));
        foreach (string source in Groups(directory))
        {
            string name = Path.GetFileName(source);
            if (_clean.TryGetValue(name, out var clean) && SmlRuntimeSelection.Hash(clean) == SmlRuntimeSelection.Hash(source)) continue;
            // Check external group output with StoneForge's library before accepting it.
            using (var input = File.OpenRead(source))
            using (var data = UndertaleModLib.UndertaleIO.Read(input, (_, _) => { }, _ => { })) { }
            string target = Path.Combine(_work, "patched", name);
            File.Copy(source, target);
            _patched[name] = target;
        }
        string resources = Path.Combine(directory, "resources");
        if (Directory.Exists(resources))
            foreach (string source in Directory.EnumerateFiles(resources, "*", SearchOption.AllDirectories))
            {
                string name = Path.GetRelativePath(resources, source).Replace('\\', '/');
                if (!IsResource(name)) throw new InvalidDataException("Invalid staged Enhanced resource: " + name);
                if (!_observed.ContainsKey(name)) _observed[name] = File.Exists(Target(name)) ? SmlRuntimeSelection.Hash(Target(name)) : null;
                if (!_clean.ContainsKey(name) && !_previous.ContainsKey(name) && File.Exists(Target(name)))
                {
                    string clean = Path.Combine(_work, "clean", name);
                    Directory.CreateDirectory(Path.GetDirectoryName(clean)!);
                    File.Copy(Target(name), clean);
                    _clean[name] = clean;
                }
                string target = Path.Combine(_work, "patched", name);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
                _patched[name] = target;
            }
    }

    internal void Commit(string? data = null)
    {
        var next = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        var changes = new List<(string Target, string? Source)>();
        foreach (string name in _previous.Keys.Concat(_patched.Keys).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? observed = _observed.GetValueOrDefault(name);
            string? current = File.Exists(Target(name)) ? SmlRuntimeSelection.Hash(Target(name)) : null;
            if (observed != current) throw new IOException("MSL resource changed during preparation: " + name);
            _clean.TryGetValue(name, out string? original);
            if (_patched.TryGetValue(name, out var patched))
            {
                next[name] = new(original != null, SmlRuntimeSelection.Hash(patched));
                changes.Add((Path.Combine(BaseDirectory, name), original));
                changes.Add((Target(name), patched));
            }
            else changes.Add((Target(name), original));
        }
        if (_previous.Count > 0 || next.Count > 0)
        {
            string state = Path.Combine(_work, "state.json");
            File.WriteAllText(state, JsonSerializer.Serialize(next));
            changes.Add((State, next.Count > 0 ? state : null));
        }
        if (data != null) changes.Add((_game.Data, data));
        if (changes.Count == 1 && data != null)
        {
            File.Move(data, _game.Data, true);
            return;
        }
        // Save every destination before the first replacement. A failed write rolls back data and groups together.
        var backups = new List<(string Target, string? Source)>();
        for (int i = 0; i < changes.Count; i++)
        {
            string target = changes[i].Target;
            string? backup = null;
            if (File.Exists(target))
            {
                backup = Path.Combine(_work, "rollback-" + i);
                File.Copy(target, backup);
            }
            backups.Add((target, backup));
        }
        File.WriteAllText(Path.Combine(_work, "recovery.json"), JsonSerializer.Serialize(backups.Select(p => new { p.Target, p.Source })));
        int applied = 0;
        try
        {
            foreach (var change in changes) { Replace(change.Target, change.Source); applied++; }
        }
        catch (Exception failure)
        {
            try { foreach (var backup in backups.Take(applied).Reverse()) Replace(backup.Target, backup.Source); }
            catch (Exception rollback)
            {
                _retainWork = true;
                throw new AggregateException("MSL resource rollback failed. Recovery snapshots were kept in " + _work, failure, rollback);
            }
            throw;
        }
    }

    private static void Replace(string target, string? source)
    {
        if (source == null) { if (File.Exists(target)) File.Delete(target); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.Copy(source, temp); File.Move(temp, target, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public void Dispose() { if (!_retainWork && Directory.Exists(_work)) Directory.Delete(_work, true); }
}
