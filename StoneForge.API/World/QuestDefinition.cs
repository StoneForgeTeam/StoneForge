namespace StoneForge;

/// <summary>A named objective in a custom quest or contract. Progress is supplied by the mod's game events.</summary>
public sealed class QuestObjective
{
    public QuestObjective(string key, string text, int targetCount = 1)
    {
        ModIdentity.CheckKey(key, "objective");
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if (targetCount < 1) throw new ArgumentOutOfRangeException(nameof(targetCount));
        Key = key; Text = text; TargetCount = targetCount;
    }
    public string Key { get; }
    public string Text { get; }
    public int TargetCount { get; }
    /// <summary>An optional key in the mod's localization catalog, used instead of Text.</summary>
    public string? TextKey { get; init; }
    /// <summary>An optional world-map coordinate shown beside this objective.</summary>
    public Cell? Location { get; init; }
}

/// <summary>A custom quest definition. Register it during Load with context.Quests.Add.</summary>
public sealed class QuestDefinition
{
    public QuestDefinition(string key, string title, string description)
    {
        ModIdentity.CheckKey(key, "quest");
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Key = key; Title = title; Description = description ?? throw new ArgumentNullException(nameof(description));
    }
    public string Key { get; }
    public string Title { get; }
    public string Description { get; }
    public string? TitleKey { get; init; }
    public string? DescriptionKey { get; init; }
    public List<QuestObjective> Objectives { get; } = new();
    public int RewardCrowns { get; init; }
    public int RewardExperience { get; init; }
    /// <summary>Game hours after starting; null means no deadline.</summary>
    public int? DeadlineHours { get; init; }
    public bool AutoComplete { get; init; } = true;
    /// <summary>Runs once for this saved quest, after its reward is claimed. Use for additional item rewards.</summary>
    public Action<CustomQuest>? OnReward { get; init; }

    internal QuestObjective[] Validate()
    {
        if (RewardCrowns < 0 || RewardExperience < 0 || DeadlineHours is <= 0)
            throw new ArgumentOutOfRangeException(nameof(RewardCrowns), "Rewards must be nonnegative and deadlines positive.");
        return ValidateObjectives(Objectives, required: true);
    }
    internal static QuestObjective[] ValidateObjectives(List<QuestObjective> objectives, bool required)
    {
        if (required && objectives.Count == 0) throw new ArgumentException("At least one objective is required.");
        if (objectives.Count > 64) throw new ArgumentException("At most 64 objectives are supported.");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var objective in objectives)
        {
            ArgumentNullException.ThrowIfNull(objective);
            if (!seen.Add(objective.Key)) throw new ArgumentException("Duplicate objective: " + objective.Key);
        }
        return objectives.ToArray();
    }
}
