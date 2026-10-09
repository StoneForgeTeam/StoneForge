using StoneForge;

public sealed class CustomQuestAndContractTests : FakeGame
{
    private const int PlayerId = 100001, ClockId = 100002;
    private readonly FakeDs _ds = new();
    private readonly FakeWorld _world = new();
    private readonly FakeScripts _scripts = new();
    private readonly ModContext _mod = new("custom_tasks_test");
    private readonly ModContext _loader = new("custom_tasks_loader");
    private readonly ModContext _other = new("custom_tasks_other");
    private DsMap _save, _quests, _clock, _raw;
    private DsList _database, _records;
    private int _crowns, _xp, _finishes;
    public CustomQuestAndContractTests()
    {
        Quests.ResetCustomForTests(); Contracts.ResetCustomForTests();
        Ds = _ds; World = _world; GameScripts = _scripts;
        _world.LendsIds = true;
        _world.Add(PlayerId, (int)GameObjectId.o_player);
        _world.Add(ClockId, (int)GameObjectId.o_time_controller);
        _save = DsMap.Create(); _quests = DsMap.Create(); _clock = DsMap.Create();
        foreach (string part in new[] { "months", "days", "hours", "minutes", "seconds" }) _clock[part] = 0;
        _save.AddMap("questsDataMap", _quests); _save.AddMap("timeDataMap", _clock);
        Globals["saveDataMap"] = _save.Id; Globals["questsDataMap"] = _quests.Id; Globals["timeDataMap"] = _clock.Id;
        var journal = DsMap.Create();
        foreach (string list in new[] { "questsList", "contractsList", "tasksCompletedList", "tasksFailedList" }) journal.AddList(list, DsList.Create());
        _save.AddMap("journalDataMap", journal); Globals["journalDataMap"] = journal.Id;
        _raw = DsMap.Create(); var template = DsMap.Create();
        foreach (string key in new[] { "Contract_Reward", "Contract_Reputation", "Contract_Expiration", "Contract_Deadline" }) template[key] = "10";
        template["Village_Type"] = "all"; template["Faction"] = "Brigand"; template["Can_Generate"] = "1";
        template["Script"] = "scr_contract_bastion_Clearing";
        _raw.AddMap("bastion_Clearing", template); Globals["contract_raw_data"] = _raw.Id;
        _database = DsList.Create(); _records = DsList.Create();
        var prototype = DsMap.Create(); prototype["contract_id"] = "bastion_Clearing"; prototype["Position"] = 0;
        prototype["isGenerate"] = false; prototype["Contract_Deadline"] = 240; prototype["Contract_Expiration"] = 10;
        prototype.AddList("Targets", DsList.Create()); _database.AddMap(prototype);
        var contracts = DsMap.Create(); contracts.AddList("contractsDatabaseList", _database); contracts.AddList("contractsDataList", _records);
        _save.AddMap("contractsDataMap", contracts);
        Globals["contractsDatabaseList"] = _database.Id; Globals["contractsDataList"] = _records.Id;
        var contractText = DsList.Create();
        foreach (string text in new[] { "bastion_Clearing", "Native title", "Offer", "Description", "Travel", "Clear", "Reward", "None", "None" }) contractText.Add(text);
        Globals["contracts_text"] = contractText.Id;
        _scripts.Add("questText", a => a[0]);
        _scripts.Add("scr_quest_definitions_create", _ => GmValue.Undefined);
        _scripts.Add("scr_contractsMapInit", _ => GmValue.Undefined);
        _scripts.Add("scr_quest_start", a =>
        {
            var q = Quests.Get(a[0].AsString)!.Value;
            if (!q["IsStarted"].AsBool && q["isComplete"].AsInt == 0) { q["IsStarted"] = true; journal.GetList("questsList")!.Value.Add(a[0]); }
            return GmValue.Undefined;
        });
        _scripts.Add("scr_quest_set_progress", a =>
        {
            var tasks = Quests.Get(a[0].AsString)!.Value.GetList("Tasks")!.Value;
            for (int i = 0; i < tasks.Count; i += 5) if (tasks[i].AsString == a[1].AsString) tasks[i + 2] = a[2];
            return true;
        });
        _scripts.Add("scr_quest_set_complete", a => { var q = Quests.Get(a[0].AsString)!.Value; q["isComplete"] = 1; return GmValue.Undefined; });
        _scripts.Add("scr_quest_set_failed", a => { var q = Quests.Get(a[0].AsString)!.Value; q["isComplete"] = -1; return GmValue.Undefined; });
        _scripts.Add("scr_journalDiaryUpdate", _ => GmValue.Undefined);
        _scripts.Add("scr_reward_ext", a => { _crowns += a[0].AsInt; return GmValue.Undefined; });
        _scripts.Add("scr_get_XP", a => { _xp += a[0].AsInt; return a[0]; });
        _scripts.Add("scr_contract_find_text", a => contractText[Game.CallBuiltin("ds_list_find_index", contractText.Id, a[0]).AsInt + a[1].AsInt]);
        _scripts.Add("scr_globaltile_dungeon_get", a => a[0].AsString == "has_contract" ? false : 777);
        _scripts.Add("scr_contract_add", a =>
        {
            var original = a[0].AsDsMap!.Value; original["isGenerate"] = true;
            var map = DsMap.Create(); map.AssignFrom(original); _records.AddMap(map);
            map["DungeonID"] = 777; map["Dungeon_Coordinate"] = "2/3"; map["Village_xy"] = "4_5";
            map["Contract_Village"] = "Osbrook"; map["isActive"] = true; map["isComplete"] = 0; map["isTaken"] = false;
            var targets = map.GetList("Targets")!.Value;
            foreach (string name in new[] { "Dungeon", "Clean", "Reward" })
            {
                var task = DsList.Create();
                foreach (GmValue value in new GmValue[] { name, 0, 1, map["contract_id"], -1, "4/5" }) task.Add(value);
                targets.AddList(task);
            }
            return map;
        });
        _scripts.Add("scr_contract_take", a => { var map = a[0].AsDsMap!.Value; map["isTaken"] = true; return GmValue.Undefined; });
        _scripts.Add("scr_contract_complete", a => { var map = a[0].AsDsMap!.Value; map["isComplete"] = 1; return GmValue.Undefined; });
        _scripts.Add("scr_contract_target_number_change", _ => true);
        _scripts.Add("scr_contract_finish", a => { var map = a[0].AsDsMap!.Value; map["isComplete"] = 2; map["isTaken"] = false; _finishes++; return GmValue.Undefined; });
        _scripts.Add("scr_contract_delete", a => { var map = a[0].AsDsMap!.Value; map["isComplete"] = -1; return GmValue.Undefined; });
        Quests.Install(_loader); Contracts.Install(_loader);
    }
    public override void Dispose()
    {
        Hooks.RemoveMod(_mod.Id); Hooks.RemoveMod(_other.Id); Hooks.RemoveMod(_loader.Id);
        Quests.ResetCustomForTests(); Contracts.ResetCustomForTests();
        base.Dispose();
    }
    private static QuestDefinition Quest(bool autoComplete = true) => new("bounty", "A bounty", "Hunt three targets")
    {
        AutoComplete = autoComplete, RewardCrowns = 30, RewardExperience = 20,
        Objectives = { new QuestObjective("hunt", "Hunt targets", 3) { Location = new Cell(2, 3) } },
    };
    [Fact]
    public void Quest_registration_is_deferred_namespaced_and_owns_nested_save_data()
    {
        Globals.Remove("questsDataMap");
        var quest = _mod.Quests.Add(Quest()); Assert.False(quest.IsAvailable);
        Globals["questsDataMap"] = _quests.Id; Quests.EnsureCustom();
        var other = _other.Quests.Add(Quest());
        Assert.NotEqual(quest.Id, other.Id);
        Assert.True(_quests.IsMap(quest.Id));
        Assert.True(quest.Data!.Value.GetList("Tasks")!.Value.IsList(4));
        Assert.True(quest.Data.Value.IsMap("sf_texts"));
        Assert.False(quest.IsStarted);
        Assert.Throws<ArgumentException>(() => _mod.Quests.Add(Quest()));
    }
    [Fact]
    public void Counted_quest_progress_completes_once_and_rewards_survive_save_restore()
    {
        var quest = _mod.Quests.Add(Quest());
        Assert.False(quest.Complete()); Assert.True(quest.Start()); Assert.False(quest.Start());
        Assert.True(quest.Advance("hunt", 2)); Assert.False(quest.IsCompleted);
        Assert.True(quest.Advance("hunt", int.MaxValue));
        Assert.Equal(3, quest.Progress("hunt")); Assert.True(quest.IsCompleted);
        Assert.Equal(30, _crowns); Assert.Equal(20, _xp);
        Assert.False(quest.ClaimReward()); Assert.False(quest.Complete()); Assert.False(quest.Fail());
        // AssignFrom copies owned nested state into a fresh save, just as a deserialized save supplies new maps.
        var loaded = DsMap.Create(); loaded.AssignFrom(_save);
        Globals["saveDataMap"] = loaded.Id; Globals["questsDataMap"] = loaded.GetMap("questsDataMap")!.Value.Id;
        Quests.EnsureCustom(forceText: true);
        Assert.True(quest.IsCompleted); Assert.True(quest.RewardClaimed); Assert.Equal(3, quest.Progress("hunt"));
        Assert.False(quest.ClaimReward()); Assert.Equal(30, _crowns);
    }
    [Fact]
    public void Explicit_completion_and_deadlines_do_not_overwrite_terminal_state()
    {
        var manual = _mod.Quests.Add(Quest(autoComplete: false));
        manual.Advance("hunt", 3); Assert.False(manual.IsCompleted); Assert.True(manual.Complete());
        var timed = _mod.Quests.Add(new QuestDefinition("timed", "Timed", "Finish within one hour")
        { DeadlineHours = 1, Objectives = { new QuestObjective("goal", "Goal") } });
        timed.Start(); _clock["hours"] = 1;
        foreach (var handler in Hooks.FrameHandlers.Where(h => h.Mod == _mod.Id).ToArray()) handler.Handler();
        Assert.True(timed.IsFailed); Assert.False(timed.Advance("goal")); Assert.False(timed.ClaimReward());
        Assert.Throws<ArgumentOutOfRangeException>(() => manual.Advance("hunt", -1));
        Assert.Throws<ArgumentException>(() => manual.Progress("missing"));
    }
    [Fact]
    public void Saved_quest_text_remains_readable_after_mod_unload()
    {
        var quest = _mod.Quests.Add(Quest()); quest.Start();
        Assert.Equal("A bounty", Game.CallScript("questText", default, quest.Id).AsString);
        Assert.Equal("Hunt targets", Game.CallScript("questText", default, quest.Id + ":task:hunt").AsString);
        Hooks.RemoveMod(_mod.Id); Quests.EnsureCustom(forceText: true);
        Assert.Equal("A bounty", Game.CallScript("questText", default, quest.Id).AsString);
        Assert.Throws<InvalidOperationException>(() => quest.Advance("hunt"));
        Assert.Equal("vanilla", Game.CallScript("questText", default, "vanilla").AsString);
    }
    [Fact]
    public void Invalid_definitions_are_rejected_before_registration()
    {
        Assert.Throws<ArgumentException>(() => _mod.Quests.Add(new QuestDefinition("empty", "Empty", "")));
        Assert.Throws<ArgumentException>(() => _mod.Quests.Add(new QuestDefinition("duplicate", "Duplicate", "")
        { Objectives = { new QuestObjective("one", "One"), new QuestObjective("one", "Again") } }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuestObjective("zero", "Zero", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _mod.Contracts.Add(new ContractDefinition("bad", "bastion_Clearing", "Bad", "") { DeadlineHours = 0 }));
    }
    private static ContractDefinition Contract(bool custom = true) => custom
        ? new("contract", "bastion_Clearing", "Custom contract", "Defeat three foes")
        { RewardCrowns = 50, DeadlineHours = 48, Objectives = { new QuestObjective("hunt", "Defeat foes", 3) } }
        : new("contract", "bastion_Clearing", "Custom contract", "Clear the dungeon");
    [Fact]
    public void Contract_registration_clones_template_and_preserves_native_database_positions()
    {
        var type = _mod.Contracts.Add(Contract());
        Assert.Equal("10", _raw.GetMap("bastion_Clearing")!.Value["Contract_Reward"].AsString);
        Assert.Equal("50", _raw.GetMap(type.Id)!.Value["Contract_Reward"].AsString);
        Assert.Equal("2", _raw.GetMap(type.Id)!.Value["Contract_Deadline"].AsString);
        Assert.Equal(2, _database.Count); Assert.Equal(0, new DsMap(_database[0].AsInt)["Position"].AsInt);
        Assert.Equal(1, new DsMap(_database[1].AsInt)["Position"].AsInt);
        Contracts.EnsureCustom(force: true); Assert.Equal(2, _database.Count);
        Assert.Equal("Custom contract", Game.CallScript("scr_contract_find_text", default, type.Id, 1).AsString);
        Assert.Equal("Native title", Game.CallScript("scr_contract_find_text", default, "bastion_Clearing", 1).AsString);
    }
    [Fact]
    public void Generated_custom_contract_uses_native_acceptance_and_requires_travel_and_objectives()
    {
        int generated = 0, rewarded = 0;
        var definition = new ContractDefinition("contract", "bastion_Clearing", "Custom contract", "Hunt")
        { Objectives = { new QuestObjective("hunt", "Defeat foes", 3) }, OnGenerated = _ => generated++, OnReward = _ => rewarded++ };
        var type = _mod.Contracts.Add(definition); var contract = type.Create(new Cell(2, 3));
        Assert.Equal(1, generated); Assert.Single(type.Instances);
        Assert.True(contract.Accept()); Assert.False(contract.Accept());
        contract.Advance("hunt", 3); Assert.False(contract.IsReady);
        var targets = contract.Data.GetList("Targets")!.Value;
        var travel = new DsList(targets[0].AsInt); travel[1] = 1; travel[4] = 1;
        Game.CallScript("scr_contract_target_number_change", default, contract.Data, 777, 1);
        Assert.True(contract.IsReady); Assert.True(contract.ClaimReward()); Assert.False(contract.ClaimReward());
        Game.CallScript("scr_contract_finish", default, contract.Data);
        Assert.Equal(1, _finishes); Assert.Equal(1, rewarded);
    }
    [Fact]
    public void Template_objectives_are_retained_and_disabled_contracts_keep_saved_text_and_raw_data()
    {
        var type = _mod.Contracts.Add(Contract(custom: false)); var contract = type.Create(new Cell(2, 3));
        Assert.Equal(3, contract.Data.GetList("Targets")!.Value.Count);
        Assert.Equal("Clear", Game.CallScript("scr_contract_find_text", default, type.Id, 5).AsString);
        contract.Accept(); Hooks.RemoveMod(_mod.Id);
        Assert.False(new DsMap(_database[1].AsInt)["can_generate"].AsBool);
        Assert.Equal("0", _raw.GetMap(type.Id)!.Value["Can_Generate"].AsString);
        Assert.Equal("Custom contract", Game.CallScript("scr_contract_find_text", default, type.Id, 1).AsString);
        // A restart recreates vanilla raw tables, while the saved database supplies the custom template snapshot.
        _raw.Remove(type.Id); Contracts.EnsureCustom(force: true);
        Assert.True(_raw.IsMap(type.Id));
        Assert.Throws<InvalidOperationException>(() => contract.Advance("hunt"));
    }
    [Fact]
    public void Contract_save_restore_preserves_custom_progress_and_failed_state()
    {
        var type = _mod.Contracts.Add(Contract()); var contract = type.Create(new Cell(2, 3));
        contract.Accept(); contract.Advance("hunt", 2);
        var loaded = DsMap.Create(); loaded.AssignFrom(_save);
        var data = loaded.GetMap("contractsDataMap")!.Value;
        Globals["contractsDatabaseList"] = data.GetList("contractsDatabaseList")!.Value.Id;
        Globals["contractsDataList"] = data.GetList("contractsDataList")!.Value.Id;
        Contracts.EnsureCustom(force: true);
        Assert.Equal(2, contract.Progress("hunt")); Assert.True(contract.Fail());
        Assert.False(contract.Advance("hunt")); Assert.False(contract.ClaimReward());
    }
    [Fact]
    public void Reward_waits_for_a_player_and_a_throwing_callback_cannot_duplicate_it()
    {
        _world.Objects[PlayerId] = 300001;
        int rewards = 0;
        var quest = _mod.Quests.Add(new QuestDefinition("pending", "Pending", "")
        { RewardCrowns = 10, OnReward = _ => rewards++, Objectives = { new QuestObjective("one", "One") } });
        quest.Advance("one"); Assert.True(quest.IsCompleted); Assert.False(quest.RewardClaimed); Assert.Equal(0, _crowns);
        _world.Objects[PlayerId] = (int)GameObjectId.o_player;
        foreach (var handler in Hooks.FrameHandlers.Where(h => h.Mod == _mod.Id).ToArray()) handler.Handler();
        Assert.True(quest.RewardClaimed); Assert.Equal(1, rewards); Assert.Equal(10, _crowns);
        var throwing = _mod.Quests.Add(new QuestDefinition("throwing", "Throwing", "")
        { RewardCrowns = 5, OnReward = _ => throw new Exception("Reward callback failed"), Objectives = { new QuestObjective("one", "One") } });
        Assert.Throws<Exception>(() => throwing.Advance("one"));
        Assert.True(throwing.RewardClaimed); Assert.False(throwing.ClaimReward()); Assert.Equal(15, _crowns);
    }
    [Fact]
    public void An_unknown_contract_template_does_not_leave_a_partial_registration()
    {
        Assert.Throws<ArgumentException>(() => _mod.Contracts.Add(new ContractDefinition("contract", "missing", "Bad", "")));
        Assert.NotNull(_mod.Contracts.Add(Contract()));
    }
    [Fact]
    public void Localized_quest_and_contract_text_refresh_without_resetting_progress()
    {
        string folder = Path.Combine(ModFiles.GameFolder, "mods", "sf-quest-text-" + Guid.NewGuid().ToString("N"));
        var context = new ModContext("localized_tasks_test", folder);
        string previous = Localization.Language;
        try
        {
            Directory.CreateDirectory(Path.Combine(folder, "Localization"));
            File.WriteAllText(Path.Combine(folder, "Localization", "en-US.json"), "{\"title\":\"English title\",\"task\":\"English objective\"}");
            File.WriteAllText(Path.Combine(folder, "Localization", "fr.json"), "{\"title\":\"Titre\",\"task\":\"Objectif\"}");
            Localization.SetLanguage("en-US");
            var quest = context.Quests.Add(new QuestDefinition("localized", "Fallback", "Description")
            { TitleKey = "title", Objectives = { new QuestObjective("one", "Fallback", 3) { TextKey = "task" } } });
            var type = context.Contracts.Add(new ContractDefinition("localized", "bastion_Clearing", "Fallback", "Description")
            { TitleKey = "title", Objectives = { new QuestObjective("one", "Fallback", 3) { TextKey = "task" } } });
            quest.Advance("one");
            Assert.Equal("English title", Game.CallScript("questText", default, quest.Id).AsString);
            Localization.SetLanguage("fr");
            foreach (var handler in Hooks.FrameHandlers.Where(h => h.Mod == context.Id).ToArray()) handler.Handler();
            Assert.Equal("Titre", Game.CallScript("questText", default, quest.Id).AsString);
            Assert.Equal("Objectif", Game.CallScript("questText", default, quest.Id + ":task:one").AsString);
            Assert.Equal("Titre", Game.CallScript("scr_contract_find_text", default, type.Id, 1).AsString);
            Assert.Equal("Objectif", Game.CallScript("scr_contract_find_text", default, type.Id, 5).AsString);
            Assert.Equal(1, quest.Progress("one"));
        }
        finally
        {
            Hooks.RemoveMod(context.Id);
            Localization.SetLanguage(previous);
            Directory.Delete(folder, true);
        }
    }
}
