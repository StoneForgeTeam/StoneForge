using StoneForge;

// The player's XP (Player): a kill's worth against its level, and XP given as the game gives it - logged as a kill only
// for a kill (laid out with FakeGame's room and scripts).
public class PlayerTests : FakeGame
{
    private const int Wolf = 302, WolfId = 100_050;
    private readonly FakeScripts _scripts = new();
    private readonly FakeWorld _world = new();

    public PlayerTests()
    {
        GameScripts = _scripts;
        World = _world;
        Refs = new FakeRefs();
        _world.ExistsByObject = true;
        _world.LendsIds = true;
        _world.Add(WolfId, Wolf);
        _world.Vars[WolfId] = new() { ["gain_xp"] = 100, ["Tier"] = 2 };
    }

    [Fact]
    public void A_kill_is_worth_less_XP_to_a_player_above_its_tier()
    {
        int level = 10;
        _scripts.Add("scr_atr", a => a[0].AsString == "LVL" ? level : GmValue.Undefined);
        var given = new List<double>();
        _scripts.Add("scr_get_XP", a => { given.Add(a[0].AsReal); return a[0].AsReal; });
        var logged = new List<double>();
        _scripts.Add("scr_actionsLogGetName", _ => "Wolf");
        _scripts.Add("scr_actionsLogXP", a => { logged.Add(a[2].AsReal); return GmValue.Undefined; });
        Instance wolf = Instance.FromId(WolfId);
        // (Level 10 against tier 2: no less. Level 20: 15% less a tier above.)
        Assert.Equal(100, Player.KillXp(wolf));
        level = 20;
        Assert.Equal(70, Player.KillXp(wolf), 6);
        // (Given as the game gives it; logged as a kill only for a kill.)
        Assert.Equal(40, Player.GiveXp(40));
        Assert.Empty(logged);
        Assert.Equal(70, Player.GiveXp(70, wolf));
        Assert.Equal(new double[] { 70 }, logged);
        Assert.Equal(new double[] { 40, 70 }, given);
    }
}
