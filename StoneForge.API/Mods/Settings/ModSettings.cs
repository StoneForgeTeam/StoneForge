using System.Text;
using System.Text.Json;

namespace StoneForge;

/// <summary>A mod's settings (<see cref="ModContext.Settings"/>): declared once, in Load - each saved for it
/// (in Stoneshard's data folder, StoneForge\Settings\&lt;mod&gt;.json, so it survives the mod being updated) and
/// shown on its page in the Mods window for the player to change:
/// <code>
/// var sparks = context.Settings.Toggle("sparks", "Sparks on crits", true, "Whether the blade's crits spark.");
/// var chance = context.Settings.Slider("shockChance", "Shock chance (%)", 33, min: 0, max: 100, step: 1);
/// if (sparks.Value) ...                // whenever it's needed
/// chance.Changed += value => ...;      // or as it's changed
/// </code>
/// A saved value that no longer fits (a different type, out of range, an option gone) gives way to the
/// default.</summary>
public sealed class ModSettings
{
    // Every loaded mod's settings, by its name (the Mods window shows them).
    internal static readonly Dictionary<string, ModSettings> ByMod = new();

    private readonly List<ModSetting> _all = new();
    private readonly string _path;
    // What the file holds, as loaded (kept as it is for keys not declared this run).
    private Dictionary<string, JsonElement>? _saved;
    private bool _dirty;

    internal ModSettings(string mod)
    {
        Mod = mod;
        string name = string.Concat(mod.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        _path = Path.Combine(ModFiles.DataFolder, "StoneForge", "Settings", name + ".json");
        ByMod[mod] = this;
    }

    internal string Mod { get; }
    public IReadOnlyList<ModSetting> All => _all;

    /// <summary>On or off: a checkbox.</summary>
    public ToggleSetting Toggle(string key, string label, bool defaultValue = false, string? tooltip = null)
        => Declare(key, () => new ToggleSetting(this, key, label, defaultValue, tooltip));

    /// <summary>A number from <paramref name="min"/> to <paramref name="max"/>, in steps of
    /// <paramref name="step"/> (0: any): a slider.</summary>
    public SliderSetting Slider(string key, string label, double defaultValue, double min = 0, double max = 1, double step = 0, string? tooltip = null)
        => Declare(key, () => new SliderSetting(this, key, label, defaultValue, min, max, step, tooltip));

    /// <summary>One of <paramref name="options"/>: a dropdown. Its value is the chosen one's index.</summary>
    public ChoiceSetting Choice(string key, string label, IReadOnlyList<string> options, int defaultIndex = 0, string? tooltip = null)
        => Declare(key, () => new ChoiceSetting(this, key, label, options.ToArray(), defaultIndex, tooltip));

    /// <summary>Some text: a text box.</summary>
    public TextSetting Text(string key, string label, string defaultValue = "", int maxLength = 40, string? tooltip = null)
        => Declare(key, () => new TextSetting(this, key, label, defaultValue, maxLength, tooltip));

    /// <summary>Every setting back to its default.</summary>
    public void ResetAll()
    {
        foreach (var setting in _all)
            setting.Reset();
    }

    // A setting declared: the one already declared by that key (if it's the same kind), or a new one with its
    // saved value.
    private T Declare<T>(string key, Func<T> make) where T : ModSetting
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("A setting needs a key", nameof(key));
        var existing = _all.FirstOrDefault(s => s.Key == key);
        if (existing != null)
            return existing as T ?? throw new ArgumentException($"There's already a setting \"{key}\" of another kind");
        var setting = make();
        if (Saved().TryGetValue(key, out var saved))
        {
            try { setting.Load(saved); }
            catch (Exception e) { Game.Log($"[{Mod}] setting {key}: its saved value is unreadable ({e.Message}) - its default"); }
        }
        _all.Add(setting);
        return setting;
    }

    private Dictionary<string, JsonElement> Saved()
    {
        if (_saved != null)
            return _saved;
        _saved = new Dictionary<string, JsonElement>();
        try
        {
            if (File.Exists(_path))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(_path));
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                    foreach (var property in document.RootElement.EnumerateObject())
                        _saved[property.Name] = property.Value.Clone();
            }
        }
        catch (Exception e) { Game.Log($"[{Mod}] settings file unreadable ({e.Message}): defaults"); }
        return _saved;
    }

    // A setting changed: saved at the end of the frame (a slider dragged saves once a frame at most).
    internal void MarkChanged() => _dirty = true;

    // ---- the loader ----

    // Each frame: what's changed, saved.
    internal static void SaveChanged()
    {
        foreach (var settings in ByMod.Values)
            if (settings._dirty)
                settings.Save();
    }

    // A mod switched off: saved, and forgotten (its settings come back with it, from the file).
    internal static void RemoveMod(string mod)
    {
        if (!ByMod.TryGetValue(mod, out var settings))
            return;
        if (settings._dirty)
            settings.Save();
        ByMod.Remove(mod);
    }

    private void Save()
    {
        _dirty = false;
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (var setting in _all)
                {
                    writer.WritePropertyName(setting.Key);
                    setting.Write(writer);
                }
                // (Settings saved before but not declared this run - another version of the mod's - kept.)
                foreach (var (key, value) in Saved())
                {
                    if (_all.Any(s => s.Key == key))
                        continue;
                    writer.WritePropertyName(key);
                    value.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, Encoding.UTF8.GetString(stream.ToArray()));
        }
        catch (Exception e) { Game.Log($"[{Mod}] settings couldn't be saved: {e.Message}"); }
    }
}
