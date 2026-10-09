using StoneForge.Loader;

public class RuntimeLoadBatchTests
{
    [Fact]
    public void Enable_all_presents_each_mod_before_loading_and_preserves_reload_order()
    {
        var batch = new RuntimeLoadBatch(new[] { ("a", false), ("a", true), ("b", true) }, id => "Mod " + id);
        var calls = new List<(string, bool)>();
        void Step() => batch.Step((id, on) => calls.Add((id, on)), _ => true, (_, e) => throw e);
        Assert.Equal(2, batch.Progress.Total);
        Assert.Equal("Mod a", batch.Progress.Current);
        Step(); Assert.Empty(calls);
        batch.Drawn(); Step(); Assert.Empty(calls);
        batch.Drawn(); Step();
        Assert.Equal(new[] { ("a", false), ("a", true) }, calls);
        Assert.Equal("Mod b", batch.Progress.Current);
        Assert.Equal(1, batch.Progress.Done);
        Step(); Assert.Equal(2, calls.Count);
        batch.Drawn(); Step(); Assert.Equal(2, calls.Count);
        batch.Drawn(); Step();
        Assert.Equal(("b", true), calls.Last());
        Assert.True(batch.Progress.Finished);
        Assert.Equal(2, batch.Progress.Loaded);
    }

    [Fact]
    public void Failed_enable_does_not_stop_the_batch_and_trailing_disables_still_run()
    {
        var batch = new RuntimeLoadBatch(new[] { ("broken", true), ("good", true), ("off", false) }, id => id);
        var calls = new List<string>();
        var failures = new List<string>();
        void Step() => batch.Step((id, _) => { calls.Add(id); if (id == "broken") throw new Exception("Load failed"); },
            id => id == "good", (id, _) => failures.Add(id));
        batch.Drawn(); batch.Drawn(); Step();
        Assert.Equal("good", batch.Progress.Current);
        Assert.Equal(1, batch.Progress.Failed);
        batch.Drawn(); batch.Drawn(); Step();
        Assert.Equal(new[] { "broken", "good", "off" }, calls);
        Assert.Equal(new[] { "broken" }, failures);
        Assert.True(batch.Progress.Finished);
        Assert.Equal(1, batch.Progress.Loaded);
        Assert.Equal(2, batch.Progress.Done);
    }

    [Fact]
    public void Unsuccessful_load_without_an_exception_is_reported_as_failed()
    {
        var batch = new RuntimeLoadBatch(new[] { ("bad", true) }, id => id);
        batch.Drawn(); batch.Drawn();
        batch.Step((_, _) => { }, _ => false, (_, e) => throw e);
        Assert.True(batch.Progress.Finished);
        Assert.Equal(1, batch.Progress.Failed);
        Assert.Equal(0, batch.Progress.Loaded);
    }

    [Fact]
    public void Restarting_progress_clears_a_previous_result()
    {
        var progress = new StartupProgress();
        progress.Begin(2); progress.Done = 2; progress.Failed = 1;
        progress.Finish(1, 1);
        progress.Begin(3);
        Assert.False(progress.Finished);
        Assert.Equal(3, progress.Total);
        Assert.Equal(0, progress.Done);
        Assert.Equal(0, progress.Failed);
        Assert.Equal(0, progress.Loaded);
        Assert.Null(progress.Current);
    }
}
