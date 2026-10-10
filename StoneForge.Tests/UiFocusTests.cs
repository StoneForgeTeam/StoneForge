using StoneForge;

public sealed class UiFocusTests : FakeGame
{
    private readonly ModContext _context = new("ui_focus_test");
    private sealed class Counter : UIElement
    {
        public int Draws;
        public Counter() { Width = Height = 40; }
        protected override void OnDraw(double x, double y) => Draws++;
    }
    public UiFocusTests()
    {
        Input = new FakeInput(); KeepGlobalWrites = true;
        Globals["window_ratio"] = 1; Globals["cameraScale"] = 2; Draw.UpdateScale();
    }
    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id); UITextBox.ReleaseFocus();
        Input!.Focused = false; InputBlock.Flush(); InputBlock.Flush();
        base.Dispose();
    }

    [Fact]
    public void Gui_keeps_drawing_when_unfocused_but_skips_empty_viewports()
    {
        var counter = _context.UI.Always.Add(new Counter());
        Input!.Focused = false;
        Hooks.DrawGui(); Assert.Equal(1, counter.Draws);
        Input.WindowWidth = 0;
        Hooks.DrawGui(); Assert.Equal(1, counter.Draws);
        Input.WindowWidth = 1280; Input.Focused = true;
        Hooks.DrawGui(); Assert.Equal(2, counter.Draws);
    }
    [Fact]
    public void Alt_tab_cancels_a_pending_click_and_keyboard_focus_without_hiding_the_screen()
    {
        var screen = _context.UI.Always;
        var counter = screen.Add(new Counter());
        var text = screen.Add(new UITextBox { X = 100, Text = "Keep this draft" });
        int clicks = 0; counter.Clicked += _ => clicks++;
        Input!.MouseX = Input.MouseY = 20; Input.Pressed = Input.Down = true;
        screen.Frame(); Assert.True(counter.IsPressed);
        text.Focus();
        Input.Focused = false; Input.Pressed = Input.Down = false;
        Keyboard.Typed = "Typing in another app";
        screen.Frame();
        Assert.True(screen.IsActive); Assert.Equal(2, counter.Draws);
        Assert.False(counter.IsPressed); Assert.False(text.IsFocused);
        Assert.Equal("Keep this draft", text.Text); Assert.Equal(0, clicks);
        Input.Focused = true; screen.Frame();
        Assert.Equal(3, counter.Draws); Assert.Equal(0, clicks);
    }
}
