using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>A preset's saved state as plain data (<see cref="LocationPreset.Export"/>, <see cref="Locations.Store"/>):
/// where it is, its flags and its entities' JSON (null: none). <see cref="ToJson"/> keeps the tags' types - a room or
/// preset tag can be a number, and the game's key 3 isn't "3".</summary>
public sealed record LocationState(string Location, GmValue Room, GmValue Preset, LocationFlags Flags, string? EntitiesJson)
{
    public string ToJson() => new JsonObject
    {
        ["location"] = Location,
        ["room"] = Room.ToJsonNode(),
        ["preset"] = Preset.ToJsonNode(),
        ["flags"] = (int)Flags,
        ["entities"] = EntitiesJson,
    }.ToJsonString();

    /// <summary>A state from <see cref="ToJson"/>'s text; null if it isn't one.</summary>
    public static LocationState? FromJson(string json)
    {
        if (GmJson.Parse(json) is not JsonObject o || o["location"]?.GetValueKind() != System.Text.Json.JsonValueKind.String)
            return null;
        GmValue room = GmJson.FromNode(o["room"]), preset = GmJson.FromNode(o["preset"]);
        if (room.IsUndefined || preset.IsUndefined)
            return null;
        return new LocationState(o["location"]!.GetValue<string>(), room, preset,
            (LocationFlags)(o["flags"]?.GetValue<int>() ?? 0), o["entities"]?.GetValue<string>());
    }
}
