namespace StoneForge;

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
