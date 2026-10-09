using StoneForge;

public class GuiResolutionTests : FakeGame
{
    public GuiResolutionTests()
    {
        Input = new FakeInput();
        Globals["window_ratio"] = 1;
        Globals["cameraScale"] = 2;
        Globals["cameraWidth"] = 640;
        Globals["cameraHeight"] = 360;
        Globals["gameframe_offset_left"] = 0;
        Globals["gameframe_offset_top"] = 0;
        Globals["window_offset_x"] = 0;
        Globals["window_offset_y"] = 0;
        Globals["resolution"] = "1280x720";
        Draw.UpdateScale();
    }

    [Theory]
    [InlineData(1280, 720, 1)]
    [InlineData(1920, 1080, 1.5)]
    [InlineData(2560, 1440, 2)]
    public void Fullscreen_720p_view_and_mouse_share_the_same_units(double width, double height, double ratio)
    {
        Input!.WindowWidth = width; Input.WindowHeight = height;
        Globals["window_ratio"] = ratio;
        Draw.UpdateScale();
        Assert.Equal(640, Draw.Width);
        Assert.Equal(360, Draw.Height);
        Input.MouseX = width / 2; Input.MouseY = height / 2;
        Assert.Equal(new Point(320, 180), Mouse.Position);
    }

    private sealed class Window : UIWindow
    {
        public int Builds, Fits;
        public UIElement Marker = new UIGroup();
        public Window() { FrameWidth = 400; FrameHeight = 300; }
        protected override void OnBuild() { Builds++; Content.Add(Marker); }
        protected override void OnFit() { Fits++; }
    }

    [Fact]
    public void Open_window_refits_after_resolution_change_without_rebuilding_its_controls()
    {
        var context = new ModContext("resolution_test");
        var window = context.UI.Always.Add(new Window());
        try
        {
            window.Open();
            Assert.Equal(120, window.Frame.X);
            Input!.WindowWidth = 1920; Input.WindowHeight = 1080;
            Globals["cameraWidth"] = 960; Globals["cameraHeight"] = 540;
            Globals["resolution"] = "1920x1080";
            Draw.UpdateScale();
            window.RunUpdate(0);
            Assert.Equal(280, window.Frame.X);
            Assert.Equal(120, window.Frame.Y);
            Assert.Equal(960, window.Width);
            Assert.Equal(540, window.Height);
            Assert.Equal(1, window.Builds);
            Assert.Equal(2, window.Fits);
            Assert.Contains(window.Marker, window.Content.Children);
        }
        finally { window.Close(); Hooks.RemoveMod(context.Id); }
    }
}
