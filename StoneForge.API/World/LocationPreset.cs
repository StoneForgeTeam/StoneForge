using System.Text.Json.Nodes;

namespace StoneForge;

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
