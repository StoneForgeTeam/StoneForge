using StoneForge;

// Moving between screens (Rooms): each move as the game's own buttons and doors make it - the room changer's events in
// their order - and none started when one's already under way or it isn't the screen for it (laid out with FakeGame's
// room, arrays, ds maps and scripts).
public class RoomsTests : FakeGame
{
    private const int Forest = 500, InGameRoom = 400, Player = 100_010, Changer = 100_020;
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    private readonly FakeWorld _world = new();
    // Each room change started: its room and events; and the other scripts run.
    private readonly List<(int Room, int[] Events, bool Fade)> _changes = new();
    private readonly List<string> _ran = new();
    private bool _saveValid = true;

    public RoomsTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        World = _world;
        Refs = new FakeRefs();
        KeepGlobalWrites = true;
        _world.ExistsByObject = true;
        _world.Assets["r_globalmap_forest"] = Forest;
        _world.Assets["r_ingame"] = InGameRoom;
        _scripts.Add("scr_smoothRoomChange", a =>
        {
            _changes.Add((a[0].AsInt, a[1].AsArray!.Select(e => e.AsInt).ToArray(), a[3].AsBool));
            return 1;
        });
        _scripts.Add("scr_slotExists", a => a[0].AsString == "character_1");
        _scripts.Add("scr_slotSaveMapLoad", _ =>
        {
            var info = DsMap.Create();
            info["valid"] = _saveValid;
            return info.Id;
        });
        foreach (string script in new[] { "scr_slotSaveTitleKeyPrepare", "scr_slotSaveScreenshotPrepare", "scr_loadingCreate" })
            _scripts.Add(script, _ => { _ran.Add(script); return GmValue.Undefined; });
        var slots = DsMap.Create();
        slots["lastCharacter"] = "N/A";
        slots["lastSave"] = "N/A";
        Globals["slotsMap"] = slots;
        Globals["mainMenuRoom"] = (int)Room.r_main_menu;
        Globals["other_hover"] = DsList.Create();
    }

    private void InGame()
    {
        Globals["room"] = InGameRoom;
        _world.Add(Player, (int)GameObjectId.o_player);
    }

    private void OnMainMenu() => Globals["room"] = (int)Room.r_main_menu;

    [Fact]
    public void Going_to_another_room_saves_the_one_being_left_as_a_door_does()
    {
        InGame();
        Assert.True(Rooms.Change("r_globalmap_forest"));
        Assert.True(Rooms.Change(Forest, saveLocation: false, fade: false));
        Assert.Equal((Forest, "4", true), (_changes[0].Room, string.Join(",", _changes[0].Events), _changes[0].Fade));
        Assert.Equal((Forest, "", false), (_changes[1].Room, string.Join(",", _changes[1].Events), _changes[1].Fade));
        Assert.Throws<ArgumentException>(() => Rooms.Change("r_nowhere"));
        Assert.Throws<ArgumentException>(() => Rooms.Change(12345));
    }

    [Fact]
    public void Nothing_starts_while_a_room_change_is_under_way()
    {
        InGame();
        _world.Add(Changer, (int)GameObjectId.o_smoothRoomChanger);
        Assert.True(Rooms.IsChanging);
        Assert.False(Rooms.Change(Forest));
        Assert.False(Rooms.ToMainMenu());
        Assert.Empty(_changes);
    }

    [Fact]
    public void Off_the_main_menu_and_out_of_a_game_theres_no_room_to_go_to()
    {
        Globals["room"] = InGameRoom;
        Assert.False(Rooms.Change(Forest));
        Assert.Empty(_changes);
    }

    [Fact]
    public void Back_to_the_main_menu_as_Exit_or_as_Save_and_Exit()
    {
        InGame();
        Assert.True(Rooms.ToMainMenu());
        Assert.Equal(-4, _changes[0].Room);
        Assert.Equal(new[] { 14, 3 }, _changes[0].Events);
        Assert.Empty(_ran);

        Assert.True(Rooms.ToMainMenu(save: true));
        Assert.Equal(new[] { 4, 7, 14, 3 }, _changes[1].Events);
        Assert.Equal(new[] { "scr_slotSaveTitleKeyPrepare", "scr_slotSaveScreenshotPrepare", "scr_loadingCreate" }, _ran);

        OnMainMenu();
        Assert.True(Rooms.ToMainMenu());
        Assert.Equal(2, _changes.Count);
    }

    [Fact]
    public void A_save_loads_as_the_save_menu_loads_it()
    {
        var save = new SaveFile(new SaveSlot("character_1"), "autosave_2");
        OnMainMenu();
        Globals["slotLoaded"] = true;
        Assert.True(Rooms.LoadSave(save));
        Assert.Equal(new[] { 2 }, _changes[0].Events);
        Assert.False(Globals["slotLoaded"].AsBool);
        var slots = Globals["slotsMap"].AsDsMap!.Value;
        Assert.Equal(("character_1", "autosave_2"), (slots["lastCharacter"].AsString, slots["lastSave"].AsString));

        // (From a game: it's left first, without saving.)
        InGame();
        Assert.True(Rooms.LoadSave(save));
        Assert.Equal(new[] { 14, 2 }, _changes[1].Events);

        Assert.False(Rooms.LoadSave(new SaveFile(new SaveSlot("character_9"), "save_1")));
        _saveValid = false;
        Assert.False(Rooms.LoadSave(save));
        Assert.Equal(2, _changes.Count);
    }

    [Fact]
    public void A_new_game_starts_from_the_main_menu_only()
    {
        OnMainMenu();
        Globals["globalMapInit"] = true;
        Assert.True(Rooms.StartNew(permadeath: true));
        Assert.Equal(new[] { 1 }, _changes[0].Events);
        Assert.True(Globals["permadeathMode"].AsBool);
        Assert.False(Globals["globalMapInit"].AsBool);
        Assert.Contains("scr_loadingCreate", _ran);

        Assert.True(Rooms.StartNew(prologue: true));
        Assert.Equal(new[] { 0 }, _changes[1].Events);
        Assert.False(Globals["permadeathMode"].AsBool);

        InGame();
        Assert.False(Rooms.StartNew());
        Assert.Equal(2, _changes.Count);
    }
}
