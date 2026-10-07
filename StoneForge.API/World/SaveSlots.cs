namespace StoneForge;

/// <summary>The saved games on disk, as the game's save menu shows them: character folders (slots: "character_1"... up
/// to 10), newest first, each with its info (the game's character.map) and its saves - manual, auto and exit saves
/// (<see cref="SaveFile"/>). A mod can add its own values to a folder's info as the game writes it
/// (<see cref="OnInfoSaving"/>) and give a folder its own header in the save menu (<see cref="SetTitle"/>). Read through
/// the game's own scripts. Game thread only.</summary>
public static class SaveSlots
{
    /// <summary>Every character folder there is, newest first (scr_slotsGetOrderList).</summary>
    public static IReadOnlyList<SaveSlot> All
    {
        get
        {
            Game.CheckRunning("SaveSlots.All");
            return Names(Game.CallScript("scr_slotsGetOrderList", default)).Select(name => new SaveSlot(name)).ToArray();
        }
    }

    /// <summary>The character folder of the game being played - the last one loaded or saved; null if it has none yet (a
    /// new character, never saved).</summary>
    public static SaveSlot? Current => Last("lastCharacter") is { } name ? new SaveSlot(name) : null;

    /// <summary>The save of the game being played - the last one loaded or saved; null if none.</summary>
    public static SaveFile? CurrentSave => Current is { } slot && Last("lastSave") is { } name ? new SaveFile(slot, name) : null;

    /// <summary>A character folder by its name ("character_1"); null if there's none.</summary>
    public static SaveSlot? Get(string name) => new SaveSlot(name) is var slot && slot.Exists ? slot : null;

    /// <summary>Runs as the game writes a character folder's info (scr_slotMapSave - each time it saves): add the mod's
    /// own values to <c>info</c> and they're kept with the folder, to read back from <see cref="SaveSlot.Info"/> -
    /// written afresh each save, so add them every time. (StoneForge makes scr_slotMapSave hookable itself: no
    /// <c>[assembly: HookScript]</c> needed.)</summary>
    public static void OnInfoSaving(ModContext context, Action<SaveSlot, DsMap> handler)
    {
        // (Not again from inside the handler: reading a folder's info - slot.Info - rebuilds and saves an empty one.)
        bool running = false;
        context.OnScript("scr_slotMapSave", before: call =>
        {
            if (running || call.Args.Length < 2 || call.Args[0].Kind != GmKind.String || call.Args[1].AsDsMap is not { } info)
                return false;
            running = true;
            try
            {
                handler(new SaveSlot(call.Args[0].AsString), info);
            }
            finally
            {
                running = false;
            }
            return false;
        });
    }

    /// <summary>Gives character folders a header of the mod's own in the save menu (o_saveMenuSlotHeader): what
    /// <paramref name="title"/> returns for a folder - in place of its character's name, numbered and styled as the game
    /// does its own ("FailMelon, Friend (1)") - or null to keep the game's. With several mods, the last to give one
    /// wins.</summary>
    public static void SetTitle(ModContext context, Func<SaveSlot, string?> title)
        => context.OnCode("gml_Object_o_saveMenuSlotHeader_Other_25", after: (header, _) =>
        {
            if (!header.Exists || header.Get("slotDirName") is not { Kind: GmKind.String } folder)
                return;
            if (title(new SaveSlot(folder.AsString)) is { Length: > 0 } text)
                header["title"] = Title(header, folder.AsString, text);
        });

    /// <summary>A header as the game makes one: the name, then the folder's number in brackets, in the save menu's style
    /// (scr_stringTransform).</summary>
    internal static string Title(Instance header, string folder, string name)
    {
        string space = Game.CallScript("scr_actionsLogGetSpace", header).AsString;
        string number = new string(folder.Where(char.IsDigit).ToArray());
        return Game.CallScript("scr_stringTransform", header, name + space
            + Game.CallScript("scr_actionsLogGetSymbol", header, "openRoundBracket").AsString + number
            + Game.CallScript("scr_actionsLogGetSymbol", header, "closeRoundBracket").AsString, true).AsString;
    }

    // One of the game's slotsMap's last loaded or saved names; null for none ("N/A").
    private static string? Last(string key)
        => Game.Global["slotsMap"].AsDsMap is { } slots && slots.Get(key, "N/A") is { Kind: GmKind.String } name && name.AsString != "N/A"
            ? name.AsString : null;

    // A list of names a script made, read and destroyed.
    internal static string[] Names(GmValue list)
    {
        if (list.AsDsList is not { } names)
            return Array.Empty<string>();
        try
        {
            return Enumerable.Range(0, names.Count).Select(i => names[i].AsString).ToArray();
        }
        finally
        {
            names.Destroy();
        }
    }

    // A map a script loaded, read and destroyed; null for none (-4).
    internal static T? Read<T>(GmValue map, Func<DsMap, T> read) where T : class
    {
        if (map.AsDsMap is not { } loaded)
            return null;
        try
        {
            return read(loaded);
        }
        finally
        {
            loaded.Destroy();
        }
    }

    // The game's date (days since 30 December 1899, as GameMaker's date_current_datetime - in UTC, the game's time zone),
    // as local time; null for none (-4).
    internal static DateTime? Date(GmValue value)
        => value.Kind == GmKind.Real && value.AsReal > 0
            ? DateTime.SpecifyKind(DateTime.FromOADate(value.AsReal), DateTimeKind.Utc).ToLocalTime() : null;

    // A map's values, plain ones only (nested maps and lists aren't kept in this info).
    internal static IReadOnlyDictionary<string, GmValue> Values(DsMap map)
        => map.Keys.Where(key => key.Kind == GmKind.String).ToDictionary(key => key.AsString, key => map[key]);
}
