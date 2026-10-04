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

/// <summary>A character folder (<see cref="SaveSlots"/>): its info and its saves.</summary>
public sealed record SaveSlot(string Name)
{
    /// <summary>Its number, as the save menu shows it ("character_3": 3).</summary>
    public int Number => int.TryParse(new string(Name.Where(char.IsDigit).ToArray()), out int n) ? n : 0;

    /// <summary>Whether it's on disk (scr_slotExists).</summary>
    public bool Exists => Game.CallScript("scr_slotExists", default, Name).AsBool;

    /// <summary>Its info (scr_slotMapLoad: the game's character.map) - the character, permadeath, the prologue, when it
    /// was last saved, and any values a mod added (<see cref="SaveSlots.OnInfoSaving"/>); null if it isn't on
    /// disk.</summary>
    public SlotInfo? Info => SaveSlots.Read(Game.CallScript("scr_slotMapLoad", default, Name), map => new SlotInfo(SaveSlots.Values(map)));

    /// <summary>Its saves, newest first (scr_slotSavesGetOrderList).</summary>
    public IReadOnlyList<SaveFile> Saves
        => SaveSlots.Names(Game.CallScript("scr_slotSavesGetOrderList", default, Name)).Select(save => new SaveFile(this, save)).ToArray();
}

/// <summary>What kind of save a <see cref="SaveFile"/> is (the game's save types).</summary>
public enum SaveKind
{
    Unknown = -4,
    /// <summary>Saved at a campfire or an inn ("save_1", "save_2").</summary>
    Manual = 0,
    /// <summary>Saved by the game as you travel ("autosave_1"...).</summary>
    Auto = 1,
    /// <summary>Saved on quitting, removed once loaded ("exitsave_1").</summary>
    Exit = 2,
}

/// <summary>One save in a character folder (<see cref="SaveSlot.Saves"/>).</summary>
public sealed record SaveFile(SaveSlot Slot, string Name)
{
    /// <summary>Its kind, by its name.</summary>
    public SaveKind Kind => Name[..Math.Max(0, Name.IndexOf('_'))] switch
    {
        "save" => SaveKind.Manual,
        "autosave" => SaveKind.Auto,
        "exitsave" => SaveKind.Exit,
        _ => SaveKind.Unknown,
    };

    /// <summary>Its info (scr_slotSaveMapLoad: the game's save.map) - the character, where they were, when; null if it
    /// isn't on disk.</summary>
    public SaveInfo? Info => SaveSlots.Read(Game.CallScript("scr_slotSaveMapLoad", default, Slot.Name, Name), map => new SaveInfo(SaveSlots.Values(map)));
}

/// <summary>A character folder's info (<see cref="SaveSlot.Info"/>), its values by the game's names.</summary>
public sealed record SlotInfo(IReadOnlyDictionary<string, GmValue> Values)
{
    /// <summary>A value (a mod's too); undefined if there's none.</summary>
    public GmValue this[string key] => Values.TryGetValue(key, out GmValue value) ? value : GmValue.Undefined;

    /// <summary>The character's name key ("N/A": unknown).</summary>
    public string NameKey => this["nameKey"] is { Kind: GmKind.String } key ? key.AsString : "N/A";

    /// <summary>The character's name, as the game shows it (global.char_name by <see cref="NameKey"/>); null if it has
    /// none.</summary>
    public string? CharacterName => CharacterNames.Of(NameKey);

    public bool IsPermadeath => this["permadeath"].Kind != GmKind.Undefined && this["permadeath"].AsReal is not (-4 or 0);
    public bool IsPrologue => this["prologue"].Kind != GmKind.Undefined && this["prologue"].AsReal is not (-4 or 0);

    /// <summary>When it was last saved, in local time; null if unknown.</summary>
    public DateTime? SavedAt => SaveSlots.Date(this["dateTime"]);
}

/// <summary>A save's info (<see cref="SaveFile.Info"/>), its values by the game's names.</summary>
public sealed record SaveInfo(IReadOnlyDictionary<string, GmValue> Values)
{
    public GmValue this[string key] => Values.TryGetValue(key, out GmValue value) ? value : GmValue.Undefined;

    /// <summary>Whether the game can load it (false: its data was missing or broken).</summary>
    public bool IsValid => this["valid"].AsBool;

    public string NameKey => this["nameKey"] is { Kind: GmKind.String } key ? key.AsString : "N/A";
    public string? CharacterName => CharacterNames.Of(NameKey);

    /// <summary>The location's title key, where the character was ("N/A": none).</summary>
    public string LocationTitleKey => this["locationTitleKey"] is { Kind: GmKind.String } key ? key.AsString : "N/A";

    /// <summary>The character's portrait sprite's name.</summary>
    public string? Avatar => this["avatar"] is { Kind: GmKind.String } avatar ? avatar.AsString : null;

    public bool IsPermadeath => this["permadeath"].Kind != GmKind.Undefined && this["permadeath"].AsReal is not (-4 or 0);
    public bool IsPrologue => this["prologue"].Kind != GmKind.Undefined && this["prologue"].AsReal is not (-4 or 0);
    public DateTime? SavedAt => SaveSlots.Date(this["dateTime"]);
}

// The game's character names by name key (global.char_name).
internal static class CharacterNames
{
    internal static string? Of(string nameKey)
        => nameKey != "N/A" && Game.Global["char_name"].AsDsMap is { } names && names.Get(nameKey, "N/A") is { Kind: GmKind.String } name
            && name.AsString != "N/A" ? name.AsString : null;
}
