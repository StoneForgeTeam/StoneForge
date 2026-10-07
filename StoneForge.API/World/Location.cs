namespace StoneForge;

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
