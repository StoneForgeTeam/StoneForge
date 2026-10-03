namespace StoneForge;

/// <summary>A window as the game's Settings menu - its frame, title and close button, over the screen dimmed -
/// with tabs down its left (<see cref="SetTabs(string[])"/>: more than fit scroll), a scrolling page on its
/// right (<see cref="Page"/>) and up to four buttons along its bottom (<see cref="AddButton"/>). While it's
/// open nothing under it takes the mouse or the game's hotkeys, and Escape closes it, as the game's own
/// windows. Put it on a screen, then <see cref="Open"/> it; inherit from it, building it in
/// <see cref="OnOpen"/> and filling its page in <see cref="OnTabOpened"/>:
/// <code>
/// public class MyWindow : UIWindow
/// {
///     public MyWindow() : base("My Mod") { }
///     protected override void OnOpen()
///     {
///         SetTabs("General", "Advanced");
///         AddCloseButton();
///         Tabs[0].Open();
///     }
///     protected override void OnTabOpened(UITab tab)
///     {
///         Page.Clear();
///         Page.AddHeader(tab.Text);
///         Page.AddCheckbox("Something", true);
///     }
/// }
/// var window = context.UI.MainMenu.Add(new MyWindow());
/// MainMenu.AddButton(context, "My Mod", window.Open);
/// </code>
/// Each <see cref="Open"/> builds it anew. Anything can go on its page (it's a <see cref="UIScrollArea"/>) or
/// its <see cref="Frame"/>.</summary>
public class UIWindow : UIElement
{
    // The settings menu's layout, from its content's corner in the frame: its tab column, page and bottom row
    // (the buttons' middles, as the game places them).
    private const double PageX = 110, PageWidth = 310, AreaHeight = 247, TabWidth = 100, TabHeight = 26, ButtonsY = 270;
    private static readonly double[] ButtonColumns = { 50, 183, 285, 387 };

    // Open windows; and each one's stand-in in the game's escape list (o_stonemod_modal), by its id.
    private static readonly List<UIWindow> OpenWindows = new();
    private static readonly Dictionary<int, UIWindow> ByModal = new();

    private readonly List<UITab> _tabs = new();
    private UIScrollArea? _tabArea;
    private UIScrollArea? _page;
    private Instance _modal;
    private double _offsetX, _offsetY;

    public UIWindow(string title)
    {
        Title = title;
        Visible = false;
        Frame = Add(new WindowFrame(this));
    }

    public string Title { get; set; }
    public bool IsOpen => Visible;
    /// <summary>The window itself - its frame, at the middle of the screen: what's on it is placed from its corner.</summary>
    public UIElement Frame { get; }
    /// <summary>The right side: a scrolling page to fill (while it's open).</summary>
    public UIScrollArea Page => _page ?? throw new InvalidOperationException("The window isn't open: fill it in OnOpen / OnTabOpened");
    public IReadOnlyList<UITab> Tabs => _tabs;
    /// <summary>The open tab (none before one's opened).</summary>
    public UITab? SelectedTab { get; private set; }

    /// <summary>It opened (after <see cref="OnOpen"/>).</summary>
    public event Action<UIWindow>? Opened;
    /// <summary>A tab was opened (after <see cref="OnTabOpened"/>).</summary>
    public event Action<UITab>? TabOpened;
    /// <summary>It closed (whoever closed it; after <see cref="OnClosed"/>).</summary>
    public event Action<UIWindow>? Closed;

    /// <summary>It's just been opened: build it here - tabs, buttons, the page.</summary>
    protected virtual void OnOpen() { }
    /// <summary>A tab was opened (clicked, or <see cref="UITab.Open"/>): fill the page for it here.</summary>
    protected virtual void OnTabOpened(UITab tab) { }
    /// <summary>It closed.</summary>
    protected virtual void OnClosed() { }

    /// <summary>Opens it (it must be on a screen: <c>context.UI.MainMenu.Add(window)</c>), built anew
    /// (<see cref="OnOpen"/>). Already open: nothing happens.</summary>
    public void Open()
    {
        if (IsOpen)
            return;
        if (Screen == null)
            throw new InvalidOperationException("Put the window on a screen first: context.UI.MainMenu.Add(window)");
        Build();
        Visible = true;
        OpenWindows.Add(this);
        TakeOver();
        UISounds.Play(Sound.snd_ui_menu_settings_big_window_st, 4);
        try { OnOpen(); }
        catch (Exception e) { Game.Log($"{GetType().Name}.OnOpen threw: {e}"); }
        Raise(Opened);
    }

