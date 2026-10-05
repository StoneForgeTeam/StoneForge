using StoneForge;

// A skill used (Skills.OnUsed: after its user event 3) and quests moving on (Quests: told only when their state changed
// around the game's quest scripts) (laid out with FakeGame's room, lists and scripts).
public class SkillAndQuestEventsTests : FakeGame
{
    private const int SkillId = 100_001, PlayerId = 100_002, Wolf = 100_003;
    private readonly FakeWorld _world = new();
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    private readonly ModContext _context = new("skill_quest_test");
    private readonly DsMap _quest;
    private readonly DsList _tasks;

    public SkillAndQuestEventsTests()
    {
        World = _world;
        Ds = _ds;
        GameScripts = _scripts;
        _world.LendsIds = true;
        _world.Add(SkillId, 300);
        _world.Add(PlayerId, (int)GameObjectId.o_player);
        _world.Add(Wolf, 301);
        // (A quest as the game keeps it: started or not, done (1) or failed (-1), five values a task.)
        var quests = DsMap.Create();
        _quest = DsMap.Create();
        _quest["IsStarted"] = false;
        _quest["isComplete"] = 0;
        _tasks = DsList.Create();
        foreach (GmValue value in new GmValue[] { "find", 3, 0, "Find the tablet", "" })
            _tasks.Add(value);
        _quest.AddList("Tasks", _tasks);
        quests.AddMap("BlackTablet", _quest);
        Globals["questsDataMap"] = quests.Id;
        _scripts.Add("scr_quest_start", _ => { _quest["IsStarted"] = true; return GmValue.Undefined; });
        _scripts.Add("scr_quest_set_progress", a => { _tasks[2] = a[2]; return true; });
        _scripts.Add("scr_quest_set_complete", _ => { _quest["isComplete"] = 1; return GmValue.Undefined; });
        _scripts.Add("scr_quest_set_failed", _ => { _quest["isComplete"] = -1; return GmValue.Undefined; });
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    [Fact]
    public void A_skill_used_is_told_with_its_caster_and_target()
    {
        var used = new List<SkillCast>();
        Skills.OnUsed(_context, used.Add);
        _world.Vars[SkillId] = new() { ["owner"] = PlayerId, ["target"] = Wolf, ["is_crit"] = true };

        RunCode("gml_Object_o_skill_Other_13", SkillId);

        var cast = Assert.Single(used);
        Assert.Equal((Instance.FromId(SkillId), Instance.FromId(PlayerId), Instance.FromId(Wolf), true),
            (cast.Skill.Instance, cast.Caster.Instance, cast.Target.Instance, cast.IsCrit));
    }

    [Fact]
    public void A_quest_is_told_as_it_starts_moves_on_and_ends_and_not_again()
    {
        var told = new List<string>();
        Quests.OnStarted(_context, q => told.Add("started " + q));
        Quests.OnProgress(_context, (q, task, value) => told.Add($"progress {q} {task} {value.AsReal}"));
        Quests.OnCompleted(_context, q => told.Add("completed " + q));
        Quests.OnFailed(_context, q => told.Add("failed " + q));

        Game.CallScript("scr_quest_start", default, "BlackTablet");
        Game.CallScript("scr_quest_start", default, "BlackTablet");
        Game.CallScript("scr_quest_set_progress", default, "BlackTablet", "find", 2);
        Game.CallScript("scr_quest_set_progress", default, "BlackTablet", "find", 2);
        Game.CallScript("scr_quest_set_complete", default, "BlackTablet");

        Assert.Equal(new[] { "started BlackTablet", "progress BlackTablet find 2", "completed BlackTablet" }, told);
        Assert.True(Quests.IsStarted("BlackTablet"));
        Assert.True(Quests.IsCompleted("BlackTablet"));
        Assert.False(Quests.IsFailed("BlackTablet"));
    }
}
