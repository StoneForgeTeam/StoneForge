namespace StoneForge;

/// <summary>A mod's own values in something the game keeps - an item's data (<see cref="InventoryItem.ModData"/>, a
/// ground item's, a mod item's), the save data (<see cref="SaveData.ModData"/>), an instance's variables
/// (<see cref="Instance.ModData"/>) - under keys only that mod uses: two mods' "kills" are two values, and neither can
/// overwrite the other's or the game's. Keys are the mod's own, given without its id; in the game they're
/// "&lt;mod id&gt;:&lt;key&gt;" in a map, "&lt;mod id&gt;__&lt;key&gt;" as a variable (<see cref="GameKey"/>). Kept and
/// saved with what holds them, as the game keeps that.</summary>
/// <example><code>
/// var mine = blade.ModData(context);
/// mine["kills"] = mine["kills"].AsInt + 1;
/// </code></example>
public sealed class ModData
{
    private readonly string _prefix;
    private readonly Func<string, GmValue> _get;
    private readonly Action<string, GmValue> _set;
    private readonly Func<IEnumerable<string>> _names;
    private readonly Action<string>? _remove;

    private ModData(string prefix, Func<string, GmValue> get, Action<string, GmValue> set, Func<IEnumerable<string>> names, Action<string>? remove)
    {
        _prefix = prefix;
        _get = get;
        _set = set;
        _names = names;
        _remove = remove;
    }

    /// <summary>One of the mod's values; undefined if it hasn't set it (or what holds it is gone).</summary>
    public GmValue this[string key]
    {
        get => _get(GameKey(key));
        set => _set(GameKey(key), value);
    }

    /// <summary>Whether the mod has set this value.</summary>
    public bool Has(string key) => !this[key].IsUndefined;

    /// <summary>Takes one of the mod's values away (a variable is left undefined: the game can't remove one).</summary>
    public void Remove(string key)
    {
        if (_remove != null)
            _remove(GameKey(key));
        else
            _set(GameKey(key), GmValue.Undefined);
    }

    /// <summary>The keys of every value the mod has set here, as the mod gave them.</summary>
    public IReadOnlyList<string> Keys => _names().Where(name => name.StartsWith(_prefix, StringComparison.Ordinal))
        .Select(name => name.Substring(_prefix.Length)).Where(key => key.Length > 0 && Has(key)).ToArray();

    /// <summary>A key as the game holds it ("examplemod:kills" in a map, "examplemod__kills" as a variable).</summary>
    public string GameKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("A key is needed.", nameof(key));
        return _prefix + key;
    }

    // In a map (an item's data, the save data's): "<mod id>:<key>".
    internal static ModData InMap(ModContext context, Func<DsMap?> map)
        => new(context.Id + ":",
            key => map() is { Exists: true } m ? m[key] : GmValue.Undefined,
            (key, value) =>
            {
                if (map() is { Exists: true } m)
                    m[key] = value;
            },
            () => map() is { Exists: true } m ? m.Keys.Where(k => k.Kind == GmKind.String).Select(k => k.AsString) : Array.Empty<string>(),
            key =>
            {
                if (map() is { Exists: true } m)
                    m.Remove(key);
            });

    // As an instance's variables: "<mod id>__<key>" (a variable's name: a key of letters, digits and _).
    internal static ModData OnInstance(ModContext context, Instance instance)
        => new(context.Id + "__",
            key => instance.Exists ? instance.Get(Variable(key)) : GmValue.Undefined,
            (key, value) =>
            {
                if (instance.Exists)
                    instance.Set(Variable(key), value);
            },
            () => instance.Exists && Game.CallBuiltin("variable_instance_get_names", instance).AsArray is { } names
                ? Read(names) : Array.Empty<string>(),
            null);

    private static string Variable(string name)
        => name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') ? name
            : throw new ArgumentException($"\"{name}\": an instance's value's key is letters, digits and _ (it's a variable's name).", nameof(name));

    private static string[] Read(GmArray names)
    {
        using (names)
        {
            var read = new string[names.Length];
            for (int i = 0; i < read.Length; i++)
                read[i] = names[i].AsString ?? "";
            return read;
        }
    }
}
