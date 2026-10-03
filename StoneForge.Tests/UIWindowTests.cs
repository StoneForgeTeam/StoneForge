using StoneForge;

// UIWindow's frame: the Settings menu's sprite by default, or another (FrameSprite), with the page, the bottom
// row and the close button following its size.
public class UIWindowTests : FakeGame
{
    // (Sprites only these tests use: Draw keeps sprites' sizes once read.)
    private const int Bigger = 9001, NotASprite = 9002;
    private static readonly int Settings = (int)Sprite.s_settings_menu;
    private readonly ModContext _context = new("window_test");

    public UIWindowTests()
    {
        SpriteSizes[Settings] = (520, 330);
        SpriteSizes[Bigger] = (620, 380);
    }

    [Fact]
    public void A_window_is_the_Settings_menus_frame_by_default()
    {
        var window = Open(new UIWindow("Test"));
        Assert.Equal((520.0, 330.0), (window.Frame.Width, window.Frame.Height));
        Assert.Equal((310.0 + 15, 247.0), (window.Page.Width, window.Page.Height));
        Assert.Equal(479.0, CloseButton(window).X);
    }

    [Fact]
    public void Another_frame_sprite_sizes_the_window_and_moves_its_edges_with_it()
    {
        var window = Open(new UIWindow("Test") { FrameSprite = Bigger });
        Assert.Equal((620.0, 380.0), (window.Frame.Width, window.Frame.Height));
        // (100 wider and 50 taller than the Settings menu's.)
        Assert.Equal((410.0 + 15, 297.0), (window.Page.Width, window.Page.Height));
        Assert.Equal(579.0, CloseButton(window).X);
        Assert.Equal(window.AddButton(0, "Left").X + 337 + 100, window.AddButton(3, "Right").X);
        Assert.Equal(window.Page.Y + 297 + 270 - 247 - 13, window.AddButton(1, "Middle").Y);
    }

    [Fact]
    public void A_frame_sprite_that_isnt_one_falls_back_to_the_Settings_menus()
    {
        var window = Open(new UIWindow("Test") { FrameSprite = NotASprite });
        Assert.Equal((520.0, 330.0), (window.Frame.Width, window.Frame.Height));
    }

    private UIWindow Open(UIWindow window)
    {
        _context.UI.Always.Add(window).Open();
        return window;
    }

    private static UIElement CloseButton(UIWindow window) => window.Frame.Children.Single(c => c.GetType().Name == "WindowCloseButton");

    public override void Dispose()
    {
        _context.UI.Always.Clear();
        base.Dispose();
    }
}
