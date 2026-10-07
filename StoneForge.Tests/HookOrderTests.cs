using StoneForge;

// Every mod's hooks on one script or code entry: run by HookOrder, then as added - and two mods' before hooks both
// replacing the same call found as a conflict, said once, the later one's result used (laid out with FakeGame's scripts,
// whose hook block calls in as the patcher's does).
public class HookOrderTests : FakeGame
{
    private const string Double = "scr_sf_test_order_double", Code = "gml_Object_o_sf_test_order_Step_0";
    private readonly FakeScripts _scripts = new();
    private readonly List<HookConflict> _conflicts = new();

    public HookOrderTests()
    {
        GameScripts = _scripts;
        _scripts.Add(Double, args => args[0].AsReal * 2);
        Hooks.ResetConflictsForTests();
        Hooks.Conflicted = _conflicts.Add;
    }

    public override void Dispose()
    {
        Hooks.Conflicted = null;
        foreach (string mod in new[] { "mod_a", "mod_b", "mod_c" })
            Hooks.RemoveMod(mod);
        base.Dispose();
    }

    private static double Call(double arg) => Game.CallScript(Double, default, arg).AsReal;

    [Fact]
    public void Hooks_run_by_their_order_then_as_added()
    {
        var ran = new List<string>();
        Hooks.AddScript("mod_a", Double, _ => { ran.Add("a last"); return false; }, order: HookOrder.Last);
        Hooks.AddScript("mod_b", Double, _ => { ran.Add("b normal"); return false; });
        Hooks.AddScript("mod_c", Double, _ => { ran.Add("c first"); return false; }, order: HookOrder.First);
        Hooks.AddScript("mod_a", Double, _ => { ran.Add("a normal"); return false; });
        Hooks.AddScript("mod_c", Double, _ => { ran.Add("c early"); return false; }, order: HookOrder.Early);
        // (Any number between: just before the Last ones, and before a Normal one.)
        Hooks.AddScript("mod_b", Double, _ => { ran.Add("b late+50"); return false; }, order: HookOrder.Late + 50);
        Hooks.AddScript("mod_b", Double, _ => { ran.Add("b -1"); return false; }, order: -1);
        Assert.Equal(10, Call(5));
        Assert.Equal(new[] { "c first", "c early", "b -1", "b normal", "a normal", "b late+50", "a last" }, ran);
        Assert.Equal(("Late", "150"), (HookOrder.Name(HookOrder.Late), HookOrder.Name(150)));
    }

    [Fact]
    public void Two_mods_replacing_a_script_is_a_conflict_said_once_and_the_later_result_is_used()
    {
        // (Loaded first, but hooked Last: its result's the one used.)
        Hooks.AddScript("mod_a", Double, call => { call.Result = 1; return true; }, order: HookOrder.Last);
        Hooks.AddScript("mod_b", Double, call => { call.Result = 2; return true; });
        Assert.Equal(1, Call(5));
        Assert.Equal(1, Call(6));
        Assert.Equal(0, _scripts.Runs[Double]);
        var conflict = Assert.Single(_conflicts);
        Assert.Equal(new HookConflict(Double, true, "mod_b", "mod_a"), conflict);
        Assert.Contains("mod_a's result is used", conflict.Describe(mod => mod));
    }

    [Fact]
    public void Replacing_alone_isnt_a_conflict()
    {
        // (Another mod's hook that lets the call through, and a mod's two hooks both replacing: no conflict.)
        Hooks.AddScript("mod_a", Double, call => { call.Result = 1; return true; });
        Hooks.AddScript("mod_a", Double, call => { call.Result = 3; return true; });
        Hooks.AddScript("mod_b", Double, _ => false);
        Assert.Equal(3, Call(5));
        Assert.Empty(_conflicts);
    }

    [Fact]
    public void Two_mods_skipping_a_code_entry_is_a_conflict()
    {
        Hooks.Add("mod_a", Code, (_, _) => true, null);
        Hooks.Add("mod_b", Code, (_, _) => true, null);
        Assert.Equal(1, RunBefore(Code, 1));
        Assert.Equal(1, RunBefore(Code, 1));
        var conflict = Assert.Single(_conflicts);
        Assert.Equal(new HookConflict(Code, false, "mod_a", "mod_b"), conflict);
        Assert.Contains("both skip", conflict.Describe(mod => mod));
    }

    [Fact]
    public void Mods_hooking_the_same_call_at_the_same_order_might_conflict()
    {
        // (Before hooks at Normal from two mods; one at Last apart; after hooks and StoneForge's own aren't counted.)
        Hooks.AddScript("mod_a", Double, _ => false);
        Hooks.AddScript("mod_b", Double, _ => false);
        Hooks.AddScript("mod_c", Double, _ => false, order: HookOrder.Last);
        Hooks.AddScript("mod_c", Double, null, _ => { });
        Hooks.Add("mod_a", Code, (_, _) => false, null);
        Hooks.Add("mod_b", Code, null, (_, _) => { });
        var overlap = Assert.Single(Hooks.Overlaps(), o => o.Name == Double);
        Assert.Equal((true, HookOrder.Normal), (overlap.IsScript, overlap.Order));
        Assert.Equal(new[] { "mod_a", "mod_b" }, overlap.Mods);
        Assert.Contains("mod_a and mod_b both hook", overlap.Describe(mod => mod));
        Assert.DoesNotContain(Hooks.Overlaps(), o => o.Name == Code);
        // (Moved apart: no overlap.)
        Hooks.RemoveMod("mod_b");
        Assert.DoesNotContain(Hooks.Overlaps(), o => o.Name == Double);
    }
}
