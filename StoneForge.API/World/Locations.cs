using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>Locations' saved state: what's dead, taken, opened, moved in each of the game's places (the game's
/// locationsRoomsDataMap). A location (by tag: its world-map cell, "12_7") has rooms - "r_global" outdoors,
/// "r_dungeon_&lt;floor&gt;" a dungeon's floors, a building's own room name - and each room has presets, each with its
/// saved entities and its <see cref="LocationFlags"/>. A location is saved as the player leaves it, so the one they're
/// standing in is written over then (<see cref="OnSaved"/>). Game thread only, and only in a game
/// (<see cref="Available"/>).</summary>
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
    public static Location? At(int x, int y) => Get(TagAt(x, y));

    /// <summary>The tag the game gives a world-map cell's location ("12_7"...: scr_locationGenerateTag), whether or not
    /// there's one there yet.</summary>
    public static string TagAt(int x, int y) => Game.CallScript("scr_locationGenerateTag", default, x, y).AsString;

    /// <summary>Whether the game has a location by this tag (scr_locationExists): its rooms have been made, or
    /// stored.</summary>
    public static bool Exists(string tag) => Game.CallScript("scr_locationExists", default, tag).AsBool;

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

    /// <summary>Runs as the game has saved the place the player is leaving (its room entity saver's user event 2): the
    /// preset it saved - its entities and flags as they are now.</summary>
    public static void OnSaved(ModContext context, Action<LocationPreset> handler)
        => context.OnCode("gml_Object_o_roomEntitySaver_Other_12", after: (saver, _) =>
        {
            if (saver.IsNone || !saver.Exists || saver.Get("locationTag") is not { Kind: GmKind.String } location)
                return;
            if (Get(location.AsString)?.Room(saver.Get("roomTag"))?.Preset(saver.Get("presetTag")) is { } preset)
                handler(preset);
        });

    internal static DsMap? All => Game.Global["locationsRoomsDataMap"].AsDsMap;
}
