using System.Text.Json.Nodes;
using StoneForge;

// The save data and the saves on disk (SaveData, SaveSlots, SaveSlot, SaveFile): the save data's sections, the
// character's apart from the world's, a mod's own map in it; character folders and their saves read through the game's
// scripts; a mod's values added to a folder's info as the game saves it (laid out with FakeGame's ds maps and scripts).
public class SaveDataTests : FakeGame
{
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    // The folders on disk, newest first: each one's info and its saves' infos, newest first.
    private readonly List<(string Name, Dictionary<string, GmValue> Info, List<(string Name, Dictionary<string, GmValue> Info)> Saves)> _disk = new();
    private readonly List<string> _saved = new();

    public SaveDataTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        _scripts.Add("scr_slotsGetOrderList", _ => List(_disk.Select(slot => slot.Name)));
        _scripts.Add("scr_slotExists", a => _disk.Any(slot => slot.Name == a[0].AsString));
        _scripts.Add("scr_slotMapLoad", a => _disk.FirstOrDefault(slot => slot.Name == a[0].AsString) is { Name: not null } slot ? Map(slot.Info) : -4);
        _scripts.Add("scr_slotSavesGetOrderList", a => List(_disk.FirstOrDefault(slot => slot.Name == a[0].AsString).Saves?.Select(save => save.Name) ?? Array.Empty<string>()));
        _scripts.Add("scr_slotSaveMapLoad", a => _disk.FirstOrDefault(slot => slot.Name == a[0].AsString).Saves?.FirstOrDefault(save => save.Name == a[1].AsString) is { Name: not null } save ? Map(save.Info) : -4);
        // (As the game writes a folder's info: what the map holds then.)
        _scripts.Add("scr_slotMapSave", a => { _saved.Add(a[1].AsDsMap!.Value.ToJson()); return GmValue.Undefined; });
        var names = DsMap.Create();
        names["name_hero"] = "Verren";
        Globals["char_name"] = names;
    }

    private static int List(IEnumerable<string> names)
    {
        var list = DsList.Create();
        foreach (string name in names)
            list.Add(name);
        return list.Id;
    }

    private static int Map(Dictionary<string, GmValue> values)
    {
        var map = DsMap.Create();
        foreach (var (key, value) in values)
            map[key] = value;
        return map.Id;
    }

    // A game being played: its save data's sections, and the folder and save it was loaded from.
    private DsMap Playing(string slot = "character_2", string save = "autosave_1")
    {
        var data = DsMap.Create();
        foreach (string name in new[] { "characterDataMap", "gameDataMap", "questsDataMap" })
            data.AddMap(name, DsMap.Create());
        var character = data.GetMap("characterDataMap")!.Value;
        character["nameKey"] = "name_hero";
        var game = data.GetMap("gameDataMap")!.Value;
        game["seed"] = 77;
        var inventory = DsList.Create();
        inventory.Add("o_inv_wine");
        data.AddList("inventoryDataList", inventory);
        Globals["saveDataMap"] = data;
        var slots = DsMap.Create();
        slots["lastCharacter"] = slot;
        slots["lastSave"] = save;
        Globals["slotsMap"] = slots;
        return data;
    }

    [Fact]
    public void The_save_data_is_in_sections_the_characters_apart_from_the_worlds()
    {
        Playing();
        Assert.True(SaveData.Available);
        Assert.Equal(new[] { "characterDataMap", "gameDataMap", "inventoryDataList", "questsDataMap" }, SaveData.Sections.Order());
        Assert.Equal(new[] { "gameDataMap", "questsDataMap" }, SaveData.WorldSections.Order());
        Assert.Equal(77, SaveData.Section("gameDataMap")!.Value["seed"].AsInt);
        Assert.Null(SaveData.Section("inventoryDataList"));
        Assert.Equal("o_inv_wine", SaveData.SectionList("inventoryDataList")!.Value[0].AsString);

        var character = (JsonObject)JsonNode.Parse(SaveData.CharacterJson()!)!;
        Assert.Equal(new[] { "characterDataMap", "inventoryDataList" }, character.Select(p => p.Key).Order());
        Assert.Equal("name_hero", character["characterDataMap"]!["nameKey"]!.GetValue<string>());
        Assert.Contains("questsDataMap", SaveData.ToJson());
    }

    [Fact]
    public void A_mod_keeps_its_own_map_in_the_save_data()
    {
        var data = Playing();
        var stash = SaveData.ModMap("mymod_stash");
        stash["gold"] = 5;
        Assert.Equal(stash, SaveData.ModMap("mymod_stash"));
        Assert.Equal(5, data.GetMap("mymod_stash")!.Value["gold"].AsInt);
        Assert.Throws<ArgumentException>(() => SaveData.ModMap("questsDataMap"));
        Assert.Throws<ArgumentException>(() => SaveData.ModMap("inventoryDataList"));
    }

    [Fact]
    public void With_no_game_there_is_no_save_data()
    {
        Assert.False(SaveData.Available);
        Assert.Empty(SaveData.Sections);
        Assert.Null(SaveData.ToJson());
        Assert.Null(SaveData.CharacterJson());
        Assert.Throws<InvalidOperationException>(() => SaveData.ModMap("mymod_stash"));
        Assert.Null(SaveSlots.Current);
    }

    [Fact]
    public void Character_folders_and_their_saves_are_read_through_the_games_scripts()
    {
        // (30 December 1899 + 46000 days: 9 December 2025, in UTC as the game keeps it.)
        _disk.Add(("character_2", new() { ["nameKey"] = "name_hero", ["permadeath"] = 0, ["prologue"] = 0, ["dateTime"] = 46000.5, ["mymod"] = "x" },
            new() { ("autosave_1", new() { ["valid"] = true, ["nameKey"] = "name_hero", ["locationTitleKey"] = "osbrook", ["dateTime"] = 46000.5 }),
                    ("save_1", new() { ["valid"] = false }) }));
        _disk.Add(("character_1", new() { ["nameKey"] = "N/A", ["permadeath"] = 1, ["prologue"] = -4, ["dateTime"] = -4 }, new()));
        Playing();

        Assert.Equal(new[] { "character_2", "character_1" }, SaveSlots.All.Select(slot => slot.Name));
        var slot = SaveSlots.Current!;
        Assert.Equal("character_2", slot.Name);
        Assert.Equal(2, slot.Number);
        var info = slot.Info!;
        Assert.Equal("Verren", info.CharacterName);
        Assert.False(info.IsPermadeath);
        Assert.Equal(DateTimeKind.Local, info.SavedAt!.Value.Kind);
        Assert.Equal(new DateTime(2025, 12, 9, 12, 0, 0, DateTimeKind.Utc), info.SavedAt.Value.ToUniversalTime());
        Assert.Equal("x", info["mymod"].AsString);

        Assert.Equal(new[] { SaveKind.Auto, SaveKind.Manual }, slot.Saves.Select(save => save.Kind));
        Assert.Equal(new SaveFile(slot, "autosave_1"), SaveSlots.CurrentSave);
        var save = SaveSlots.CurrentSave!.Info!;
        Assert.True(save.IsValid);
        Assert.Equal(("Verren", "osbrook"), (save.CharacterName, save.LocationTitleKey));
        Assert.False(slot.Saves[1].Info!.IsValid);

        var other = SaveSlots.Get("character_1")!.Info!;
        Assert.Null(other.CharacterName);
        Assert.True(other.IsPermadeath);
        Assert.False(other.IsPrologue);
        Assert.Null(other.SavedAt);
        Assert.Null(SaveSlots.Get("character_9"));
        Assert.Null(new SaveSlot("character_9").Info);
        Assert.Equal(SaveKind.Exit, new SaveFile(slot, "exitsave_1").Kind);
    }

    [Fact]
    public void A_new_character_has_no_folder_yet()
    {
        Playing(slot: "N/A", save: "N/A");
        Assert.Null(SaveSlots.Current);
        Assert.Null(SaveSlots.CurrentSave);
    }

    [Fact]
    public void A_mod_adds_its_values_to_a_folders_info_as_the_game_saves_it()
    {
        var context = new ModContext("save_info_test");
        try
        {
            SaveSlots.OnInfoSaving(context, (slot, info) => info["mymod_players"] = slot.Name + ": A, B");
            var info = DsMap.Create();
            info["nameKey"] = "name_hero";
            Game.CallScript("scr_slotMapSave", default, "character_3", info.Id);
            var written = (JsonObject)JsonNode.Parse(Assert.Single(_saved))!;
            Assert.Equal("character_3: A, B", written["mymod_players"]!.GetValue<string>());
            Assert.Equal("name_hero", written["nameKey"]!.GetValue<string>());

            // (A handler whose reading saves again - the game rebuilding an empty folder's info - isn't run again.)
            var nested = new ModContext("save_info_nested");
            int runs = 0;
            SaveSlots.OnInfoSaving(nested, (_, map) => { runs++; Game.CallScript("scr_slotMapSave", default, "character_3", map.Id); });
            Game.CallScript("scr_slotMapSave", default, "character_3", info.Id);
            Assert.Equal(1, runs);
            Hooks.RemoveMod(nested.Name);
        }
        finally
        {
            Hooks.RemoveMod(context.Name);
        }
    }
}
