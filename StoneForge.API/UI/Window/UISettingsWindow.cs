namespace StoneForge;

/// <summary>A <see cref="UIWindow"/> laid out as the game's Settings menu, from the generic pieces: its frame
/// (s_settings_menu), tabs down its left (<see cref="Tabs"/>, a <see cref="UITabStrip"/>), a scrolling page on its right
/// (<see cref="Page"/>) and a row of buttons along its bottom (<see cref="Buttons"/>, any number):
/// <code>
/// public class MyWindow : UISettingsWindow
/// {
///     public MyWindow() : base("My Mod") { }
///     protected override void OnOpen()
///     {
///         SetTabs("General", "Advanced");
///         AddButton("Reset").Clicked += _ => Reset();
///         AddCloseButton();
///         Tabs.Tabs[0].Open();
///     }
///     protected override void OnTabOpened(UITab tab)
///     {
///         Page.Clear();
///         Page.AddHeader(tab.Text);
///         Page.AddCheckbox("Something", true);
///     }
/// }
/// </code></summary>
public class UISettingsWindow : UIWindow
{
    // The Settings menu's layout, from its content's corner: the tab column, the page, the bottom row (as the game
    // places its buttons: their middles 270 down, the last 387 across).
    private const double TabWidth = 100, PageX = 110, PageWidth = 325, AreaHeight = 247, ButtonsY = 257, LayoutWidth = 437, LayoutHeight = 283;
    // Its frame has places for four buttons drawn along its bottom (two plates, two buttons each): their left edges.
    private static readonly double[] ButtonPlaces = { 0, 133, 235, 337 };

    private UITabStrip? _tabs;
    private UIScrollArea? _page;
    private UIButtonRow? _buttons;

    public UISettingsWindow(string title) : base(title)
    {
        FrameSprite = (int)Sprite.s_settings_menu;
        // (Its layout goes with it - whether or not a subclass's OnClosed calls this one's.)
        Closed += _ => (_tabs, _page, _buttons) = (null, null, null);
    }

    /// <summary>The tabs down its left (while it's open).</summary>
    public UITabStrip Tabs => _tabs ?? throw NotOpen();
    /// <summary>The page on its right: a scrolling column to fill (while it's open).</summary>
    public UIScrollArea Page => _page ?? throw NotOpen();
    /// <summary>The buttons along its bottom (while it's open).</summary>
    public UIButtonRow Buttons => _buttons ?? throw NotOpen();
    /// <summary>The open tab (none before one's opened).</summary>
    public UITab? SelectedTab => _tabs?.Selected;

    /// <summary>A tab was opened (after <see cref="OnTabOpened"/>).</summary>
    public event Action<UITab>? TabOpened;

    /// <summary>A tab was opened (clicked, or <see cref="UITab.Open"/>): fill the page for it here.</summary>
    protected virtual void OnTabOpened(UITab tab) { }

    /// <summary>The tabs down its left, in place of any it had - more than fit scroll.</summary>
    public IReadOnlyList<UITab> SetTabs(params string[] names) => Tabs.SetTabs(names);

    public IReadOnlyList<UITab> SetTabs(IEnumerable<string> names) => Tabs.SetTabs(names);

    /// <summary>A button (the game's) in the bottom row: in the places the frame has for them, from the left (more than
    /// four: spread along it).</summary>
    public UIButton AddButton(string text) => Buttons.Add(text);

    /// <summary>A button in the bottom row that closes the window - in its last place, as the Settings menu's Cancel.</summary>
    public UIButton AddCloseButton(string text = "Close")
    {
        var button = AddButton(text);
        Buttons.Pin(button, ButtonPlaces.Length - 1);
        button.Clicked += _ => Close();
        return button;
    }

    // The Settings menu's frame at this resolution: where its content starts, its title, its close button.
    protected override void OnFit()
    {
        int cameraHeight = Game.Global["cameraHeight"].AsInt;
        (double left, double top) = cameraHeight is 360 or 400 ? (27, 27) : (35, 34);
        ContentInsets = new UIInsets(left, top, Math.Max(0, Frame.Width - left - LayoutWidth), Math.Max(0, Frame.Height - top - LayoutHeight));
        bool small = Game.Global["resolution"].AsString is "1280x720" or "1280x800";
        TitleY = small ? 9 : 13;
        // (Where the game puts its close button, from the frame's top right.)
        CloseButton.X = Math.Max(0, Frame.Width - (small ? 472 : 479) - CloseButton.Width);
        CloseButton.Y = small ? 1 : 3;
    }

    protected override void OnBuild()
    {
        _tabs = Content.Add(new UITabStrip(0, 0, TabWidth, AreaHeight));
        _tabs.TabOpened += tab =>
        {
            try { OnTabOpened(tab); }
            catch (Exception e) { Game.Log($"{GetType().Name}.OnTabOpened threw: {e}"); }
            try { TabOpened?.Invoke(tab); }
            catch (Exception e) { Game.Log($"{GetType().Name} TabOpened handler threw: {e}"); }
        };
        _page = Content.Add(new UIScrollArea(PageX, 0, PageWidth, AreaHeight));
        _buttons = Content.Add(new UIButtonRow(0, ButtonsY, LayoutWidth) { Positions = ButtonPlaces });
    }

    private static InvalidOperationException NotOpen()
        => new("The window isn't open: fill it in OnOpen / OnTabOpened");
}