    /// <summary>Closes it, with the game's sound.</summary>
    public void Close()
    {
        if (!IsOpen)
            return;
        UISounds.Play(Sound.snd_ui_menu_push_down_button_release_close_window, 4);
        Shut();
    }

    /// <summary>The tabs down its left, in place of any it had - more than fit (9) scroll, beside a scrollbar.
    /// Open one with <see cref="UITab.Open"/>.</summary>
    public IReadOnlyList<UITab> SetTabs(params string[] names) => SetTabs((IEnumerable<string>)names);

    public IReadOnlyList<UITab> SetTabs(IEnumerable<string> names)
    {
        var list = names.ToList();
        foreach (var tab in _tabs)
            tab.Parent?.Remove(tab);
        _tabs.Clear();
        SelectedTab = null;
        if (_tabArea != null)
            Frame.Remove(_tabArea);
        _tabArea = null;
        if (list.Count * TabHeight > AreaHeight)
        {
            // (The column the tabs have - 0 to 100 - with the scrollbar's track at its right, clear of the page's
            // frame at about 106; the tabs narrowed for it.)
            _tabArea = Frame.Add(new UIScrollArea(_offsetX, _offsetY, 88 + 15, AreaHeight) { Padding = 0, Spacing = 0 });
            for (int i = 0; i < list.Count; i++)
                _tabs.Add(_tabArea.Add(new UITab(this, list[i], i, 86)));
        }
        else
        {
            for (int i = 0; i < list.Count; i++)
                _tabs.Add(Frame.Add(new UITab(this, list[i], i, TabWidth) { X = _offsetX, Y = _offsetY + TabHeight * i }));
        }
        return _tabs;
    }

    /// <summary>A button (the Settings menu's) in the bottom row, in one of its four places: 0 left ... 3
    /// right, where the settings menu has Cancel.</summary>
    public UIButton AddButton(int place, string text)
    {
        if (place < 0 || place >= ButtonColumns.Length)
            throw new ArgumentOutOfRangeException(nameof(place), "0 to 3");
        return Frame.Add(new UIButton(text, _offsetX + ButtonColumns[place] - 50, _offsetY + ButtonsY - 13));
    }

    /// <summary>A button that closes the window, in the bottom row (as the settings menu's Cancel).</summary>
    public UIButton AddCloseButton(int place = 3, string text = "Close")
    {
        var button = AddButton(place, text);
        button.Clicked += _ => Close();
        return button;
    }

    // A tab opened: shown as the open one, scrolled to - in the middle where it can be - if its list scrolls;
    // then OnTabOpened and TabOpened.
    internal void Select(UITab tab)
    {
        SelectedTab = tab;
        _tabArea?.ScrollToShow(tab);
        try { OnTabOpened(tab); }
        catch (Exception e) { Game.Log($"{GetType().Name}.OnTabOpened threw: {e}"); }
        try { TabOpened?.Invoke(tab); }
        catch (Exception e) { Game.Log($"{GetType().Name} TabOpened handler threw: {e}"); }
    }

    // Made anew for each opening: the frame emptied but for its close button, the page.
    private void Build()
    {
        Frame.Clear();
        _tabs.Clear();
        _tabArea = null;
        SelectedTab = null;
        var frame = (WindowFrame)Frame;
        frame.Fit();
        (_offsetX, _offsetY) = frame.ContentOffset;
        _page = Frame.Add(new UIScrollArea(_offsetX + PageX, _offsetY, PageWidth + 15, AreaHeight));
        Frame.Add(frame.MakeCloseButton());
    }

    protected override void OnUpdate(double deltaTime)
    {
        // (Over the whole screen: the dimming, and nothing under it clicked.)
        Width = Draw.Width;
        Height = Draw.Height;
        // The main menu kept waiting - its list may have been made since it opened (the main menu's room comes
        // a few seconds before its list).
        if (IsOpen && Gm.InstanceExists(GameObjectId.o_mainMenuNavContainer))
            foreach (var nav in Instances.All<GameInstance>(GameObjectId.o_mainMenuNavContainer))
                if (nav.Instance.Get("active").AsBool)
                    nav.Instance.Set("active", false);
    }

    protected override void OnDraw(double x, double y)
        => Scripts.scr_drawBG.Call(null, x, y, Width, Height, Draw.Black, 0.7);

