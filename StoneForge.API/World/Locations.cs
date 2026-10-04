using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>What of a location's saved state is spawned afresh the next time it loads (a preset's "flags", the game's
/// names): a flag that's set makes the loader ignore that kind's saved entities, spawn new ones, and clear the flag. The
/// game sets them when a location respawns - a settlement's mobs, NPCs, corpses, dropped loot, doors... - and a
/// location never saved spawns everything anyway.</summary>
[Flags]
public enum LocationFlags
{
    None = 0,
    Mobs = 1,
    Npc = 2,
    Corpses = 4,
    LootRoom = 8,
    LootDrop = 16,
    LootGrow = 32,
    StuffRoom = 64,
    StuffGrow = 128,
    ContainersRoom = 256,
    ContainersGrow = 512,
    ContainersCrime = 1024,
    Doors = 2048,
    Transitions = 4096,
    Marks = 8192,
    Triggers = 16384,
    TriggersAmbush = 32768,
    Insects = 65536,
}

/// <summary>Locations' saved state: what's dead, taken, opened, moved in each of the game's places (the game's
/// locationsRoomsDataMap). A location (by tag: its world-map cell, "12_7") has rooms - "r_global" outdoors,
/// "r_dungeon_&lt;floor&gt;" a dungeon's floors, a building's own room name - and each room has presets, each with its
/// saved entities and its <see cref="LocationFlags"/>. A location is saved as the player leaves it, so the one they're
/// standing in is written over then. Game thread only, and only in a game (<see cref="Available"/>).</summary>
public static class Locations
{
    /// <summary>Whether there are locations to read: a game loaded or begun.</summary>
    public static bool Available => All != null;

    /// <summary>Every location with saved state, by tag.</summary>
    public static IReadOnlyList<string> Tags => All is { } all ? all.Keys.Select(key => key.AsString).ToArray() : Array.Empty<string>();

    /// <summary>The location with this tag; null if it has no saved state.</summary>
    public static Location? Get(string tag) => All?.GetMap(tag) != null ? new Location(tag) : null;

    /// <summary>The location of world-map cell (<paramref name="x"/>, <paramref name="y"/>) (scr_locationGenerateTag);
    /// null if it has no saved state.</summary>
    public static Location? At(int x, int y) => Get(Game.CallScript("scr_locationGenerateTag", default, x, y).AsString);

    /// <summary>Where the player is: the current location's tag and room's tag (scr_locationGenerateTag,
    /// scr_locationRoomGenerateTag); null outside a game.</summary>
    public static (string Location, GmValue Room)? Here
    {
        get
        {
            if (!Available || Game.CallBuiltin("instance_find", (int)GameObjectId.o_player, 0).AsInstance.IsNone)
                return null;
            GmValue room = Game.CallScript("scr_locationRoomGenerateTag", default);
            return room.Kind == GmKind.String && room.AsString == "N/A"
                ? null : (Game.CallScript("scr_locationGenerateTag", default).AsString, room);
        }
    }

    /// <summary>Stores a saved state (<see cref="LocationPreset.Export"/>, perhaps another game's) where the game keeps
    /// it - its location, room and preset made if they're new - so the next visit there loads it. Its flags go too: they
    /// decide what spawns afresh. False, with nothing changed, if its entities aren't JSON the game can read back (the
    /// game would rebuild the location from scratch).</summary>
    public static bool Store(LocationState state)
    {
        if (All == null)
            throw new InvalidOperationException("There are no locations: no game is loaded or begun (Locations.Available).");
        if (state.EntitiesJson != null)
        {
            if (DsMap.FromJson(state.EntitiesJson) is not { } check)
                return false;
            check.Destroy();
        }
        if (Game.CallScript("scr_locationRoomPresetGet", default, state.Location, state.Room, state.Preset, true).AsDsMap is not { } preset)
            return false;
        preset["flags"] = (int)state.Flags;
        preset["entitiesDataMapString"] = state.EntitiesJson ?? "N/A";
        return true;
    }

    internal static DsMap? All => Game.Global["locationsRoomsDataMap"].AsDsMap;
}

/// <summary>A location's saved state (<see cref="Locations.Get"/>): its rooms.</summary>
public readonly record struct Location(string Tag)
{
    private DsMap? Map => Locations.All?.GetMap(Tag);

    /// <summary>Its rooms' tags ("r_global", "r_dungeon_2", a building's room name).</summary>
    public IReadOnlyList<GmValue> Rooms => Map?.Keys ?? Array.Empty<GmValue>();

    /// <summary>A room; null if it has none by that tag.</summary>
    public LocationRoom? Room(GmValue tag) => Map?.GetMap(tag) != null ? new LocationRoom(Tag, tag) : null;

    /// <summary>Sets flags on every preset of every room (scr_locationFlagSet): those kinds spawn afresh on the next
    /// visit.</summary>
    public void SetFlags(LocationFlags flags)
    {
        foreach (var flag in Single(flags))
            Game.CallScript("scr_locationFlagSet", default, Tag, (int)flag);
    }

    /// <summary>Forgets all its saved state (scr_locationDelete): it's built afresh on the next visit.</summary>
    public void Delete() => Game.CallScript("scr_locationDelete", default, Tag);

    // (The game sets flags one at a time.)
    internal static IEnumerable<LocationFlags> Single(LocationFlags flags)
        => Enum.GetValues<LocationFlags>().Where(flag => flag != LocationFlags.None && flags.HasFlag(flag));
}

