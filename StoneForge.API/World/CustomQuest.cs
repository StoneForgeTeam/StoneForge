namespace StoneForge;

/// <summary>A registered quest, with state kept in Stoneshard's saved questsDataMap. Game thread only.</summary>
public sealed class CustomQuest
{
    internal readonly ModContext Context;
    internal readonly QuestDefinition Definition;
    internal readonly QuestObjective[] Objectives;
    internal CustomQuest(ModContext context, QuestDefinition definition, QuestObjective[] objectives)
    { Context = context; Definition = definition; Objectives = objectives; Id = context.ContentId(definition.Key); }
    public string Id { get; }
    public DsMap? Data => Quests.Get(Id);
    public bool IsStarted => Data?["IsStarted"].AsBool ?? false;
    public bool IsCompleted => Data?["isComplete"].AsInt == 1;
    public bool IsFailed => Data?["isComplete"].AsInt == -1;
    public bool RewardClaimed => Data?.Get("sf_reward_claimed", false).AsBool ?? false;
    public bool IsAvailable => Data != null;

    private DsMap Required()
    {
        Quests.EnsureCustom();
        if (!Quests.IsRegistered(this)) throw new InvalidOperationException("This quest's mod has been unloaded.");
        return Data ?? throw new InvalidOperationException("No quest data is loaded yet.");
    }
    public bool Start()
    {
        var data = Required();
        if (data["IsStarted"].AsBool || data["isComplete"].AsInt != 0) return false;
        if (Definition.DeadlineHours is { } hours)
        {
            if (!Time.Available) throw new InvalidOperationException("No game clock is available.");
            data["sf_deadline"] = Time.Timestamp + (long)hours * 60;
        }
        Game.CallScript("scr_quest_start", default, Id);
        return IsStarted;
    }
    public int Progress(string objective)
    {
        var data = Required();
        var (tasks, index) = Find(data, objective);
        return tasks[index + 2].AsInt;
    }
    public bool SetProgress(string objective, int count)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var data = Required();
        var (tasks, index) = Find(data, objective);
        if (data["isComplete"].AsInt != 0) return false;
        Start();
        int value = Math.Min(count, tasks[index + 1].AsInt);
        if (tasks[index + 2].AsInt == value) return false;
        Game.CallScript("scr_quest_set_progress", default, Id, tasks[index], value);
        // The game's diary normally refreshes only for sentinel progress; counted objectives need it too.
        RefreshDiary(data);
        if (Definition.AutoComplete && AllDone(data)) Complete();
        return true;
    }
    public bool Advance(string objective, int amount = 1)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        return SetProgress(objective, (int)Math.Min(int.MaxValue, (long)Progress(objective) + amount));
    }
    public bool Complete()
    {
        var data = Required();
        if (!IsStarted || data["isComplete"].AsInt != 0 || !AllDone(data)) return false;
        Game.CallScript("scr_quest_set_complete", default, Id);
        RefreshDiary(data);
        ClaimReward();
        return IsCompleted;
    }
    public bool Fail()
    {
        var data = Required();
        if (!IsStarted || data["isComplete"].AsInt != 0) return false;
        Game.CallScript("scr_quest_set_failed", default, Id);
        RefreshDiary(data);
        return IsFailed;
    }
    /// <summary>Claims a completed quest's reward once. With no player it remains pending until a later frame.</summary>
    public bool ClaimReward()
    {
        var data = Required();
        if (!IsCompleted || RewardClaimed || !Player.Exists) return false;
        // Persist the claim before invoking game/mod code; exceptions must not duplicate money or item rewards.
        data["sf_reward_claimed"] = true;
        if (Definition.RewardCrowns > 0) Game.CallScript("scr_reward_ext", Player.Instance, Definition.RewardCrowns);
        if (Definition.RewardExperience > 0) Player.GiveXp(Definition.RewardExperience);
        Definition.OnReward?.Invoke(this);
        return true;
    }
    private (DsList List, int Index) Find(DsMap data, string key)
    {
        if (!Objectives.Any(o => o.Key == key)) throw new ArgumentException("Unknown objective: " + key, nameof(key));
        var tasks = data.GetList("Tasks") ?? throw new InvalidOperationException("Quest objectives are missing.");
        for (int i = 0; i + 4 < tasks.Count; i += 5)
            if (tasks[i].AsString == ObjectiveId(key)) return (tasks, i);
        throw new InvalidOperationException("Saved quest objective is missing: " + key);
    }
    internal string ObjectiveId(string key) => Id + ":task:" + key;
    internal static bool AllDone(DsMap data)
    {
        if (data.GetList("Tasks") is not { } tasks || tasks.Count == 0) return false;
        for (int i = 0; i + 4 < tasks.Count; i += 5)
            if (tasks[i + 2].AsInt < tasks[i + 1].AsInt) return false;
        return true;
    }
    internal static void RefreshDiary(DsMap data)
    {
        if (Journal.DiaryShows(data)) Game.CallScript("scr_journalDiaryUpdate", default, data, true, false, true, false);
    }
}

/// <summary>A mod's custom quests. Registration is deferred until game data exists.</summary>
public sealed class ModQuests
{
    private readonly ModContext _context;
    internal ModQuests(ModContext context) => _context = context;
    public CustomQuest Add(QuestDefinition definition) => Quests.Register(_context, definition);
}
