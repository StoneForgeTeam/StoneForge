using StoneForge;

// Script hooks: Before, After (the call made by the loader, its result seen and changed), and CallOriginal skipping
// the hooks for its own call only - not for the calls it makes (laid out with FakeGame's scripts, whose hook block
// calls in as the patcher's does).
public class ScriptHookTests : FakeGame
{
    private const string Double = "scr_sf_test_double", Factorial = "scr_sf_test_factorial", Plain = "scr_sf_test_plain";
    private readonly FakeScripts _scripts = new();

    public ScriptHookTests()
    {
        GameScripts = _scripts;
        _scripts.Add(Double, args => args[0].AsReal * 2);
        _scripts.Add(Factorial, args => args[0].AsReal <= 1 ? 1 : args[0].AsReal * Game.CallScript(Factorial, default, args[0].AsReal - 1).AsReal);
        _scripts.Add(Plain, args => 7);
        _scripts.Unhooked.Add(Plain);
    }

    public override void Dispose()
    {
        Hooks.RemoveMod("testmod");
        Hooks.RemoveMod("othermod");
        base.Dispose();
    }

    private static double Call(string name, double arg) => Game.CallScript(name, default, arg).AsReal;

    [Fact]
    public void After_sees_the_result_and_can_change_it()
    {
        var seen = new List<double>();
        Hooks.AddScript("testmod", Double, null, call =>
        {
            seen.Add(call.Result.AsReal);
            call.Result = call.Result + 1;
        });
        Assert.Equal(11, Call(Double, 5));
        Assert.Equal(new[] { 10.0 }, seen);
        Assert.Equal(1, _scripts.Runs[Double]);
    }

    [Fact]
    public void After_follows_a_before_that_replaced_the_call()
    {
        Hooks.AddScript("testmod", Double, call =>
        {
            call.Result = 100;
            return true;
        });
        Hooks.AddScript("othermod", Double, null, call => call.Result = call.Result + 1);
        Assert.Equal(101, Call(Double, 5));
        Assert.Equal(0, _scripts.Runs[Double]);
    }

    [Fact]
    public void Several_afters_share_one_call()
    {
        Hooks.AddScript("testmod", Double, null, call => call.Result = call.Result + 1);
        Hooks.AddScript("othermod", Double, null, call => call.Result = call.Result * 10);
        Assert.Equal(110, Call(Double, 5));
        Assert.Equal(1, _scripts.Runs[Double]);
    }

    [Fact]
    public void A_before_that_doesnt_replace_leaves_the_after_its_result()
    {
        var args = new List<double>();
        Hooks.AddScript("testmod", Double, call => { args.Add(call.Args[0].AsReal); return false; });
        Hooks.AddScript("othermod", Double, null, call => call.Result = call.Result + 1);
        Assert.Equal(7, Call(Double, 3));
        Assert.Equal(new[] { 3.0 }, args);
    }

    [Fact]
    public void A_throwing_after_leaves_the_rest()
    {
        Hooks.AddScript("testmod", Double, null, _ => throw new InvalidOperationException("after"));
        Hooks.AddScript("othermod", Double, null, call => call.Result = call.Result + 1);
        Assert.Equal(11, Call(Double, 5));
    }

    [Fact]
    public void After_runs_for_every_level_of_a_recursive_script_its_code_once_each()
    {
        var results = new List<double>();
        Hooks.AddScript("testmod", Factorial, null, call => results.Add(call.Result.AsReal));
        Assert.Equal(24, Call(Factorial, 4));
        Assert.Equal(new[] { 1.0, 2, 6, 24 }, results);
        Assert.Equal(4, _scripts.Runs[Factorial]);
    }

    [Fact]
    public void CallOriginal_skips_the_hooks_for_its_own_call_only()
    {
        var seen = new List<double>();
        Hooks.AddScript("testmod", Factorial, call => { seen.Add(call.Args[0].AsReal); return false; });
        var script = new Script(Factorial, 1);
        Assert.Equal(24, script.CallOriginal(null, 4).AsReal);
        // (Its own call not seen; the ones it makes in turn are.)
        Assert.Equal(new[] { 3.0, 2, 1 }, seen);
    }

    [Fact]
    public void CallOriginal_inside_a_before_runs_the_code_once()
    {
        var script = new Script(Double, 1);
        Hooks.AddScript("testmod", Double, call =>
        {
            call.Result = script.CallOriginal(call) + 1;
            return true;
        });
        Assert.Equal(11, Call(Double, 5));
        Assert.Equal(1, _scripts.Runs[Double]);
    }

    [Fact]
    public void A_pass_through_not_used_is_let_go_of()
    {
        // (A script with no hook block in the game data never calls in: the next hooked call is hooked all the same.)
        Hooks.AddScript("testmod", Double, null, call => call.Result = call.Result + 1);
        Assert.Equal(7, Hooks.CallOriginal(Plain, default, default, Array.Empty<GmValue>()).AsReal);
        Assert.Equal(11, Call(Double, 5));
    }

    [Fact]
    public void The_call_for_after_handlers_keeps_self_and_other()
    {
        Hooks.AddScript("testmod", Double, null, _ => { });
        // (As inside a game callback, where the game's instances are handed over by pointer.)
        using var lease = new CallbackLifetime();
        var (self, other) = (new Instance((IntPtr)0x1000), new Instance((IntPtr)0x2000));
        Assert.True(Hooks.ScriptCalled(Double, self, other, new GmValue[] { 4 }, out GmValue result));
        Assert.Equal(8, result.AsReal);
        Assert.Equal(((IntPtr)0x1000, (IntPtr)0x2000), (_scripts.LastSelf, _scripts.LastOther));
    }

    [Fact]
    public void A_hook_needs_a_handler()
        => Assert.Throws<ArgumentException>(() => Hooks.AddScript("testmod", Double, null, null));
}
