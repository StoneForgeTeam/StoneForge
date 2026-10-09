using System.Globalization;

namespace StoneForge;

public static partial class Contracts
{
    private static readonly Dictionary<string, CustomContractType> Custom = new(StringComparer.Ordinal);
    private static int _lastRaw = -1, _lastDatabase = -1, _lastRevision = -1;
    private static DsMap? Raw => Game.Global["contract_raw_data"].AsDsMap;
    private static DsList? Database => Game.Global["contractsDatabaseList"].AsDsList;
    private static DsList? Records => Game.Global["contractsDataList"].AsDsList;
    internal static DsMap? Record(int index) => Records is { } list && list.IsMap(index) ? new DsMap(list[index].AsInt) : null;
    internal static bool IsRegistered(CustomContractType type) => Custom.GetValueOrDefault(type.Id) == type;

    /// <summary>Registers a custom contract in the game's generation pool, deferred until its tables are loaded.</summary>
    public static CustomContractType Register(ModContext context, ContractDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(definition);
        var objectives = definition.Validate();
        if (Raw is { } raw && !raw.IsMap(definition.BasedOn))
            throw new ArgumentException("Unknown contract template: " + definition.BasedOn, nameof(definition));
        string id = context.ContentId(definition.Key);
        if (Custom.ContainsKey(id)) throw new ArgumentException("Contract already registered: " + id);
        var type = new CustomContractType(context, definition, objectives);
        Custom.Add(id, type);
        context.Localization.TranslationsChanged += () => EnsureCustom(force: true);
        context.OnScript("scr_contract_add", after: call =>
        {
            if (call.Result.AsDsMap is not { } map || map["contract_id"].AsString != id) return;
            InitializeCustom(type, map);
        });
        context.OnScript("scr_contract_finish", after: call =>
        {
            if (call.Args.Length == 0 || call.Args[0].AsDsMap is not { } map || map["contract_id"].AsString != id ||
                map["isComplete"].AsInt != 2 || map.Get("sf_reward_callback", false).AsBool) return;
            map["sf_reward_callback"] = true;
            int index = IndexOf(map);
            if (index >= 0) type.Definition.OnReward?.Invoke(new CustomContract(type, index));
        });
        EnsureCustom(force: true);
        return type;
    }
    internal static void Install(ModContext loader)
    {
        loader.OnScript("scr_contractsMapInit", before: _ => { EnsureRaw(); return false; }, after: _ => EnsureCustom(force: true));
        loader.OnScript("scr_contract_find_text", call =>
        {
            if (call.Args.Length < 2 || call.Args[0].Kind != GmKind.String) return false;
            string id = call.Args[0].AsString;
            int index = call.Args[1].AsInt;
            var map = FindById(Database, id) ?? FindById(Records, id);
            if (map?.GetList("sf_texts") is not { } texts || index < 0 || index >= texts.Count) return false;
            call.Result = texts[index]; return true;
        });
        loader.OnScript("scr_contract_target_number_change", after: call =>
        {
            if (call.Args.Length > 0 && call.Args[0].AsDsMap is { } map) UpdateCustomCompletion(map);
        });
        loader.OnScript("scr_contract_finish", call =>
        {
            // Native NPC turn-in is also guarded against repeat rewards for owned contracts.
            if (call.Args.Length == 0 || call.Args[0].AsDsMap is not { } map || !map.Has("sf_owner")) return false;
            return !map["isTaken"].AsBool || map["isComplete"].AsInt != 1;
        }, order: -100);
        loader.Frame += () => EnsureCustom();
    }
    private static void EnsureRaw()
    {
        if (Raw is not { } raw) return;
        foreach (var type in Custom.Values)
        {
            if (raw.GetMap(type.Definition.BasedOn) is not { } original)
                throw new InvalidOperationException("Unknown contract template: " + type.Definition.BasedOn);
            if (raw.GetMap(type.Id) is { } existing && existing.Get("sf_owner", "").AsString != type.Context.Id)
                throw new InvalidOperationException("Contract ID is already used: " + type.Id);
            var map = raw.GetMap(type.Id) ?? DsMap.Create();
            if (!raw.IsMap(type.Id)) raw.AddMap(type.Id, map);
            map.AssignFrom(original);
            map["sf_owner"] = type.Context.Id;
            map["Can_Generate"] = type.Definition.GenerateNaturally ? "1" : "0";
            void Number(string key, int? value) { if (value is { } n) map[key] = n.ToString(CultureInfo.InvariantCulture); }
            Number("Contract_Reward", type.Definition.RewardCrowns);
            Number("Contract_Reputation", type.Definition.ReputationReward);
            Number("Contract_Expiration", type.Definition.ExpirationHours);
            if (type.Definition.DeadlineHours is { } hours) map["Contract_Deadline"] = (hours / 24.0).ToString(CultureInfo.InvariantCulture);
            if (type.Definition.Settlement is { } settlement) map["Village_Type"] = settlement;
            if (type.Definition.Faction is { } faction) map["Faction"] = faction;
        }
    }
    internal static void EnsureCustom(bool force = false)
    {
        int rawId = Raw?.Id ?? -1, databaseId = Database?.Id ?? -1;
        if (!force && rawId == _lastRaw && databaseId == _lastDatabase && _lastRevision == Localization.Revision) return;
        _lastRaw = rawId; _lastDatabase = databaseId; _lastRevision = Localization.Revision;
        EnsureRaw();
        if (Raw is not { } raw || Database is not { } database) return;
        foreach (var type in Custom.Values)
        {
            var map = FindById(database, type.Id);
            if (map == null)
            {
                if (FindById(database, type.Definition.BasedOn) is not { } original)
                    throw new InvalidOperationException("The contract database lacks its template: " + type.Definition.BasedOn);
                var made = DsMap.Create();
                made.AssignFrom(original);
                made["contract_id"] = type.Id; made["Position"] = database.Count;
                made["isGenerate"] = false; made["isActive"] = false; made["isTaken"] = false; made["isComplete"] = 0;
                made["CD"] = 0;
                made.GetList("Targets")?.Clear();
                database.AddMap(made);
                map = made;
            }
            var entry = map.Value;
            entry["sf_owner"] = type.Context.Id; entry["can_generate"] = type.Definition.GenerateNaturally;
            if (type.Definition.DeadlineHours is { } hours) entry["Contract_Deadline"] = hours;
            if (type.Definition.ExpirationHours is { } expiration) entry["Contract_Expiration"] = expiration;
            var snapshot = entry.GetMap("sf_raw") ?? DsMap.Create();
            if (!entry.IsMap("sf_raw")) entry.AddMap("sf_raw", snapshot);
            snapshot.AssignFrom(raw.GetMap(type.Id)!.Value);
            WriteText(type, entry);
            foreach (var active in FindCustom(type))
            {
                WriteText(type, active.Data);
                if (Journal.DiaryShows(active.Data)) Game.CallScript("scr_journalDiaryUpdate", default, active.Data, false, false, true, false);
            }
        }
        // Saved contracts remain readable and can finish when their mod is disabled on the next launch.
        for (int i = 0; i < database.Count; i++)
            if (database.IsMap(i))
            {
                var entry = new DsMap(database[i].AsInt);
                string id = entry["contract_id"].AsString;
                if (Custom.ContainsKey(id) || entry.GetMap("sf_raw") is not { } snapshot) continue;
                entry["can_generate"] = false;
                var restored = raw.GetMap(id) ?? DsMap.Create();
                if (!raw.IsMap(id)) raw.AddMap(id, restored);
                restored.AssignFrom(snapshot); restored["Can_Generate"] = "0";
            }
    }
    private static DsMap? FindById(DsList? records, string id)
    {
        if (records is not { } list) return null;
        for (int i = 0; i < list.Count; i++)
            if (list.IsMap(i) && new DsMap(list[i].AsInt) is var map && map["contract_id"].AsString == id) return map;
        return null;
    }
    private static int IndexOf(DsMap map)
    {
        if (Records is not { } list) return -1;
        for (int i = 0; i < list.Count; i++) if (list.IsMap(i) && list[i].AsInt == map.Id) return i;
        return -1;
    }
    internal static IReadOnlyList<CustomContract> FindCustom(CustomContractType type)
    {
        var result = new List<CustomContract>();
        if (Records is not { } list) return result;
        for (int i = 0; i < list.Count; i++)
            if (Record(i) is { } map && map["contract_id"].AsString == type.Id) result.Add(new CustomContract(type, i));
        return result;
    }
    internal static CustomContract CreateCustom(CustomContractType type, Cell dungeon)
    {
        if (!IsRegistered(type)) throw new InvalidOperationException("This contract's mod has been unloaded.");
        EnsureCustom(force: true);
        var prototype = FindById(Database, type.Id) ?? throw new InvalidOperationException("No contract database is loaded.");
        var clock = Instances.All(GameObjectId.o_time_controller).FirstOrDefault();
        if (clock.IsNone) throw new InvalidOperationException("No contract time controller is available.");
        if (prototype["isGenerate"].AsBool) throw new InvalidOperationException("This contract already has a generated instance.");
        if (Game.CallScript("scr_globaltile_dungeon_get", default, "has_contract", dungeon.X, dungeon.Y, false).AsBool)
            throw new InvalidOperationException("This dungeon already has a contract.");
        var dungeonId = Game.CallScript("scr_globaltile_dungeon_get", default, "id", dungeon.X, dungeon.Y, -4);
        if (dungeonId.IsUndefined || dungeonId.Kind == GmKind.Real && dungeonId.AsReal < 0)
            throw new ArgumentException("The target is not a dungeon.", nameof(dungeon));
        var faction = Game.CallScript("scr_globaltile_dungeon_get", default, "dungeon_faction", dungeon.X, dungeon.Y, "");
        if (faction.Kind == GmKind.String && Raw?.GetMap(type.Id) is { } raw && raw["Faction"].AsString != faction.AsString)
            throw new ArgumentException("The dungeon faction does not match this contract's template.", nameof(dungeon));
        var made = Game.CallScript("scr_contract_add", clock, prototype, dungeon.X, dungeon.Y).AsDsMap
            ?? throw new InvalidOperationException("The game could not generate this contract.");
        int index = IndexOf(made);
        if (index < 0) throw new InvalidOperationException("Generated contract is not in the saved list.");
        return new CustomContract(type, index);
    }
    private static void InitializeCustom(CustomContractType type, DsMap map)
    {
        if (map.Get("sf_initialized", false).AsBool) return;
        map["sf_initialized"] = true; map["sf_owner"] = type.Context.Id;
        WriteText(type, map);
        if (type.Objectives.Length > 0)
        {
            var targets = map.GetList("Targets") ?? throw new InvalidOperationException("Contract targets are missing.");
            GmValue rewardCoordinates = "0/0";
            for (int i = 0; i < targets.Count; i++)
                if (targets.IsList(i) && new DsList(targets[i].AsInt) is var task && task.Count >= 6 && task[0].AsString == "Reward") rewardCoordinates = task[5];
            targets.Clear();
            void Add(GmValue key, int count, int status, GmValue coordinates)
            {
                var task = DsList.Create();
                task.Add(key); task.Add(0); task.Add(count); task.Add(type.Id); task.Add(status); task.Add(coordinates);
                targets.AddList(task);
            }
            Add(map["DungeonID"], 1, 0, map["Dungeon_Coordinate"]);
            foreach (var objective in type.Objectives)
                Add(type.ObjectiveId(objective.Key), objective.TargetCount, -1,
                    objective.Location is { } cell ? cell.X + "/" + cell.Y : map["Dungeon_Coordinate"]);
            Add("Reward", 1, -1, rewardCoordinates);
        }
        int index = IndexOf(map);
        if (index >= 0) type.Definition.OnGenerated?.Invoke(new CustomContract(type, index));
    }
    internal static void UpdateCustomCompletion(DsMap map)
    {
        if (!Custom.TryGetValue(map["contract_id"].AsString, out var type) || type.Objectives.Length == 0 ||
            !map["isTaken"].AsBool || map["isComplete"].AsInt != 0 || map.GetList("Targets") is not { } targets) return;
        for (int i = 0; i < targets.Count - 1; i++)
        {
            var task = new DsList(targets[i].AsInt);
            for (int j = 0; j + 5 < task.Count; j += 6)
                if (task[j + 1].AsInt < task[j + 2].AsInt) return;
        }
        Game.CallScript("scr_contract_complete", default, map);
    }
    private static void WriteText(CustomContractType type, DsMap map)
    {
        var texts = map.GetList("sf_texts") ?? DsList.Create();
        if (!map.IsList("sf_texts")) map.AddList("sf_texts", texts);
        texts.Clear();
        string Resolve(string value, string? key) => key == null ? value : type.Context.Localization.Get(key);
        texts.Add(type.Id);
        texts.Add(Resolve(type.Definition.Title, type.Definition.TitleKey));
        string description = Resolve(type.Definition.Description, type.Definition.DescriptionKey);
        texts.Add(description); texts.Add(description);
        if (type.Objectives.Length == 0)
        {
            var original = Game.Global["contracts_text"].AsDsList;
            int start = original is { } list ? Game.CallBuiltin("ds_list_find_index", list.Id, type.Definition.BasedOn).AsInt : -1;
            for (int i = 4; i <= 8; i++) texts.Add(start >= 0 && original is { } source ? source[start + i] : "Complete the objective");
        }
        else
        {
            texts.Add(Resolve(type.Definition.TravelText, type.Definition.TravelTextKey));
            foreach (var objective in type.Objectives) texts.Add(Resolve(objective.Text, objective.TextKey));
            texts.Add(Resolve(type.Definition.ReturnText, type.Definition.ReturnTextKey));
        }
    }
    internal static void RemoveMod(string mod)
    {
        var keys = Custom.Where(entry => entry.Value.Context.Id == mod).Select(entry => entry.Key).ToArray();
        if (keys.Length == 0) return;
        foreach (var key in keys) Custom.Remove(key);
        _lastRaw = _lastDatabase = -1;
        if (Game.Running) EnsureCustom(force: true);
    }
    internal static void ResetCustomForTests() { Custom.Clear(); _lastRaw = _lastDatabase = _lastRevision = -1; }
}
