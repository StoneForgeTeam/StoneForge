using System.Text.Json;

namespace StoneForge;

/// <summary>A GameMaker ds_map, the game's own: it keeps almost everything in them - the save data, characters,
/// contracts, locations. A map is a number the game hands out (<see cref="GmValue.AsDsMap"/>); this reads and changes it
/// in place. A map or list held in one of its keys is either <i>marked</i> as nested - the map owns it, destroys it with
/// itself and writes it into JSON as an object or array (<see cref="AddMap"/>, <see cref="AddList"/>, <see cref="IsMap"/>)
/// - or just a number.
/// <para>The game holds on to its maps - the journal to a contract's, the diary to its targets - so a map is updated
/// with <see cref="AssignFrom"/>, which copies another's contents into it, nested ones too, rather than replaced.</para></summary>
public readonly struct DsMap : IEquatable<DsMap>
{
    // (GameMaker's ds_type_map.)
    internal const int Type = 1;

    public DsMap(int id) => Id = id;

    /// <summary>Its number, as the game knows it.</summary>
    public int Id { get; }

    /// <summary>Whether it's (still) a map.</summary>
    public bool Exists => Game.CallBuiltin("ds_exists", Id, Type).AsBool;

    /// <summary>A key's value (undefined if it has none). Setting one replaces it - a nested map or list there is
    /// destroyed first, as the map owned it.</summary>
    public GmValue this[GmValue key]
    {
        get => Game.CallBuiltin("ds_map_find_value", Id, key);
        set
        {
            if (IsMap(key) || IsList(key))
                Remove(key);
            Game.CallBuiltin("ds_map_set", Id, key, value);
        }
    }

    /// <summary>Whether it has the key (ds_map_exists).</summary>
    public bool Has(GmValue key) => Game.CallBuiltin("ds_map_exists", Id, key).AsBool;

    /// <summary>A key's value, or <paramref name="fallback"/> if it has none (as the game's ds_map_find_value_ext).</summary>
    public GmValue Get(GmValue key, GmValue fallback) => Has(key) ? this[key] : fallback;

    /// <summary>Takes a key out - a nested map or list there is destroyed with it (the game's ds_map_delete leaves it,
    /// owned by nothing).</summary>
    public void Remove(GmValue key)
    {
        int kind = IsMap(key) ? Type : IsList(key) ? DsList.Type : 0;
        GmValue value = this[key];
        Game.CallBuiltin("ds_map_delete", Id, key);
        DestroyNested(kind, value);
    }

    /// <summary>How many keys it has.</summary>
    public int Count => Game.CallBuiltin("ds_map_size", Id).AsInt;

    /// <summary>Its keys, as they are (a number stays a number), in the map's order.</summary>
    public GmValue[] Keys
    {
        get
        {
            int count = Count;
            var keys = new List<GmValue>(count);
            GmValue key = Game.CallBuiltin("ds_map_find_first", Id);
            for (int i = 0; i < count && !key.IsUndefined; i++)
            {
                keys.Add(key);
                key = Game.CallBuiltin("ds_map_find_next", Id, key);
            }
            return keys.ToArray();
        }
    }

    /// <summary>Whether a key holds a nested map (ds_map_is_map: marked as one).</summary>
    public bool IsMap(GmValue key) => Has(key) && Game.CallBuiltin("ds_map_is_map", Id, key).AsBool;

    /// <summary>Whether a key holds a nested list (ds_map_is_list: marked as one).</summary>
    public bool IsList(GmValue key) => Has(key) && Game.CallBuiltin("ds_map_is_list", Id, key).AsBool;

    /// <summary>The nested map in a key, or null if it holds none.</summary>
    public DsMap? GetMap(GmValue key) => IsMap(key) ? new DsMap(this[key].AsInt) : null;

    /// <summary>The nested list in a key, or null if it holds none.</summary>
    public DsList? GetList(GmValue key) => IsList(key) ? new DsList(this[key].AsInt) : null;

    /// <summary>Puts <paramref name="map"/> in a key as a nested map (ds_map_add_map): this map owns it from now on. What the
    /// key held is removed first.</summary>
    public void AddMap(GmValue key, DsMap map)
    {
        Remove(key);
        Game.CallBuiltin("ds_map_add_map", Id, key, map.Id);
    }

    /// <summary>Puts <paramref name="list"/> in a key as a nested list (ds_map_add_list): this map owns it from now on.
    /// What the key held is removed first.</summary>
    public void AddList(GmValue key, DsList list)
    {
        Remove(key);
        Game.CallBuiltin("ds_map_add_list", Id, key, list.Id);
    }

    /// <summary>Makes this map a copy of <paramref name="source"/>, in place - everything holding this map, or a map or
    /// list nested in it, sees the new contents. Keys <paramref name="source"/> lacks are removed; its nested maps and
    /// lists are copied into the ones this map has in the same keys (at any depth), and made anew where it has none.
    /// What's nested in <paramref name="source"/> stays its own.</summary>
    public void AssignFrom(DsMap source)
    {
        if (source.Id == Id)
            return;
        foreach (GmValue key in Keys)
            if (!source.Has(key))
                Remove(key);
        foreach (GmValue key in source.Keys)
        {
            if (source.GetMap(key) is { } map)
            {
                if (GetMap(key) is { } mine)
                    mine.AssignFrom(map);
                else
                {
                    var made = Create();
                    made.AssignFrom(map);
                    AddMap(key, made);
                }
            }
            else if (source.GetList(key) is { } list)
            {
                if (GetList(key) is { } mine)
                    mine.AssignFrom(list);
                else
                {
                    var made = DsList.Create();
                    made.AssignFrom(list);
                    AddList(key, made);
                }
            }
            else
                this[key] = source[key];
        }
    }

    /// <summary>Its JSON, as the game writes it (json_encode): nested maps and lists as objects and arrays, anything
    /// else as a value.</summary>
    public string ToJson() => Game.CallBuiltin("json_encode", Id).AsString;

    /// <summary>A new map from JSON text, as the game reads it (json_decode): objects and arrays in it nested in it, marked;
    /// an array at the top in its "default" key. Null if the text isn't JSON. The caller owns it: <see cref="Destroy"/> it,
    /// or nest it in another.</summary>
    public static DsMap? FromJson(string json)
    {
        // (The game makes a map of anything at all: text that isn't JSON is turned away first.)
        try
        {
            using var _ = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
        return From(Game.CallBuiltin("json_decode", json));
    }

    /// <summary>The map numbered <paramref name="value"/>; null if it isn't a number, or no map has it.</summary>
    public static DsMap? From(GmValue value)
        => value.Kind == GmKind.Real && value.AsReal >= 0 && Game.CallBuiltin("ds_exists", value, Type).AsBool ? new DsMap(value.AsInt) : null;

    /// <summary>A new, empty map. The caller owns it: <see cref="Destroy"/> it, or nest it in another.</summary>
    public static DsMap Create() => new(Game.CallBuiltin("ds_map_create").AsInt);

    /// <summary>Destroys it - and the maps and lists nested in it.</summary>
    public void Destroy() => Game.CallBuiltin("ds_map_destroy", Id);

    // Destroys a map or list (kind: Type / DsList.Type; 0 neither) taken out of a map or list, if it's still there. (The
    // game's ds_map_delete and ds_list_delete leave a nested one as it is.)
    internal static void DestroyNested(int kind, GmValue value)
    {
        if (kind == Type && Game.CallBuiltin("ds_exists", value, Type).AsBool)
            Game.CallBuiltin("ds_map_destroy", value);
        else if (kind == DsList.Type && Game.CallBuiltin("ds_exists", value, DsList.Type).AsBool)
            Game.CallBuiltin("ds_list_destroy", value);
    }

    public static implicit operator GmValue(DsMap map) => map.Id;

    public bool Equals(DsMap other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is DsMap other && Equals(other);
    public override int GetHashCode() => Id;
    public static bool operator ==(DsMap a, DsMap b) => a.Id == b.Id;
    public static bool operator !=(DsMap a, DsMap b) => a.Id != b.Id;
    public override string ToString() => $"ds_map {Id}";
}
