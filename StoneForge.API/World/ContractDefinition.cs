namespace StoneForge;

/// <summary>A custom dungeon contract based on a game's contract template (e.g. bastion_Clearing).
/// Keeps the game's settlement, dungeon, deadline and reward flow. Optional objectives replace the template's
/// goals between entering the dungeon and returning for the reward.</summary>
public sealed class ContractDefinition
{
    public ContractDefinition(string key, string basedOn, string title, string description)
    {
        ModIdentity.CheckKey(key, "contract");
        ArgumentException.ThrowIfNullOrWhiteSpace(basedOn);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Key = key; BasedOn = basedOn; Title = title; Description = description ?? throw new ArgumentNullException(nameof(description));
    }
    public string Key { get; }
    public string BasedOn { get; }
    public string Title { get; }
    public string Description { get; }
    public string? TitleKey { get; init; }
    public string? DescriptionKey { get; init; }
    /// <summary>Empty: retain the template's native objectives and automatic progress.</summary>
    public List<QuestObjective> Objectives { get; } = new();
    public int? RewardCrowns { get; init; }
    public int? ReputationReward { get; init; }
    public int? DeadlineHours { get; init; }
    public int? ExpirationHours { get; init; }
    /// <summary>The game's village type, such as Osbrook; null keeps the template's eligibility.</summary>
    public string? Settlement { get; init; }
    public string? Faction { get; init; }
    public bool GenerateNaturally { get; init; } = true;
    public string TravelText { get; init; } = "Head to %dungeon_name%";
    public string ReturnText { get; init; } = "Claim the reward";
    public string? TravelTextKey { get; init; }
    public string? ReturnTextKey { get; init; }
    /// <summary>Runs once when an instance is generated. Customize its dungeon or attach mod event tracking here.</summary>
    public Action<CustomContract>? OnGenerated { get; init; }
    /// <summary>Runs once after successful native turn-in; use for extra item rewards.</summary>
    public Action<CustomContract>? OnReward { get; init; }
    internal QuestObjective[] Validate()
    {
        if (RewardCrowns is < 0 || ReputationReward is < 0 || DeadlineHours is <= 0 || ExpirationHours is <= 0)
            throw new ArgumentOutOfRangeException(nameof(RewardCrowns), "Rewards must be nonnegative and deadlines positive.");
        return QuestDefinition.ValidateObjectives(Objectives, required: false);
    }
}
