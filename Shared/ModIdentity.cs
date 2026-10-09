using System.Text.Json;
using System.Text.RegularExpressions;

namespace StoneForge;

/// <summary>A mod's mod.json, as read (shared by the loader's API and the patcher). Requires and After: the mods it needs,
/// and the ones it loads after if they're there, by ID (null: none given).</summary>
internal sealed record ManifestData(string Id, string Name, string Version, string Author, string Description, string? StoneForge, bool Trusted = false,
    IReadOnlyList<string>? Requires = null, IReadOnlyList<string>? After = null, IReadOnlyList<string>? Contributors = null);

/// <summary>Who a mod is - its mod.json - and how its content is named: content keyed "key" in the mod "examplemod"
/// is "examplemod:key" to mods, and "examplemod__key" in the game's data (objects, tables, saves). A mod ID has no
/// "__" and a key doesn't start with "_", so no two mods' content can ever share a name in the game.</summary>
internal static class ModIdentity
{
    public const string ManifestFile = "mod.json";

    // Lowercase letters and digits, single underscores between them: "examplemod", "failmelon_examplemod".
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly string[] Keys = { "id", "name", "version", "author", "description", "stoneforge", "trusted", "requires", "after", "contributors", "Contributors" };

    public static bool IsValidId(string id) => id.Length <= 64 && IdPattern.IsMatch(id);

    /// <summary>"examplemod:key": how mods refer to a mod's content.</summary>
    public static string FullId(string modId, string key) => modId + ":" + key;

    /// <summary>"examplemod__key": the content's name in the game's data.</summary>
    public static string GameKey(string modId, string key) => modId + "__" + key;

    /// <summary>A name as the game knows it: a mod's full ID ("examplemod:key") as its game key ("examplemod__key");
    /// anything else (the game's own names) as it is.</summary>
    public static string ToGameKey(string name)
    {
        int colon = name.IndexOf(':');
        return colon > 0 && IsValidId(name.Substring(0, colon)) ? GameKey(name.Substring(0, colon), name.Substring(colon + 1)) : name;
    }

