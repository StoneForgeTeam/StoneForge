namespace StoneForge;

public static partial class Quests
{
    private static readonly Dictionary<string, CustomQuest> Custom = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> CustomText = new(StringComparer.Ordinal);
    private static int _lastMap = -1;
    private static int _lastTextMap = -1;
    private static int _lastRevision = -1;

    /// <summary>Registers a saved quest without starting it. IDs are qualified by the registering mod.</summary>
    public static CustomQuest Register(ModContext context, QuestDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(definition);
        var objectives = definition.Validate();
        string id = context.ContentId(definition.Key);
        if (Custom.ContainsKey(id)) throw new ArgumentException("Quest already registered: " + id);
        var quest = new CustomQuest(context, definition, objectives);
        Custom.Add(id, quest);
        context.Localization.TranslationsChanged += () => EnsureCustom(forceText: true);
        context.Frame += () =>
        {
            if (quest.Data is not { } data) return;
            if (quest.IsStarted && !quest.IsCompleted && !quest.IsFailed && Time.Available &&
                data.Get("sf_deadline", -1).AsReal is var deadline && deadline >= 0 && Time.Timestamp >= deadline) quest.Fail();
            if (quest.IsCompleted && !quest.RewardClaimed && Player.Exists) quest.ClaimReward();
        };
        EnsureCustom(forceText: true);
        return quest;
    }
    internal static bool IsRegistered(CustomQuest quest) => Custom.GetValueOrDefault(quest.Id) == quest;
    internal static void Install(ModContext loader)
    {
        loader.OnScript("questText", call =>
        {
            if (call.Args.Length == 0 || call.Args[0].Kind != GmKind.String || !CustomText.TryGetValue(call.Args[0].AsString, out string? text)) return false;
            call.Result = text;
            return true;
        });
        loader.OnScript("scr_quest_definitions_create", after: _ => EnsureCustom(forceText: true));
        loader.Frame += () => EnsureCustom();
    }
    internal static void EnsureCustom(bool forceText = false)
    {
        if (Game.Global["questsDataMap"].AsDsMap is not { } map) return;
        int textMap = Game.Global["quest_text"].AsDsMap?.Id ?? -1;
        bool refresh = forceText || map.Id != _lastMap || textMap != _lastTextMap || _lastRevision != Localization.Revision;
        _lastMap = map.Id; _lastTextMap = textMap; _lastRevision = Localization.Revision;
        foreach (var quest in Custom.Values)
        {
            if (map.GetMap(quest.Id) is { } existing)
            {
                if (existing.Get("sf_owner", "").AsString != quest.Context.Id) throw new InvalidOperationException("Quest ID is already used by another definition: " + quest.Id);
                if (refresh) WriteText(quest, existing);
                continue;
            }
            var made = DsMap.Create();
            try
            {
                made["Name"] = quest.Id;
                made["IsStarted"] = false; made["isComplete"] = 0;
                made["CurrentTask"] = quest.ObjectiveId(quest.Objectives[0].Key);
                made["CurrentDescription"] = quest.Id + ":description";
                made["currentTimestamp"] = -4; made["StartedAtTimestamp"] = -4; made["CompletedAtTimestamp"] = -4;
                made["sf_owner"] = quest.Context.Id; made["sf_reward_claimed"] = false;
                var tasks = DsList.Create(); made.AddList("Tasks", tasks);
                foreach (var objective in quest.Objectives)
                {
                    tasks.Add(quest.ObjectiveId(objective.Key)); tasks.Add(objective.TargetCount); tasks.Add(0);
                    tasks.Add(quest.Id + ":description");
                    var coordinates = DsList.Create();
                    if (objective.Location is { } cell) { coordinates.Add("Absolute_Coordinate"); coordinates.Add(cell.X); coordinates.Add(cell.Y); }
                    tasks.AddList(coordinates);
                }
                WriteText(quest, made);
                map.AddMap(quest.Id, made);
                refresh = true;
            }
            catch { made.Destroy(); throw; }
        }
        if (refresh)
        {
            CustomText.Clear();
            foreach (var key in map.Keys)
                if (map.GetMap(key)?.GetMap("sf_texts") is { } texts)
                    foreach (var textKey in texts.Keys) CustomText[textKey.AsString] = texts[textKey].AsString;
            foreach (var quest in Custom.Values)
                if (quest.Data is { } data) CustomQuest.RefreshDiary(data);
        }
    }
    private static void WriteText(CustomQuest quest, DsMap map)
    {
        var text = map.GetMap("sf_texts") ?? DsMap.Create();
        if (!map.IsMap("sf_texts")) map.AddMap("sf_texts", text);
        string Resolve(string value, string? key) => key == null ? value : quest.Context.Localization.Get(key);
        text[quest.Id] = Resolve(quest.Definition.Title, quest.Definition.TitleKey);
        text[quest.Id + ":description"] = Resolve(quest.Definition.Description, quest.Definition.DescriptionKey);
        foreach (var objective in quest.Objectives) text[quest.ObjectiveId(objective.Key)] = Resolve(objective.Text, objective.TextKey);
    }
    internal static void RemoveMod(string mod)
    {
        foreach (var key in Custom.Where(entry => entry.Value.Context.Id == mod).Select(entry => entry.Key).ToArray()) Custom.Remove(key);
        // Saved state and text remain readable even with its mod disabled.
    }
    internal static void ResetCustomForTests()
    { Custom.Clear(); CustomText.Clear(); _lastMap = _lastTextMap = _lastRevision = -1; }
}
