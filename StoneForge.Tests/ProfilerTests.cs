using StoneForge;

// The profiler: mod handlers timed through Hooks.Invoke and a mod's own parts (Profiler.Measure), averaged per frame,
// only while it's on. (Its window set to nothing: each frame publishes.)
public class ProfilerTests : FakeGame
{
    private readonly ModContext _context = new("profiler_test");

    public ProfilerTests()
    {
        Profiler.WindowSeconds = 0;
        Profiler.Visible = true;
        // (A frame to start from: what earlier tests left is published and gone.)
        Profiler.NewFrame();
    }

    public override void Dispose()
    {
        Profiler.Visible = false;
        Profiler.WindowSeconds = 1;
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    private static Profiler.Timing Find(string mod, string name) => Profiler.Timings.Single(t => t.Mod == mod && t.Name == name);

    [Fact]
    public void A_handler_and_a_mods_own_part_are_timed_per_frame()
    {
        Hooks.Invoke("profiler_test", "Tick", () => { Thread.Sleep(2); return false; });
        Profiler.Measure(_context, "loot sync", () => Thread.Sleep(1));
        Profiler.Measure(_context, "loot sync", () => Thread.Sleep(1));
        Profiler.NewFrame();
        var tick = Find("profiler_test", "Tick");
        Assert.False(tick.Section);
        Assert.True(tick.Average >= 1.5, $"{tick.Average}");
        Assert.Equal(1, tick.CallsPerFrame);
        var part = Find("profiler_test", "loot sync");
        Assert.True(part.Section);
        Assert.Equal(2, part.CallsPerFrame);
        Assert.True(part.Worst >= part.Average);
        Assert.True(Profiler.Fps > 0);
    }

    [Fact]
    public void Measure_gives_back_what_the_work_does()
        => Assert.Equal(42, Profiler.Measure(_context, "answer", () => 42));

    [Fact]
    public void Nothing_is_timed_while_its_off()
    {
        Profiler.Visible = false;
        bool ran = false;
        Hooks.Invoke("profiler_test", "Tick", () => false);
        Profiler.Measure(_context, "part", () => ran = true);
        Profiler.Visible = true;
        Profiler.NewFrame();
        Assert.True(ran);
        Assert.DoesNotContain(Profiler.Timings, t => t.Mod == "profiler_test");
    }

    [Fact]
    public void A_handler_that_throws_is_timed_all_the_same()
    {
        Hooks.Invoke("profiler_test", "Frame", () => throw new InvalidOperationException("boom"));
        Profiler.NewFrame();
        Assert.Equal(1, Find("profiler_test", "Frame").CallsPerFrame);
    }
}
