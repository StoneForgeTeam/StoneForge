using StoneForge;
public sealed class QuestMod : IStoneMod
{
    private CustomQuest _quest = null!;
    public void Load(ModContext context)
    {
        var quest = new QuestDefinition("bounty", "A bounty", "Defeat three enemies")
        { RewardCrowns = 50, Objectives = { new QuestObjective("hunt", "Defeat enemies", 3) } };
        _quest = context.Quests.Add(quest);
        Units.OnDied(context, (unit, killer) => { if (_quest.IsStarted && killer.Equals(Player.Instance)) _quest.Advance("hunt"); });
        context.Contracts.Add(new ContractDefinition("clearing", "bastion_Clearing", "A new contract", "Clear a dungeon") { RewardCrowns = 250 });
    }
    public void Unload() { }
}
