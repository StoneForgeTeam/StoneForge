using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StoneForge;

// Catalogs are data only; malformed translations never stop a mod.
internal sealed class TextCatalog(Func<string, string?> read, Action<string> log)
{
    private readonly Dictionary<string, Dictionary<string, string>> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _reported = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string?> _sources = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, Dictionary<string, string>>? _previous;

    internal bool Refresh()
    {
        bool changed = false;
        foreach (string language in _loaded.Keys.ToArray())
        {
            try { if (read(language) != _sources.GetValueOrDefault(language)) changed = true; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
            { Report(language, language + ": " + e.Message); }
        }
        if (!changed) return false;
        _previous = new(_loaded, StringComparer.OrdinalIgnoreCase);
        _loaded.Clear();
        // Load English first so placeholder validation uses the refreshed fallback.
        Load(Localization.DefaultLanguage);
        foreach (string language in _previous.Keys) Load(language);
        _previous = null;
        return true;
    }

    internal string Get(string language, string key, params object?[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        CultureInfo culture;
        try { culture = CultureInfo.GetCultureInfo(language); }
        catch (CultureNotFoundException) { culture = CultureInfo.GetCultureInfo(Localization.DefaultLanguage); }
        string? english = Find(Localization.DefaultLanguage, key);
        string text = Find(culture.Name, key) ?? english ?? key;
        try { return string.Format(culture, text, arguments); }
        catch (FormatException)
        {
            Report(culture.Name + ":" + key, "Cannot format " + key + "; using English.");
            try { return string.Format(culture, english ?? key, arguments); }
            catch (FormatException) { return english ?? key; }
        }
    }

    internal string Template(string language, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Find(language, key) ?? Find(Localization.DefaultLanguage, key) ?? key;
    }

    private string? Find(string language, string key)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        while (culture.Name.Length > 0)
        {
            if (Load(culture.Name).TryGetValue(key, out var text)) return text;
            culture = culture.Parent;
        }
        return null;
    }

    private Dictionary<string, string> Load(string language)
    {
        if (_loaded.TryGetValue(language, out var existing)) return existing;
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        _loaded[language] = result;
        try
        {
            string? json = read(language);
            _sources[language] = json;
            if (json == null) return result;
            if (json.Length > 1024 * 1024) throw new InvalidDataException("Catalog exceeds 1 MB.");
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Catalog must be a JSON object.");
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name) || property.Value.ValueKind != JsonValueKind.String || result.ContainsKey(property.Name))
                    throw new InvalidDataException("Keys must be unique and values must be strings.");
                string text = property.Value.GetString()!;
                try
                {
                    CompositeFormat.Parse(text);
                    if (language != Localization.DefaultLanguage && language != "en" && Find(Localization.DefaultLanguage, property.Name) is { } english &&
                        !Placeholders(text).SetEquals(Placeholders(english)))
                        throw new FormatException("Placeholders do not match English.");
                    result.Add(property.Name, text);
                }
                catch (FormatException e) { Report(language + ":" + property.Name, language + "/" + property.Name + ": " + e.Message); }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            result.Clear();
            if (_previous?.TryGetValue(language, out var lastGood) == true)
                foreach (var entry in lastGood) result.Add(entry.Key, entry.Value);
            Report(language, language + ": " + e.Message);
        }
        return result;
    }

    private static HashSet<int> Placeholders(string text)
    {
        var result = new HashSet<int>();
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
            int start = ++i;
            while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
            result.Add(int.Parse(text[start..i], CultureInfo.InvariantCulture));
        }
        return result;
    }

    private void Report(string key, string message) { if (_reported.Add(key)) log(message); }
}