/// <summary>One room of a location (<see cref="Location.Room"/>): its presets.</summary>
public readonly record struct LocationRoom(string Location, GmValue Tag)
{
    private DsMap? Map => Locations.All?.GetMap(Location)?.GetMap(Tag);

    /// <summary>Its presets' tags.</summary>
    public IReadOnlyList<GmValue> Presets => Map?.Keys ?? Array.Empty<GmValue>();

    /// <summary>A preset; null if it has none by that tag.</summary>
    public LocationPreset? Preset(GmValue tag) => Map?.GetMap(tag) is { } map ? new LocationPreset(Location, Tag, tag, map) : null;

    /// <summary>Whether any of its presets has saved entities (scr_locationRoomHasSaveData).</summary>
    public bool HasSaveData => Game.CallScript("scr_locationRoomHasSaveData", default, Location, Tag).AsBool;

    /// <summary>Sets flags on every preset (scr_locationRoomFlagSet): those kinds spawn afresh on the next visit.</summary>
    public void SetFlags(LocationFlags flags)
    {
        foreach (var flag in StoneForge.Location.Single(flags))
            Game.CallScript("scr_locationRoomFlagSet", default, Location, Tag, (int)flag);
    }

    /// <summary>Forgets the room's saved state (scr_locationRoomDelete).</summary>
    public void Delete() => Game.CallScript("scr_locationRoomDelete", default, Location, Tag);
}

/// <summary>A preset of a room (<see cref="LocationRoom.Preset"/>): one saved state - its entities (what's dead,
/// taken, opened, moved: mobs, NPCs, corpses, loot, containers, doors...) and its <see cref="LocationFlags"/>.</summary>
public sealed class LocationPreset
{
    private readonly DsMap _map;

    internal LocationPreset(string location, GmValue room, GmValue tag, DsMap map)
    {
        Location = location;
        Room = room;
        Tag = tag;
        _map = map;
    }

    public string Location { get; }
    public GmValue Room { get; }
    public GmValue Tag { get; }

    /// <summary>What spawns afresh on the next visit.</summary>
    public LocationFlags Flags => (LocationFlags)_map.Get("flags", 0).AsInt;

    /// <summary>Sets flags (scr_locationRoomPresetFlagSet, one at a time, as the game does).</summary>
    public void SetFlags(LocationFlags flags)
    {
        foreach (var flag in StoneForge.Location.Single(flags))
            Game.CallScript("scr_locationRoomPresetFlagSet", default, Location, Room, Tag, (int)flag);
    }

    /// <summary>Clears flags (scr_locationRoomPresetFlagUnset).</summary>
    public void UnsetFlags(LocationFlags flags)
    {
        foreach (var flag in StoneForge.Location.Single(flags))
            Game.CallScript("scr_locationRoomPresetFlagUnset", default, Location, Room, Tag, (int)flag);
    }

    /// <summary>Clears every flag (scr_locationRoomPresetFlagsReset).</summary>
    public void ResetFlags() => Game.CallScript("scr_locationRoomPresetFlagsReset", default, Location, Room, Tag);

    /// <summary>Whether it has saved entities (none yet: it's spawned afresh).</summary>
    public bool HasSaveData => EntitiesJson != null;

    /// <summary>Its saved entities as the game keeps them, JSON; null if it has none.</summary>
    public string? EntitiesJson => _map.Get("entitiesDataMapString", "N/A") is { Kind: GmKind.String } json && json.AsString != "N/A" ? json.AsString : null;

    /// <summary>Its saved entities as a new map - mobsMap, npcMap, lootMap, containersMap... each with its "static" and
    /// "dynamic" entities - or null if it has none. A copy: <see cref="DsMap.Destroy"/> it when done, and
    /// <see cref="SetEntities"/> to save changes.</summary>
    public DsMap? Entities => EntitiesJson is { } json ? DsMap.FromJson(json) : null;

    /// <summary>Saves <paramref name="entities"/> as its entities (scr_locationRoomPresetSetEntitiesMap). The map stays
    /// the caller's.</summary>
    public void SetEntities(DsMap entities)
        => Game.CallScript("scr_locationRoomPresetSetEntitiesMap", default, Location, Room, Tag, entities);

    /// <summary>Forgets its saved state (scr_locationRoomPresetDelete).</summary>
    public void Delete() => Game.CallScript("scr_locationRoomPresetDelete", default, Location, Room, Tag);

    /// <summary>Its saved state as plain data, to keep or send on (<see cref="Locations.Store"/> puts it back).</summary>
    public LocationState Export() => new(Location, Room, Tag, Flags, EntitiesJson);
}

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
