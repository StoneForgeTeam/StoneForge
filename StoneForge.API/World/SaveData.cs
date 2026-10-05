using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>The save data of the game being played (the game's global.saveDataMap): everything a save writes, in
/// sections - the character's (<see cref="CharacterSections"/>: who they are, their stats, skills, inventory, scrolls and
/// the fog they've cleared) and the world's (the rest: the world map, its locations, quests, contracts, time,
/// weather...). It's the live data: the game keeps changing it, and writes it to disk as it saves. A mod may keep its own
/// values in it (<see cref="ModMap"/>): they're saved and loaded with it. <see cref="OnLoaded"/> and
/// <see cref="OnSaving"/> run as a save is read and as one's about to be written. Game thread only, and only in a game
/// (<see cref="Available"/>).</summary>
public static class SaveData
{
    /// <summary>The character's sections, by the game's names: what makes them who they are, rather than the world
    /// they're in.</summary>
    public static readonly IReadOnlyList<string> CharacterSections = new[]
    {
        "characterDataMap", "characterStatsDataMap", "skillsDataMap", "inventoryDataList", "scrollsDataList", "locationsFogDataMap",
    };

    /// <summary>Runs as a save has been read (scr_slotLoad): its data is the save data now (<see cref="Map"/>), before
    /// the game sets itself up from it - the save. Not when it couldn't be read. (StoneForge makes scr_slotLoad hookable
    /// itself.)</summary>
    public static void OnLoaded(ModContext context, Action<SaveFile> handler)
        => context.OnScript("scr_slotLoad", after: call =>
        {
            if (Available && call.Args.Length > 1 && call.Args[0].Kind == GmKind.String && call.Args[1].Kind == GmKind.String)
                handler(new SaveFile(new SaveSlot(call.Args[0].AsString), call.Args[1].AsString));
        });

    /// <summary>Runs as a save is about to be written (scr_slotSaveUpdate: saving, an autosave, an exit save) - what's in
    /// the save data then is what goes to disk: the save. (StoneForge makes scr_slotSaveUpdate hookable itself.)</summary>
    public static void OnSaving(ModContext context, Action<SaveFile> handler)
        => context.OnScript("scr_slotSaveUpdate", before: call =>
        {
            if (Available && call.Args.Length > 1 && call.Args[0].Kind == GmKind.String && call.Args[1].Kind == GmKind.String)
                handler(new SaveFile(new SaveSlot(call.Args[0].AsString), call.Args[1].AsString));
            return false;
        });

    /// <summary>Whether there's save data: a game loaded or begun.</summary>
    public static bool Available => Map != null;

    /// <summary>The save data itself (live, the game's: don't destroy it); null outside a game.</summary>
    public static DsMap? Map => Game.Global["saveDataMap"].AsDsMap;

    /// <summary>Every section's name: the character's and the world's, and any a mod keeps.</summary>
    public static IReadOnlyList<string> Sections => Map is { } map ? map.Keys.Select(key => key.AsString).ToArray() : Array.Empty<string>();

    /// <summary>The world's sections: every one that isn't the character's.</summary>
    public static IReadOnlyList<string> WorldSections => Sections.Where(name => !CharacterSections.Contains(name)).ToArray();

    /// <summary>A section that's a map ("gameDataMap", "questsDataMap"...); null if there's none by that name, or it's a
    /// list.</summary>
    public static DsMap? Section(string name) => Map?.GetMap(name);

    /// <summary>A section that's a list ("inventoryDataList", "scrollsDataList"...); null if there's none by that
    /// name.</summary>
    public static DsList? SectionList(string name) => Map?.GetList(name);

    /// <summary>The whole save data as JSON, as the game writes it (json_encode); null outside a game.</summary>
    public static string? ToJson() => Map?.ToJson();

    /// <summary>Just these sections as JSON, an object by section name (those there are); null outside a game.</summary>
    public static string? ToJson(IEnumerable<string> sections)
    {
        if (ToJson() is not { } json || GmJson.Parse(json) is not JsonObject all)
            return null;
        var picked = new JsonObject();
        foreach (string name in sections)
            if (all[name] is { } section)
                picked[name] = section.DeepClone();
        return picked.ToJsonString();
    }

    /// <summary>Runs the game's save step now (scr_savegame), as its saves run it inside their fade: the character
    /// collected into the save data (where it stands, its inventory, skills and effects), then the save written as
    /// <paramref name="kind"/> (scr_slotUpdate - which a mod may hook to keep it). No fade, no room change.</summary>
    public static void Save(SaveKind kind = SaveKind.Auto)
        => Game.CallScript("scr_savegame", default, Rooms.CurrentName, (int)kind);

    /// <summary>Writes the save data as it is now over a save already on disk (its data.sav only:
    /// scr_slotSaveDataMapSave) - to add to one just made. False with no save data.</summary>
    public static bool WriteTo(SaveFile save)
    {
        if (Map is not { } map)
            return false;
        Game.CallScript("scr_slotSaveDataMapSave", default, save.Slot.Name, save.Name, map);
        return true;
    }

    /// <summary>The character's sections as JSON (<see cref="CharacterSections"/>): who's playing, without the world
    /// they're in. Null outside a game.</summary>
    public static string? CharacterJson() => ToJson(CharacterSections);

    /// <summary>A map of a mod's own in the save data, by <paramref name="key"/> (made empty the first time): what's in it
    /// is saved with the game and back when it's loaded. Use a key of the mod's own ("mymod_stash"): the game's sections
    /// are the save data's other keys.</summary>
    public static DsMap ModMap(string key)
    {
        if (Map is not { } map)
            throw new InvalidOperationException("There's no save data: no game is loaded or begun (SaveData.Available).");
        if (CharacterSections.Contains(key) || IsGames(key))
            throw new ArgumentException($"\"{key}\" is one of the game's sections: use a key of the mod's own.", nameof(key));
        if (map.GetMap(key) is { } existing)
            return existing;
        var made = DsMap.Create();
        map.AddMap(key, made);
        return made;
    }

    // (The game's sections, as scr_saveDataMapInit makes them.)
    private static bool IsGames(string key) => key is "gameDataMap" or "contractsDataMap" or "questsDataMap" or "journalDataMap"
        or "locationsRoomsDataMap" or "containersLootDataMap" or "timeDataMap" or "weatherDataMap" or "smokeDataMap"
        or "globalmapDataMap" or "factionsDataMap" or "caravanDataMap" or "randomDebuggerDataMap";
}
