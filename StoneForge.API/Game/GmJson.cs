using System.Text.Json;
using System.Text.Json.Nodes;

namespace StoneForge;

// Game values as System.Text.Json nodes and back (GmValue.ToJsonNode / FromJsonNode): walked value by value, as DsList
// writes its JSON, rather than through the game's json_stringify - so what's written is always JSON.
internal static class GmJson
{
    // How deep arrays and structs may nest (a struct holding itself is caught before this).
    private const int MaxDepth = 256;

    public static JsonNode? ToNode(GmValue value) => ToNode(value, new HashSet<IntPtr>(), 0);

    private static JsonNode? ToNode(GmValue value, HashSet<IntPtr> path, int depth)
    {
        switch (value.Kind)
        {
            case GmKind.Array:
            case GmKind.Struct:
                GmRef reference = value.Kind == GmKind.Array ? value.AsArray! : value.AsStruct!;
                // (A method reaches C# as a struct: it has no members to write.)
                if (value.Kind == GmKind.Struct && Game.CallBuiltin("is_method", value).AsBool)
                    return null;
                if (depth >= MaxDepth)
                    throw new InvalidOperationException($"Game values nested more than {MaxDepth} deep can't be written as JSON.");
                if (!path.Add(reference.Pointer))
                    throw new InvalidOperationException($"This game {(value.Kind == GmKind.Array ? "array" : "struct")} contains itself, so it can't be written as JSON.");
                try
                {
                    return value.Kind == GmKind.Array ? Array(value.AsArray!, path, depth) : Object(value.AsStruct!, path, depth);
                }
                finally
                {
                    path.Remove(reference.Pointer);
                }
            default:
                return Scalar(value);
        }
    }

    internal static JsonArray Array(GmArray array, HashSet<IntPtr> path, int depth)
    {
        var node = new JsonArray();
        int length = array.Length;
        for (int i = 0; i < length; i++)
        {
            GmValue element = array[i];
            try { node.Add(ToNode(element, path, depth + 1)); }
            finally { Release(element); }
        }
        return node;
    }

    internal static JsonObject Object(GmStruct strukt, HashSet<IntPtr> path, int depth)
    {
        var node = new JsonObject();
        foreach (string name in strukt.Names)
        {
            GmValue member = strukt[name];
            try { node[name] = ToNode(member, path, depth + 1); }
            finally { Release(member); }
        }
        return node;
    }

    internal static JsonArray Array(GmArray array) => Array(array, new HashSet<IntPtr> { array.Pointer }, 0);
    internal static JsonObject Object(GmStruct strukt) => Object(strukt, new HashSet<IntPtr> { strukt.Pointer }, 0);

    /// <summary>A plain value: a number (NaN and infinity have no JSON: null), true/false, text; an instance as its id
    /// (what the game's functions take), or null if it has none; undefined as null.</summary>
    public static JsonNode? Scalar(GmValue value) => value.Kind switch
    {
        GmKind.Real => double.IsFinite(value.AsReal) ? JsonValue.Create(value.AsReal) : null,
        GmKind.Bool => JsonValue.Create(value.AsBool),
        GmKind.String => JsonValue.Create(value.AsString),
        GmKind.Instance => value.AsInstance.Id >= 0 ? JsonValue.Create(value.AsInstance.Id) : null,
        _ => null,
    };

    public static GmValue FromNode(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return GmValue.Undefined;
            case JsonArray array:
                return FromArray(array);
            case JsonObject obj:
                return FromObject(obj);
        }
        return node.GetValueKind() switch
        {
            JsonValueKind.Number => node.GetValue<double>(),
            JsonValueKind.String => node.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => GmValue.Undefined,
        };
    }

    internal static GmArray FromArray(JsonArray array)
    {
        var made = GmArray.Create(array.Count);
        for (int i = 0; i < array.Count; i++)
        {
            GmValue element = FromNode(array[i]);
            try { made[i] = element; }
            finally { Release(element); }
        }
        return made;
    }

    internal static GmStruct FromObject(JsonObject obj)
    {
        var made = GmStruct.Create();
        foreach (var (name, member) in obj)
        {
            GmValue value = FromNode(member);
            try { made[name] = value; }
            finally { Release(value); }
        }
        return made;
    }

    // JSON text as a node, or null if it isn't JSON.
    public static JsonNode? Parse(string json)
    {
        try { return JsonNode.Parse(json); }
        catch (JsonException) { return null; }
    }

    // (A nested array or struct C# held only to walk or place: let go of now; the game keeps it where it lives.)
    private static void Release(GmValue value)
    {
        value.AsArray?.Dispose();
        value.AsStruct?.Dispose();
    }
}
