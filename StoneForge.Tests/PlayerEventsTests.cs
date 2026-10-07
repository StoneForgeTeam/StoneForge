using StoneForge;

// The player dying and levelling up, as events (Player.OnDying, OnLevelUp): o_player's user event 6, stoppable, and
// its Step with the level before and after (laid out with FakeGame's room and scripts).
public class PlayerEventsTests : FakeGame
{
    private const int PlayerId = 100_001;
    private readonly FakeWorld _world = new();
    private readonly FakeScripts _scripts = new();
    private readonly ModContext _context = new("player_events_test");
    private int _level = 3;

    public PlayerEventsTests()
    {
        World = _world;
        GameScripts = _scripts;
        _world.LendsIds = true;
        _world.Add(PlayerId, (int)GameObjectId.o_player);
        _scripts.Add("scr_atr", a => a[0].AsString == "LVL" ? _level : 0);
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    [Fact]
    public void Dying_is_told_and_a_handler_can_stop_it()
    {
        bool stop = false;
        int told = 0;
        Player.OnDying(_context, () =>
        {
            told++;
            return stop;
        });
        bool gameDeathRan = false;

        RunCode("gml_Object_o_player_Other_16", PlayerId, () => gameDeathRan = true);
        Assert.Equal(1, told);

        // (Stopped: the code entry's own code is skipped - the bridge doesn't run it when a before says so.)
        stop = true;
        Assert.Equal(1, RunBefore("gml_Object_o_player_Other_16", PlayerId));
        Assert.Equal(2, told);
        Assert.True(gameDeathRan);
    }

    [Fact]
    public void A_level_up_in_the_players_step_is_told_with_the_new_level()
    {
        var levels = new List<int>();
        Player.OnLevelUp(_context, levels.Add);

        RunCode("gml_Object_o_player_Step_0", PlayerId);
        RunCode("gml_Object_o_player_Step_0", PlayerId, () => _level = 5);
        RunCode("gml_Object_o_player_Step_0", PlayerId);

        Assert.Equal(new[] { 5 }, levels);
    }
}
