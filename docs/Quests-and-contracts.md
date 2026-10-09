# Custom quests and contracts

Register definitions in your mod's Load method. Registration does not start a quest
or accept a contract. StoneForge waits for Stoneshard's data to exist, and stores
progress in the game's normal saved quest and contract records.

## Quests

```csharp
var definition = new QuestDefinition("bounty", "A local bounty", "Defeat three enemies")
{
    TitleKey = "quests.bounty.title",
    DescriptionKey = "quests.bounty.description",
    RewardCrowns = 100,
    RewardExperience = 50,
    DeadlineHours = 24,
    Objectives =
    {
        new QuestObjective("hunt", "Defeat enemies", 3)
        { TextKey = "quests.bounty.hunt" }
    }
};
CustomQuest bounty = context.Quests.Add(definition);

// Call Start from a dialogue, menu action or another game event, once a game is loaded.
bounty.Start();
Units.OnDied(context, (unit, killer) =>
{
    if (bounty.IsStarted && killer.Equals(Player.Instance)) bounty.Advance("hunt");
});
```

The quest's ID is `context.ContentId("bounty")`, such as `mymod:bounty`.
The standard Quests events also see its start, progress, completion and failure.
Objectives may include a world-map `Location` (`Cell`). Counters are nonnegative and
clamped to their target. Unknown objective keys throw; finished or failed quests
cannot advance. Keep quest and objective keys stable between mod versions.

All objectives must finish before Complete succeeds. With AutoComplete (the default),
finishing the final objective completes the quest. Set AutoComplete = false when a
conversation or other event should confirm completion. Fail marks a started quest
failed in the normal journal. DeadlineHours counts game time from Start; expiry is
checked on the next game frame, including after loading a save.

Crowns, XP and the optional OnReward callback are claimed once per saved quest.
Without a player, the reward remains pending until a later frame. The claim is saved
before invoking reward code: if a callback fails, StoneForge does not repeat rewards.
Reward callbacks should therefore validate their prerequisites before granting items.

## Contracts

Contracts retain the game's dungeon and settlement machinery. Choose a native
contract ID as a template, such as `bastion_Clearing`:

```csharp
var definition = new ContractDefinition("brigand_job", "bastion_Clearing",
    "A tougher assignment", "Defeat three enemies in the assigned dungeon")
{
    TitleKey = "contracts.brigand_job.title",
    DescriptionKey = "contracts.brigand_job.description",
    RewardCrowns = 250,
    DeadlineHours = 72,
    Objectives = { new QuestObjective("hunt", "Defeat enemies", 3) }
};
CustomContractType job = context.Contracts.Add(definition);
```

The template supplies dungeon faction, generation script, settlement eligibility,
boss initialization and native reward/reputation rules. RewardCrowns is the base
reward: the game's dungeon tier and other modifiers still apply. Optional Settlement,
Faction, ReputationReward, DeadlineHours and ExpirationHours override template values.
GenerateNaturally defaults to true, adding the new definition to the game's normal
contract selection pool. Existing assignments are preserved; new jobs appear when
the game next generates eligible contracts. Disabling natural generation still lets
the mod explicitly call `job.Create(dungeonCell)` for an uncontracted dungeon.
Use a dungeon appropriate for the chosen template; this API does not create dungeons.

With no Objectives, the template's native objectives and automatic tracking remain.
With Objectives, StoneForge keeps entering the assigned dungeon and returning for
payment, replacing the middle objectives with the mod's named counters. The mod must
track its chosen events and call Advance or SetProgress on a taken instance from
job.Instances. Scope events to the assigned dungeon when appropriate. Entry into the
dungeon and completion of every custom objective are required before native turn-in.

Accept, ClaimReward and Fail use the game's native contract scripts. NPC turn-in also
works. OnGenerated runs for natural and explicit generation; OnReward runs once after
native turn-in and can supply additional item rewards. Native deadlines, journal
entries, reputation and settlement consequences are retained.

## Localization, saves and unloading

TitleKey, DescriptionKey and objective TextKey use the registering mod's localization
catalog. Contract travel/return text also has optional localization keys. The journal
resolves current translations and a displayed entry refreshes after language/catalog
changes. Contract descriptions may use native `%dungeon_name%` and `%village_name%`
placeholders; keep them intact in translations.

Saved records own their nested maps/lists. Registering again reuses existing state,
including reward claims; it does not restart a quest or regenerate an active contract.
Disabling a mod removes its callbacks and prevents new contracts from being generated,
while saved records retain readable text and the native contract template metadata.
Mod-specific objective logic stops until that mod is enabled again. The game can still
process native contract deadlines and completed turn-ins from retained records.

These APIs add quests and dungeon contracts. New settlements, dungeon layouts and
standalone notice-board jobs need separate game changes. Use the
[dialogue API](Dialogues.md) for branching NPC conversations.

ExampleMod's `ExampleNpcJobs.cs` demonstrates three Osbrook jobs offered through
topics in native NPC Talk conversations (`context.Dialogues`).
It randomly assigns distinct employers, saving their `id_name` in each quest's map.
Stable NPC keys survive room changes and save loads; room instance IDs do not.
The examples track partial item deliveries or nearby brigand kills and require
returning to the same employer before completion and payment. The Esc menu's
Osbrook jobs ledger lists the employers; the ordinary Talk and trade options remain.