    // ---- the game, while it's open ----

    // As the game's windows: the main menu waits, Escape comes to it (its stand-in in the escape list), the
    // game's hotkeys are held off (InputBlock).
    private void TakeOver()
    {
        try
        {
            _modal = Game.CallBuiltinTrusted("instance_create_depth", default, default, 0, 0, 0, Gm.AssetGetIndex("o_stonemod_modal")).AsInstance;
            if (!_modal.IsNone)
            {
                ByModal[Id(_modal)] = this;
                Game.CallScript("scr_escapeButtonListAdd", _modal, _modal);
            }
            foreach (var nav in Instances.All<GameInstance>(GameObjectId.o_mainMenuNavContainer))
                nav.Instance.Set("active", false);
        }
        catch (Exception e) { Game.Log($"{GetType().Name}: couldn't take the game's input: {e.Message}"); }
    }

    // Closed (also when its screen leaves its context, or its mod's switched off): the game given back.
    internal void Shut()
    {
        if (!IsOpen)
            return;
        Visible = false;
        OpenWindows.Remove(this);
        _page = null;
        try
        {
            if (!_modal.IsNone)
            {
                ByModal.Remove(Id(_modal));
                Game.CallScript("scr_escapeButtonListRemove", _modal, _modal);
                Game.CallBuiltinTrusted("instance_destroy", _modal, _modal);
                _modal = default;
            }
            if (OpenWindows.Count == 0)
            {
                foreach (var nav in Instances.All<GameInstance>(GameObjectId.o_mainMenuNavContainer))
                {
                    nav.Instance.Set("active", true);
                    // Mods switched on or off took buttons off the main menu or put them on: its list is made again.
                    if (Game.Global["stonemod_menu_dirty"].AsBool)
                    {
                        Game.Global["stonemod_menu_dirty"] = false;
                        Game.CallBuiltinAs("event_user", nav.Instance, nav.Instance, 0);
                    }
                }
                // (The click that closed it isn't one for the game's buttons under it.)
                foreach (var button in Instances.All<GameInstance>(GameObjectId.o_button))
                    Game.CallBuiltinTrusted("alarm_set", button.Instance, button.Instance, 1, 5);
            }
        }
        catch (Exception e) { Game.Log($"{GetType().Name}: couldn't give the game's input back: {e.Message}"); }
        try { OnClosed(); }
        catch (Exception e) { Game.Log($"{GetType().Name}.OnClosed threw: {e}"); }
        Raise(Closed);
    }

    private void Raise(Action<UIWindow>? handlers)
    {
        try { handlers?.Invoke(this); }
        catch (Exception e) { Game.Log($"{GetType().Name} handler threw: {e}"); }
    }

    private static int Id(Instance instance) => instance.Pointer == IntPtr.Zero ? instance.Id : instance.Get("id").AsInstance.Id;

    // ---- the loader ----

    // Whether a window is open (the game's hotkeys are held off meanwhile); the one on top (the last opened);
    // all of them, in the order they opened (drawn so, over every screen - UIScreen.DrawWindows).
    internal static bool AnyOpen => OpenWindows.Count > 0;
    internal static UIWindow? Top => OpenWindows.Count > 0 ? OpenWindows[^1] : null;
    internal static IReadOnlyList<UIWindow> InOrder => OpenWindows.ToList();
    internal UIScreen? ScreenOf => Screen;

    // Escape: its stand-in tells it (scr_stonemod_gui_event "close").
    internal static void Install(ModContext loader)
    {
        loader.OnScript("scr_stonemod_gui_event", call =>
        {
            if (call.Args.Length >= 1 && call.Args[0].AsString == "close")
            {
                GmValue id = call.Self.Get("id");
                int key = id.Kind == GmKind.Instance ? id.AsInstance.Id : (int)id.AsReal;
                if (ByModal.TryGetValue(key, out var window))
                    window.Close();
            }
            return true;
        });
    }

    // Windows on a screen leaving its context, or of a mod switched off: closed at once.
    internal static void ShutOn(UIScreen screen)
    {
        foreach (var window in OpenWindows.Where(w => w.Screen == screen).ToList())
            window.Shut();
    }

    internal static void ShutMod(string mod)
    {
        foreach (var window in OpenWindows.Where(w => w.Screen?.Owner == mod).ToList())
            window.Shut();
    }
}
