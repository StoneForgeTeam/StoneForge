namespace StoneForge;

/// <summary>Moving between screens as the game does (scr_smoothRoomChange: a fade to black, the room changer's events,
/// then the next room): going to another room in the game being played, back to the main menu, loading a save and
/// starting a new game. Each returns false, doing nothing, when it can't be done now - another room change is already
/// under way (<see cref="IsChanging"/>), or it isn't the screen for it. Game thread only.</summary>
public static class Rooms
{
    // The room changer's events (o_smoothRoomChanger's user events), by what they do.
    private const int NewPrologue = 0, NewAdventure = 1, Load = 2, MainMenu = 3, SaveLocation = 4, ExitSave = 7, LeaveGame = 14;

    /// <summary>The room the game is in.</summary>
    public static int Current => Gm.Room;

    /// <summary>Its name ("r_globalmap_forest").</summary>
    public static string CurrentName => Game.CallBuiltin("room_get_name", Gm.Room).AsString;

    /// <summary>Whether the game is on its main menu.</summary>
    public static bool InMainMenu => Gm.InMainMenu;

    /// <summary>Whether a room change is under way (the game's o_smoothRoomChanger): another can't start until it's
    /// done.</summary>
    public static bool IsChanging => Game.CallBuiltin("instance_exists", (int)GameObjectId.o_smoothRoomChanger).AsBool;

    /// <summary>Goes to another room of the game being played, as a door does: the room being left is saved first, so
    /// it's as it was when the player comes back (the changer's event 4), unless <paramref name="saveLocation"/> is
    /// false; with <paramref name="fade"/>, through black. Only in game. Where the player stands in the new room is the
    /// game's to say (or set it first, as the game does: the character's localX / localY).</summary>
    public static bool Change(int room, bool saveLocation = true, bool fade = true)
    {
        Game.CheckRunning("Rooms.Change");
        if (!Game.CallBuiltin("room_exists", room).AsBool)
            throw new ArgumentException($"There's no room {room}.", nameof(room));
        return Gm.InGame && Start(room, saveLocation ? new[] { SaveLocation } : Array.Empty<int>(), fade);
    }

    /// <summary>Goes to another room by its name ("r_globalmap_forest").</summary>
    public static bool Change(string room, bool saveLocation = true, bool fade = true)
    {
        Game.CheckRunning("Rooms.Change");
        int index = Game.CallBuiltin("asset_get_index", room).AsInt;
        if (index < 0 || !Game.CallBuiltin("room_exists", index).AsBool)
            throw new ArgumentException($"There's no room \"{room}\".", nameof(room));
        return Change(index, saveLocation, fade);
    }

    /// <summary>Back to the main menu from the game being played, as the Esc menu's Exit does - the game isn't saved -
    /// or, with <paramref name="save"/>, as Save and Exit does: the room saved, then an exit save made (the save the
    /// game loads next and removes once loaded). True on the main menu already.</summary>
    public static bool ToMainMenu(bool save = false)
    {
        Game.CheckRunning("Rooms.ToMainMenu");
        if (Gm.InMainMenu)
            return true;
        if (!save)
            return Start(-4, new[] { LeaveGame, MainMenu });
        if (!Gm.InGame || !Start(-4, new[] { SaveLocation, ExitSave, LeaveGame, MainMenu }))
            return false;
        // (As the game's Save and Exit: the save's location title and screenshot, and the loading screen.)
        Game.CallScript("scr_slotSaveTitleKeyPrepare", default);
        Game.CallScript("scr_slotSaveScreenshotPrepare", default);
        Game.CallScript("scr_loadingCreate", default, -4, "N/A", 2 * RoomSpeed);
        return true;
    }

    /// <summary>Loads a save (<see cref="SaveSlots"/>) as the save menu does: from the main menu, or from the game being
    /// played - which is left without saving. False if it isn't on disk.</summary>
    public static bool LoadSave(SaveFile save)
    {
        Game.CheckRunning("Rooms.LoadSave");
        if (!save.Slot.Exists || save.Info is not { IsValid: true })
            return false;
        bool fromMenu = Gm.InMainMenu;
        if (fromMenu)
            PlayStartSound();
        if (!Start(-4, fromMenu ? new[] { Load } : new[] { LeaveGame, Load }))
            return false;
        // (What the changer's load event reads: the save to load, not one already loaded.)
        Game.Global["slotLoaded"] = false;
        if (Game.Global["slotsMap"].AsDsMap is { } slots)
        {
            slots["lastCharacter"] = save.Slot.Name;
            slots["lastSave"] = save.Name;
        }
        return true;
    }

    /// <summary>Starts a new game from the main menu, as its New Game buttons do: an adventure - a new world, its map
    /// generated, and the class picked at the start - or the <paramref name="prologue"/>, with or without
    /// <paramref name="permadeath"/>. Only on the main menu.</summary>
    public static bool StartNew(bool prologue = false, bool permadeath = false)
    {
        Game.CheckRunning("Rooms.StartNew");
        if (!Gm.InMainMenu || IsChanging)
            return false;
        Game.Global["permadeathMode"] = permadeath;
        // (A world map made afresh, not one left from a game played before.)
        Game.Global["globalMapInit"] = false;
        PlayStartSound();
        if (!Start(-4, new[] { prologue ? NewPrologue : NewAdventure }))
            return false;
        // (The loading screen, as the buttons show it: its picture and its tip.)
        if (Game.Global["other_hover"].AsDsList is { } hover)
            Game.CallScript("scr_loadingCreate", default, 4019, hover[22]);
        return true;
    }

    private static int RoomSpeed => Game.CallBuiltin("game_get_speed", 0).AsInt;

    private static void PlayStartSound()
    {
        int sound = Game.CallBuiltin("asset_get_index", "snd_ui_menu_start_game_st").AsInt;
        if (sound >= 0)
            Game.CallBuiltin("audio_play_sound", sound, 4, 0);
    }

    // The game's room change: to room (-4: none - the events say where), its events in order; false if one is already
    // under way.
    private static bool Start(int room, int[] events, bool fade = true)
    {
        if (IsChanging)
            return false;
        using GmArray list = GmArray.From(events.Select(e => (GmValue)e));
        // (Half a second between the fade and the events, the game's default.)
        GmValue changer = Game.CallScript("scr_smoothRoomChange", default, room, list, RoomSpeed / 2.0, fade);
        return !(changer.Kind == GmKind.Real && changer.AsReal == -4);
    }
}
