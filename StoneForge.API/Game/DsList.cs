using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>A GameMaker ds_list, the game's own - a map's nested list of contracts, a unit's list of effects. A list is a
/// number the game hands out (<see cref="GmValue.AsDsList"/>); this reads and changes it in place. An element that's a
/// map or list is either <i>marked</i> as nested - the list owns it, destroys it with itself and writes it into JSON as an
/// object or array (<see cref="AddMap"/>, <see cref="AddList"/>, <see cref="IsMap"/>) - or just a number.
/// <para>As with <see cref="DsMap"/>, the game holds on to its lists: one is updated with <see cref="AssignFrom"/>,
/// rather than replaced.</para></summary>
public readonly struct DsList : IEquatable<DsList>
{
    // (GameMaker's ds_type_list.)
    internal const int Type = 2;

    public DsList(int id) => Id = id;

    /// <summary>Its number, as the game knows it.</summary>
    public int Id { get; }

    /// <summary>Whether it's (still) a list.</summary>
    public bool Exists => Game.CallBuiltin("ds_exists", Id, Type).AsBool;

    /// <summary>How many elements it has.</summary>
    public int Count => Game.CallBuiltin("ds_list_size", Id).AsInt;

    /// <summary>An element (undefined past the end). Setting one replaces it - a nested map or list there is destroyed
    /// first, as the list owned it.</summary>
    public GmValue this[int index]
    {
        get => Game.CallBuiltin("ds_list_find_value", Id, index);
        set
        {
            // (A nested one's mark goes with its slot: the slot goes, and the value goes in in its place.)
            if (IsMap(index) || IsList(index))
            {
                DestroyAt(index);
                Put(index, value, 0);
            }
            else
                Game.CallBuiltin("ds_list_replace", Id, index, value);
        }
    }

    /// <summary>Adds a value at the end.</summary>
    public void Add(GmValue value) => Game.CallBuiltin("ds_list_add", Id, value);

    /// <summary>Takes an element out; the ones after it move up. A nested map or list there is destroyed.</summary>
    public void RemoveAt(int index)
    {
        if (index < 0 || index >= Count)
            return;
        DestroyAt(index);
    }

    /// <summary>Empties it - the maps and lists nested in it destroyed.</summary>
    public void Clear()
    {
        for (int i = Count - 1; i >= 0; i--)
            DestroyAt(i);
    }

    /// <summary>Whether an element is a nested map (ds_list_is_map: marked as one).</summary>
    public bool IsMap(int index) => index >= 0 && index < Count && Game.CallBuiltin("ds_list_is_map", Id, index).AsBool;

    /// <summary>Whether an element is a nested list (ds_list_is_list: marked as one).</summary>
    public bool IsList(int index) => index >= 0 && index < Count && Game.CallBuiltin("ds_list_is_list", Id, index).AsBool;

    /// <summary>The nested map at an index, or null if there's none.</summary>
    public DsMap? GetMap(int index) => IsMap(index) ? new DsMap(this[index].AsInt) : null;

    /// <summary>The nested list at an index, or null if there's none.</summary>
    public DsList? GetList(int index) => IsList(index) ? new DsList(this[index].AsInt) : null;

    /// <summary>Adds <paramref name="map"/> at the end as a nested map (ds_list_mark_as_map): this list owns it from now on.</summary>
    public void AddMap(DsMap map) => Put(Count, map.Id, DsMap.Type);

    /// <summary>Adds <paramref name="list"/> at the end as a nested list (ds_list_mark_as_list): this list owns it from now on.</summary>
    public void AddList(DsList list) => Put(Count, list.Id, Type);

    /// <summary>Makes this list a copy of <paramref name="source"/>, in place - everything holding this list, or a map or
    /// list nested in it, sees the new contents. Element by element: nested maps and lists are copied into the ones this
    /// list has at the same index (at any depth), and made anew where it has none; it's cut to the source's length.
    /// What's nested in <paramref name="source"/> stays its own.</summary>
    public void AssignFrom(DsList source)
    {
        if (source.Id == Id)
            return;
        int count = source.Count;
        for (int i = 0; i < count; i++)
        {
            int mine = Count;
            int kind = KindAt(i, mine), theirs = source.KindAt(i, count);
            // (Past its end, or a slot whose kind changes - nested to plain, a map to a list - and whose mark can't be
            // changed in place: the copy goes in anew.)
            if (i >= mine || kind != theirs && (kind != 0 || theirs != 0))
            {
                if (i < mine)
                    DestroyAt(i);
                PutCopy(i, source, i, theirs);
                continue;
            }
            if (theirs == DsMap.Type)
                new DsMap(this[i].AsInt).AssignFrom(new DsMap(source[i].AsInt));
            else if (theirs == Type)
                new DsList(this[i].AsInt).AssignFrom(new DsList(source[i].AsInt));
            else
                Game.CallBuiltin("ds_list_replace", Id, i, source[i]);
        }
        for (int i = Count - 1; i >= count; i--)
            DestroyAt(i);
    }

    /// <summary>Its JSON: nested maps and lists as objects and arrays, anything else as a value (as the game writes a map's
    /// with json_encode).</summary>
    public string ToJson() => ToJsonNode().ToJsonString();

    /// <summary>A new list from a JSON array (objects and arrays in it nested in it, marked), or null if the text isn't
    /// one. The caller owns it: <see cref="Destroy"/> it, or nest it in another.</summary>
    public static DsList? FromJson(string json)
    {
        // (The game reads JSON into a map: an array at the top in its "default" key. The list is copied out of it.)
        if (DsMap.FromJson(json) is not { } read)
            return null;
        try
        {
            if (read.GetList("default") is not { } list)
                return null;
            var made = Create();
            made.AssignFrom(list);
            return made;
        }
        finally
        {
            read.Destroy();
        }
    }

    /// <summary>The list numbered <paramref name="value"/>; null if it isn't a number, or no list has it.</summary>
    public static DsList? From(GmValue value)
        => value.Kind == GmKind.Real && value.AsReal >= 0 && Game.CallBuiltin("ds_exists", value, Type).AsBool ? new DsList(value.AsInt) : null;

    /// <summary>A new, empty list. The caller owns it: <see cref="Destroy"/> it, or nest it in another.</summary>
    public static DsList Create() => new(Game.CallBuiltin("ds_list_create").AsInt);

    /// <summary>Destroys it - and the maps and lists nested in it.</summary>
    public void Destroy() => Game.CallBuiltin("ds_list_destroy", Id);

    // An element's kind: 0 a value, DsMap.Type a nested map, Type a nested list, -1 past the end.
    private int KindAt(int index, int count)
        => index >= count ? -1 : Game.CallBuiltin("ds_list_is_map", Id, index).AsBool ? DsMap.Type
            : Game.CallBuiltin("ds_list_is_list", Id, index).AsBool ? Type : 0;

    // Puts a value in at index (the ones from it on moving down; at Count, at the end), marked as a nested map or list
    // (mark: DsMap.Type / Type; 0 none).
    private void Put(int index, GmValue value, int mark)
    {
        if (index >= Count)
            Game.CallBuiltin("ds_list_add", Id, value);
        else
            Game.CallBuiltin("ds_list_insert", Id, index, value);
        if (mark == DsMap.Type)
            Game.CallBuiltin("ds_list_mark_as_map", Id, index);
        else if (mark == Type)
            Game.CallBuiltin("ds_list_mark_as_list", Id, index);
    }

    // Puts a copy of source's element in at index: a nested one made anew.
    private void PutCopy(int index, DsList source, int at, int kind)
    {
        if (kind == DsMap.Type)
        {
            var made = DsMap.Create();
            made.AssignFrom(new DsMap(source[at].AsInt));
            Put(index, made.Id, kind);
        }
        else if (kind == Type)
        {
            var made = Create();
            made.AssignFrom(new DsList(source[at].AsInt));
            Put(index, made.Id, kind);
        }
        else
            Put(index, source[at], 0);
    }

    // Deletes an element, destroying it if it's nested. (Whether ds_list_delete destroys a marked one itself isn't
    // something to count on: it's destroyed after, if it's still there. A mark goes with its element.)
    private void DestroyAt(int index)
    {
        int kind = KindAt(index, Count);
        GmValue value = this[index];
        Game.CallBuiltin("ds_list_delete", Id, index);
        if (kind == DsMap.Type && Game.CallBuiltin("ds_exists", value, DsMap.Type).AsBool)
            Game.CallBuiltin("ds_map_destroy", value);
        else if (kind == Type && Game.CallBuiltin("ds_exists", value, Type).AsBool)
            Game.CallBuiltin("ds_list_destroy", value);
    }

    private JsonArray ToJsonNode()
    {
        var array = new JsonArray();
        int count = Count;
        for (int i = 0; i < count; i++)
        {
            GmValue value = this[i];
            array.Add(KindAt(i, count) switch
            {
                DsMap.Type => JsonNode.Parse(new DsMap(value.AsInt).ToJson()),
                Type => new DsList(value.AsInt).ToJsonNode(),
                _ => value.Kind switch
                {
                    GmKind.Real => JsonValue.Create(value.AsReal),
                    GmKind.Bool => JsonValue.Create(value.AsBool),
                    GmKind.Undefined => null,
                    _ => JsonValue.Create(value.AsString),
                },
            });
        }
        return array;
    }

    public static implicit operator GmValue(DsList list) => list.Id;

    public bool Equals(DsList other) => Id == other.Id;
    public override bool Equals(object? obj) => obj is DsList other && Equals(other);
    public override int GetHashCode() => Id;
    public static bool operator ==(DsList a, DsList b) => a.Id == b.Id;
    public static bool operator !=(DsList a, DsList b) => a.Id != b.Id;
    public override string ToString() => $"ds_list {Id}";
}
