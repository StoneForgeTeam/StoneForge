namespace StoneForge;

/// <summary>A custom contract template registered by a mod. Its instances use the game's native contract list.</summary>
public sealed class CustomContractType
{
    internal readonly ModContext Context;
    internal readonly ContractDefinition Definition;
    internal readonly QuestObjective[] Objectives;
    internal CustomContractType(ModContext context, ContractDefinition definition, QuestObjective[] objectives)
    { Context = context; Definition = definition; Objectives = objectives; Id = context.ContentId(definition.Key); }
    public string Id { get; }
    public IReadOnlyList<CustomContract> Instances => Contracts.FindCustom(this);
    /// <summary>Explicitly generates this contract in an eligible, uncontracted dungeon. Natural generation also works.</summary>
    public CustomContract Create(Cell dungeon) => Contracts.CreateCustom(this, dungeon);
    internal string ObjectiveId(string key) => Id + ":task:" + key;
}

/// <summary>One generated contract: accepting and claiming rewards follow the game's own scripts.</summary>
public sealed class CustomContract
{
    private readonly CustomContractType _type;
    internal CustomContract(CustomContractType type, int index) { _type = type; Index = index; }
    public string Id => _type.Id;
    public int Index { get; }
    /// <summary>The live, saved contract map. Never destroy it.</summary>
    public DsMap Data => Contracts.Record(Index) is { } map && map["contract_id"].AsString == Id
        ? map : throw new InvalidOperationException("This contract no longer exists.");
    public bool IsTaken => Data["isTaken"].AsBool;
    public bool IsReady => Data["isComplete"].AsInt == 1;
    public bool IsCompleted => Data["isComplete"].AsInt == 2;
    public bool IsFailed => Data["isComplete"].AsInt == -1;
    private void Registered()
    {
        if (!Contracts.IsRegistered(_type)) throw new InvalidOperationException("This contract's mod has been unloaded.");
    }
    public bool Accept()
    {
        Registered();
        var map = Data;
        if (IsTaken || !map["isActive"].AsBool || map["isComplete"].AsInt != 0) return false;
        var coordinates = map["Village_xy"].AsString.Split('_');
        if (coordinates.Length != 2 || !int.TryParse(coordinates[0], out int x) || !int.TryParse(coordinates[1], out int y))
            throw new InvalidOperationException("Contract settlement coordinates are missing.");
        Game.CallScript("scr_contract_take", Player.Instance, map, x, y, true);
        return IsTaken;
    }
    public int Progress(string objective)
    {
        Registered();
        var (tasks, index) = Find(objective);
        return tasks[index + 1].AsInt;
    }
    public bool SetProgress(string objective, int count)
    {
        Registered();
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        var (tasks, index) = Find(objective);
        if (!IsTaken || Data["isComplete"].AsInt != 0) return false;
        int value = Math.Min(count, tasks[index + 2].AsInt);
        if (tasks[index + 1].AsInt == value) return false;
        tasks[index + 1] = value; tasks[index + 4] = value >= tasks[index + 2].AsInt ? 1 : 0;
        Contracts.UpdateCustomCompletion(Data);
        if (Journal.DiaryShows(Data)) Game.CallScript("scr_journalDiaryUpdate", default, Data, false, false, true, false);
        return true;
    }
    public bool Advance(string objective, int amount = 1)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        return SetProgress(objective, (int)Math.Min(int.MaxValue, (long)Progress(objective) + amount));
    }
    public bool ClaimReward()
    {
        Registered();
        if (!IsTaken || !IsReady || !Player.Exists) return false;
        Game.CallScript("scr_contract_finish", Player.Instance, Data);
        return IsCompleted;
    }
    public bool Fail()
    {
        Registered();
        if (!IsTaken || IsCompleted || IsFailed) return false;
        Contracts.Delete(Data);
        return IsFailed;
    }
    private (DsList List, int Index) Find(string key)
    {
        if (!_type.Objectives.Any(o => o.Key == key)) throw new ArgumentException("Unknown custom objective: " + key, nameof(key));
        if (Data.GetList("Targets") is { } targets)
            for (int i = 0; i < targets.Count; i++)
                if (targets.IsList(i))
                {
                    var task = new DsList(targets[i].AsInt);
                    for (int j = 0; j + 5 < task.Count; j += 6)
                        if (task[j].AsString == _type.ObjectiveId(key)) return (task, j);
                }
        throw new InvalidOperationException("Saved contract objective is missing: " + key);
    }
}

/// <summary>A mod's custom contract definitions.</summary>
public sealed class ModContracts
{
    private readonly ModContext _context;
    internal ModContracts(ModContext context) => _context = context;
    public CustomContractType Add(ContractDefinition definition) => Contracts.Register(_context, definition);
}
