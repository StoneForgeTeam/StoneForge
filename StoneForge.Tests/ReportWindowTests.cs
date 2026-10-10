using StoneForge;
using StoneForge.Loader;

[Collection("MainMenu")]
public sealed class ReportWindowTests : FakeGame
{
    private readonly ModContext _context = new("report_ui_test");
    private sealed class Mod : IStoneMod { public void Load(ModContext context) { } public void Unload() { } }
    public ReportWindowTests()
    {
        EscMenu.ResetForTests();
        Input = new FakeInput(); KeepGlobalWrites = true;
        Globals["window_ratio"] = 1; Globals["cameraScale"] = 2;
        Globals["cameraWidth"] = 640; Globals["cameraHeight"] = 360;
        Globals["gameframe_offset_left"] = Globals["gameframe_offset_top"] = 0;
        Globals["window_offset_x"] = Globals["window_offset_y"] = 0;
        Globals["resolution"] = "1280x720"; Draw.UpdateScale();
    }
    public override void Dispose()
    {
        foreach (string id in new[] { "report_linked", "report_disabled", "report_unlinked", "report_notloaded" })
        { ModList.RemoveMod(id); ModRegistry.All.RemoveAll(m => m.Id == id); }
        UIWindow.ShutMod(_context.Id); Hooks.RemoveMod(_context.Id); UITextBox.ReleaseFocus(); EscMenu.ResetForTests(); base.Dispose();
    }
    [Fact]
    public void Reporter_does_not_schedule_escape_menu_lookup_before_startup_finishes()
    {
        bool ready = false;
        EscMenu.Install(_context);
        ReportWindow.InstallWhenReady(_context, () => ready);
        void Frame()
        {
            foreach (var handler in Hooks.FrameHandlers.Where(h => h.Mod == _context.Id).ToArray()) handler.Handler();
        }
        Calls.Clear();
        for (int i = 0; i < 4; i++) Frame();
        Assert.DoesNotContain(Calls, c => c.Contains("instance_find"));
        ready = true; Frame();
        Assert.Contains(Localization.Get("report.button"), EscMenu.Describe());
        Assert.Equal(1, EscMenu.Describe().Count(text => text == Localization.Get("report.button")));
        Frame();
        Assert.Equal(1, EscMenu.Describe().Count(text => text == Localization.Get("report.button")));
    }
    private void Add(string id, bool enabled, bool linked, bool loaded)
    {
        ModRegistry.All.Add(new(id, id, "", "", "1", enabled, ""));
        if (loaded) ModList.Add(new ModManifest(new ManifestData(id, id, "1", "", "", null,
            Github: linked ? "Owner/" + id : null)), new Mod());
    }
    [Fact]
    public void Selector_only_lists_running_enabled_linked_mods_and_stoneforge()
    {
        Add("report_linked", true, true, true); Add("report_disabled", false, true, true);
        Add("report_unlinked", true, false, true); Add("report_notloaded", true, true, false);
        var targets = BugReporter.Targets(_context);
        Assert.Contains(targets, t => t.Id == Hooks.LoaderId);
        Assert.Contains(targets, t => t.Id == "report_linked");
        Assert.DoesNotContain(targets, t => t.Id is "report_disabled" or "report_unlinked" or "report_notloaded");
    }
    [Fact]
    public void Draft_and_labels_survive_reopening_and_resolution_refit()
    {
        var window = _context.UI.InGame.Add(new ReportWindow(_context)); window.Open();
        var title = window.Content.Children.OfType<UITextBox>().First(t => !t.Multiline);
        var description = window.Content.Children.OfType<UITextBox>().Single(t => t.Multiline);
        title.Text = "Testing title"; description.Text = "Testing description\nwith multiple lines.";
        window.Close(); window.Open();
        title = window.Content.Children.OfType<UITextBox>().First(t => !t.Multiline);
        description = window.Content.Children.OfType<UITextBox>().Single(t => t.Multiline);
        Assert.Equal("Testing title", title.Text); Assert.Contains("\n", description.Text);
        Assert.All(window.Content.Children.OfType<UIButton>(), button => Assert.NotEmpty(button.Text));
        Input!.WindowWidth = 1920; Input.WindowHeight = 1080;
        Globals["window_ratio"] = 1.5; Globals["resolution"] = "1920x1080"; Draw.UpdateScale();
        window.RunUpdate(0);
        Assert.Same(description, window.Content.Children.OfType<UITextBox>().Single(t => t.Multiline));
        Assert.Equal("Testing title", title.Text);
        Assert.Equal(100, window.Frame.X); Assert.Equal(10, window.Frame.Y);
        Assert.True(description.Y + description.Height < window.Content.Children.OfType<UICheckbox>().Single().Y);
    }
    [Fact]
    public void Recent_log_snapshot_is_bounded_and_has_levels_and_timestamps()
    {
        for (int i = 0; i < 110; i++) Game.Log(i == 109 ? "warning: " + new string('x', 700) : "line " + i);
        var logs = ReportLogs.Snapshot(); Assert.Equal(100, logs.Length);
        Assert.Equal("warn", logs[^1].Level); Assert.Equal(512, logs[^1].Message.Length);
        Assert.True(logs[^1].Timestamp > DateTimeOffset.UtcNow.AddMinutes(-1));
    }
}
