using System.Text.Json;
using System.Text.RegularExpressions;

namespace StoneForge;

/// <summary>A mod's mod.json, as read (shared by the loader's API and the patcher).</summary>
internal sealed record ManifestData(string Id, string Name, string Version, string Author, string Description, string? StoneForge);

/// <summary>Who a mod is - its mod.json - and how its content is named: content keyed "key" in the mod "examplemod"
/// is "examplemod:key" to mods, and "examplemod__key" in the game's data (objects, tables, saves). A mod ID has no
/// "__" and a key doesn't start with "_", so no two mods' content can ever share a name in the game.</summary>
internal static class ModIdentity
{
    public const string ManifestFile = "mod.json";

    // Lowercase letters and digits, single underscores between them: "examplemod", "failmelon_examplemod".
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9]*(_[a-z0-9]+)*$", RegexOptions.CultureInvariant);
    private static readonly string[] Keys = { "id", "name", "version", "author", "description", "stoneforge" };

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
            foreach (var property in root.EnumerateObject())
            {
                if (!Keys.Contains(property.Name))
                    throw new InvalidDataException($"{ManifestFile}: unknown key \"{property.Name}\" (it has {string.Join(", ", Keys)})");
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
            if (stoneForge != null && !System.Version.TryParse(stoneForge.Count(c => c == '.') == 0 ? stoneForge + ".0" : stoneForge, out _))
                throw new InvalidDataException($"{ManifestFile}: stoneforge \"{stoneForge}\" - the StoneForge version it needs, e.g. \"0.1\"");
            return new ManifestData(id, Required("name"), Required("version"), values.GetValueOrDefault("author") ?? "",
                values.GetValueOrDefault("description") ?? "", stoneForge);
        }
    }

    /// <summary>Whether this StoneForge is at least <paramref name="needed"/> ("0.1", "0.1.2").</summary>
    public static bool Satisfies(string current, string needed)
    {
        static System.Version Parse(string v) => System.Version.Parse(v.Count(c => c == '.') == 0 ? v + ".0" : v);
        return Parse(current.Split('-', '+')[0]) >= Parse(needed);
    }
}
