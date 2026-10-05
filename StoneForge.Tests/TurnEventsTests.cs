using StoneForge;

// The world's turn passing, as an event (Turns.OnTurn): after the game's scr_global_turn (laid out with FakeGame's
// scripts).
public class TurnEventsTests : FakeGame
{
    private readonly FakeScripts _scripts = new();
    private readonly ModContext _context = new("turn_events_test");
    private int _turnsRun;

    public TurnEventsTests()
    {
        GameScripts = _scripts;
        _scripts.Add("scr_global_turn", _ =>
        {
            _turnsRun++;
            return GmValue.Undefined;
        });
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    [Fact]
    public void Each_world_turn_is_told_once_it_has_passed()
    {
        var seen = new List<int>();
        Turns.OnTurn(_context, () => seen.Add(_turnsRun));

        Game.CallScript("scr_global_turn", default);
        Game.CallScript("scr_global_turn", default);

        Assert.Equal(new[] { 1, 2 }, seen);
    }
}
