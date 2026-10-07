using StoneForge;

// The game's confirmation panel asking a mod's question (GameDialogs): Yes runs the action and closes it, closed
// otherwise runs No (laid out with FakeGame's room and scripts, its Yes fired as the game's code hook).
public class GameDialogsTests : FakeGame
{
    private const int PanelObject = 310, PanelId = 100_080;
    private readonly FakeScripts _scripts = new();
    private readonly FakeWorld _world = new();
    private readonly ModContext _context = new("dialogs_test");

    public GameDialogsTests()
    {
        GameScripts = _scripts;
        World = _world;
        _world.ExistsByObject = true;
        _world.LendsIds = true;
        _world.Assets["o_exit_confirm_panel"] = PanelObject;
        GameDialogs.Install(_context);
        _scripts.Add("scr_guiCreateContainer", _ =>
        {
            _world.Add(PanelId, PanelObject);
            _world.Vars[PanelId] = new();
            return PanelId;
        });
    }

    public override void Dispose()
    {
        GameDialogs.RemoveMod(_context.Id);
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    // The panel's Yes (its user event 0), on it.
    private static int Yes() => RunBefore("gml_Object_o_exit_confirm_panel_Other_10", PanelId);

    private static void Frame()
    {
        foreach (var (_, handler) in Hooks.FrameHandlers.ToList())
            handler();
    }

    [Fact]
    public void Yes_runs_the_question_s_action_and_closes_the_panel()
    {
        int yes = 0, no = 0;
        Assert.True(GameDialogs.Confirm(_context, "Leave?", () => yes++, () => no++));
        // (Ours: the action, the panel closed - its user event 1 - and the game's Yes skipped.)
        Assert.Equal(1, Yes());
        Assert.Equal(1, yes);
        Assert.Contains((PanelId, 1), _world.UserEvents);
        // (Answered: no No when it's gone, and a second Yes isn't ours.)
        _world.Active.Remove(PanelId);
        Frame();
        Assert.Equal(0, no);
        _world.Active.Add(PanelId);
        Assert.Equal(0, Yes());
        Assert.Equal(1, yes);
    }

    [Fact]
    public void Closed_without_Yes_runs_No()
    {
        int yes = 0, no = 0;
        Assert.True(GameDialogs.Confirm(_context, "Leave?", () => yes++, () => no++));
        Frame();
        Assert.Equal(0, no);
        _world.Active.Remove(PanelId);
        Frame();
        Assert.Equal((0, 1), (yes, no));
    }
}