    /// <summary>A content key's own rules on top of its kind's: not empty, no leading "_" or ":".</summary>
    public static void CheckKey(string key, string what)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException($"A {what} needs a key", nameof(key));
        if (key.StartsWith('_') || key.Contains(':'))
            throw new ArgumentException($"A {what}'s key can't start with _ or contain : (\"{key}\") - the mod's ID is added in front of it", nameof(key));
    }

    /// <summary>The folder's mod.json; throws (with what's wrong) if it's missing or invalid.</summary>
    public static ManifestData ReadManifest(string folder)
    {
        string path = Path.Combine(folder, ManifestFile);
        if (!File.Exists(path))
            throw new InvalidDataException($"no {ManifestFile} - a mod needs one with its id, name and version");
        return ParseManifest(File.ReadAllText(path));
    }

    public static ManifestData ParseManifest(string json)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException e) { throw new InvalidDataException($"{ManifestFile} isn't valid JSON: {e.Message}"); }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException($"{ManifestFile} must be a JSON object");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            bool trusted = false;
            IReadOnlyList<string>? contributors = null;
            var lists = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!Keys.Contains(property.Name))
                    throw new InvalidDataException($"{ManifestFile}: unknown key \"{property.Name}\" (it has {string.Join(", ", Keys)})");
                if (property.Name is "contributors" or "Contributors")
                {
                    if (contributors != null) throw new InvalidDataException($"{ManifestFile}: contributors is listed more than once");
                    contributors = ContributorIds(property);
                    continue;
                }
                // (Full access - its own DLLs, no sandbox: see ModManifest.Trusted.)
                if (property.Name == "trusted")
                {
                    trusted = property.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        _ => throw new InvalidDataException($"{ManifestFile}: \"trusted\" must be true or false"),
                    };
                    continue;
                }
                // (Other mods, by ID: ["othermod", ...].)
                if (property.Name is "requires" or "after")
                {
                    lists[property.Name] = ModIds(property);
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException($"{ManifestFile}: \"{property.Name}\" must be a string");
                values[property.Name] = property.Value.GetString()!.Trim();
            }
            string Required(string key) => values.TryGetValue(key, out var value) && value.Length > 0
                ? value : throw new InvalidDataException($"{ManifestFile}: \"{key}\" is missing");
            string id = Required("id");
            if (!IsValidId(id))
                throw new InvalidDataException($"{ManifestFile}: id \"{id}\" - lowercase letters and digits, single underscores between them (\"examplemod\", \"failmelon_examplemod\")");
            string? stoneForge = values.GetValueOrDefault("stoneforge");
            if (stoneForge != null && !IsLatest(stoneForge)
                && !System.Version.TryParse(stoneForge.Count(c => c == '.') == 0 ? stoneForge + ".0" : stoneForge, out _))
                throw new InvalidDataException($"{ManifestFile}: stoneforge \"{stoneForge}\" - the StoneForge version it needs, e.g. \"0.1\" (or \"latest\", for a mod in development)");
            foreach (var (key, ids) in lists)
                if (ids.Contains(id))
                    throw new InvalidDataException($"{ManifestFile}: \"{key}\" names the mod itself (\"{id}\")");
            return new ManifestData(id, Required("name"), Required("version"), values.GetValueOrDefault("author") ?? "",
                values.GetValueOrDefault("description") ?? "", stoneForge, trusted, lists.GetValueOrDefault("requires"), lists.GetValueOrDefault("after"), contributors);
        }
    }

    private static IReadOnlyList<string> ContributorIds(JsonProperty property)
    {
        if (property.Value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{ManifestFile}: contributors must be an array of Steam IDs as strings");
        var ids = new List<string>();
        foreach (var item in property.Value.EnumerateArray())
        {
            string? id = item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : null;
            if (!TryContributorAccount(id, out _))
                throw new InvalidDataException($"{ManifestFile}: contributors entries must be positive Steam account IDs or individual SteamID64 values, as strings");
            string normalized = ulong.Parse(id!, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!ids.Contains(normalized)) ids.Add(normalized);
        }
        return ids.AsReadOnly();
    }

    // Public-universe individual SteamID64 values have the 32-bit account ID in their low bits.
    internal static bool TryContributorAccount(string? id, out uint account)
    {
        account = 0;
        if (string.IsNullOrEmpty(id) || !id.All(char.IsAsciiDigit) ||
            !ulong.TryParse(id, out ulong value)) return false;
        const ulong individual = 76561197960265728;
        if (value > individual && value <= individual + uint.MaxValue) value -= individual;
        if (value is 0 or > uint.MaxValue) return false;
        account = (uint)value;
        return true;
    }

    // "requires" / "after": a list of mod IDs (each once).
    private static IReadOnlyList<string> ModIds(JsonProperty property)
    {
        string example = $"\"{property.Name}\": [\"othermod\"]";
        if (property.Value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{ManifestFile}: \"{property.Name}\" must be a list of mod IDs ({example})");
        var ids = new List<string>();
        foreach (var item in property.Value.EnumerateArray())
        {
            string? id = item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : null;
            if (id == null || !IsValidId(id))
                throw new InvalidDataException($"{ManifestFile}: \"{property.Name}\" has {(id == null ? item.GetRawText() : $"\"{id}\"")} - "
                    + $"a mod's ID: lowercase letters and digits, single underscores between them ({example})");
            if (!ids.Contains(id))
                ids.Add(id);
        }
        return ids;
    }

    /// <summary>The "stoneforge" of a mod in development: built against StoneForge as it is now - any StoneForge loads it
    /// (its release names the StoneForge it was built against).</summary>
    public const string Latest = "latest";

    /// <summary>Whether a "stoneforge" requirement is <see cref="Latest"/>.</summary>
    public static bool IsLatest(string needed) => string.Equals(needed.Trim(), Latest, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this StoneForge is at least <paramref name="needed"/> ("0.1", "0.1.2"); any is, for
    /// <see cref="Latest"/>.</summary>
    public static bool Satisfies(string current, string needed)
    {
        if (IsLatest(needed))
            return true;
        static System.Version Parse(string v) => System.Version.Parse(v.Count(c => c == '.') == 0 ? v + ".0" : v);
        return Parse(current.Split('-', '+')[0]) >= Parse(needed);
    }
}
