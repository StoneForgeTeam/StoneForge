namespace StoneForge;

// Whether what the game makes now is the place loading rather than play (for the events about things that appear:
// GroundItems.OnAdded, Units.OnSpawned): the place's own things, which its loaders make (o_roomEntityLoader's user
// events, a caravan's), and the room being left and built, up to its first frame. One per registration, on the mod's
// hooks; Frame runs from the mod's frame handler.
internal sealed class PlaceLoading
{
    private static readonly string[] LoaderCodes = Enumerable.Range(10, 15).Select(e => $"gml_Object_o_roomEntityLoader_Other_{e}")
        .Append("gml_Object_o_roomEntityLoader_Create_0").Append("gml_Object_o_caravanLoader_Other_10").ToArray();

    private int _loading;
    private bool _building, _started;

    public PlaceLoading(ModContext context)
    {
        foreach (string code in LoaderCodes)
            context.OnCode(code,
                before: (_, _) =>
                {
                    _loading++;
                    return false;
                },
                after: (_, _) => _loading = Math.Max(0, _loading - 1));
        context.OnCode("gml_Object_o_controller_Other_5", before: (_, _) =>
        {
            _building = true;
            _started = false;
            return false;
        });
        context.OnCode("gml_Object_o_controller_Other_4", after: (_, _) => _started = true);
    }

    /// <summary>Whether the game is loading the place, or building the room, now.</summary>
    public bool Now => _loading > 0 || _building;

    /// <summary>A new frame: a room that's started is built.</summary>
    public void Frame()
    {
        if (_building && _started)
            _building = false;
    }
}
