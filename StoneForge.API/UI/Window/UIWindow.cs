namespace StoneForge;

/// <summary>A window as the game's own: a frame (any sprite - or a plain panel), its title and close button, in the
/// middle of the screen over it dimmed. While it's open nothing under it takes the mouse or the game's hotkeys, and
/// Escape closes it. Put it on a screen, then <see cref="Open"/> it; inherit from it and build it in
/// <see cref="OnOpen"/>, adding what goes in it to <see cref="Content"/> - the frame's inside, less its borders
/// (<see cref="ContentInsets"/>):
/// <code>
/// public class MyWindow : UIWindow
/// {
///     public MyWindow() : base("My Mod")
///     {
///         FrameSprite = (int)Sprite.s_journal_window;
///         ContentInsets = new UIInsets(20, 30, 20, 20);
///     }
///     protected override void OnOpen()
///     {
///         var page = Content.Add(new UIScrollArea(0, 0, Content.Width, Content.Height - 34));
///         page.AddText("Hello");
///         var buttons = Content.Add(new UIButtonRow(0, Content.Height - 26, Content.Width));
///         buttons.Add("Close").Clicked += _ => Close();
///     }
/// }
/// var window = context.UI.MainMenu.Add(new MyWindow());
/// MainMenu.AddButton(context, "My Mod", window.Open);
/// </code>
/// Its size: its sprite's (the game's version of it for the resolution, as its windows pick theirs); or, with
/// <see cref="Slice"/> - or no sprite - <see cref="FrameWidth"/> x <see cref="FrameHeight"/>, the sprite 9-sliced to it.
/// Nothing in it is laid out for it: place things in <see cref="Content"/> yourself, or with the layout elements
/// (<see cref="UIScrollArea"/>, <see cref="UITabStrip"/>, <see cref="UIButtonRow"/>...). <see cref="UISettingsWindow"/>
/// is one laid out as the game's Settings menu. Each <see cref="Open"/> empties <see cref="Content"/> and builds it
/// anew.</summary>
public class UIWindow : UIElement
{
    // Open windows; and each one's stand-in in the game's escape list (o_stonemod_modal), by its id.
    private static readonly List<UIWindow> OpenWindows = new();
    private static readonly Dictionary<int, UIWindow> ByModal = new();

    private Instance _modal;
    private (double Width, double Height, double Left, double Top, double OffsetX, double OffsetY, double Scale, string Resolution) _fit;

    private (double, double, double, double, double, double, double, string) FitState() =>
        (Game.Global["cameraWidth"].AsReal, Game.Global["cameraHeight"].AsReal,
         Game.Global["gameframe_offset_left"].AsReal, Game.Global["gameframe_offset_top"].AsReal,
         Game.Global["window_offset_x"].AsReal, Game.Global["window_offset_y"].AsReal, Draw.Scale,
         Game.Global["resolution"].AsString);

    public UIWindow(string title = "")
    {
        Title = title;
        Visible = false;
        Frame = Add(new WindowFrame(this));
        Content = Frame.Add(new UIGroup());
        CloseButton = Frame.Add(new WindowCloseButton(this) { Anchor = UIAnchor.TopRight, X = 4, Y = 4 });
    }

    public string Title { get; set; }
    public bool IsOpen => Visible;

    /// <summary>Its frame's sprite: any sprite (-1: none - a plain panel, <see cref="FrameWidth"/> x
    /// <see cref="FrameHeight"/>).</summary>
    public int FrameSprite { get; set; } = -1;
    /// <summary>Whether the game's version of <see cref="FrameSprite"/> for its resolution is drawn - &lt;name&gt;_&lt;view
    /// height&gt;, as its windows pick theirs (default: yes; the sprite itself if there's none).</summary>
    public bool AdaptiveSprite { get; set; } = true;
    /// <summary>The frame sprite's borders, to draw it 9-sliced to <see cref="FrameWidth"/> x <see cref="FrameHeight"/>
    /// (null: drawn at its own size, the window its size).</summary>
    public UIInsets? Slice { get; set; }
    /// <summary>Its size when its sprite is sliced, or it has none (default 400 x 300).</summary>
    public double FrameWidth { get; set; } = 400;
    public double FrameHeight { get; set; } = 300;
    /// <summary>The frame's borders: <see cref="Content"/> is the frame less these.</summary>
    public UIInsets ContentInsets { get; set; } = new(16);
    /// <summary>Whether the screen behind it is dimmed (default: yes).</summary>
    public bool DimBackground { get; set; } = true;
    /// <summary>Where its title is drawn: from the middle of the frame's top (default 13 down).</summary>
    public double TitleX { get; set; }
    public double TitleY { get; set; } = 13;

    /// <summary>The window itself - its frame, in the middle of the screen.</summary>
    public UIElement Frame { get; }
    /// <summary>The frame's inside, less <see cref="ContentInsets"/>: what's in the window goes here (sized and emptied
    /// when it opens, before <see cref="OnOpen"/>).</summary>
    public UIElement Content { get; }
    /// <summary>Its close button: the game's, at the frame's top right (<see cref="UIElement.X"/> / <see cref="UIElement.Y"/>
    /// in from that corner). Hide it, or move it, as the frame needs.</summary>
    public UIElement CloseButton { get; }

    /// <summary>It opened (after <see cref="OnOpen"/>).</summary>
    public event Action<UIWindow>? Opened;
    /// <summary>It closed (whoever closed it; after <see cref="OnClosed"/>).</summary>
    public event Action<UIWindow>? Closed;

    /// <summary>Opening, its frame sized for the game's resolution: change what depends on it here - its
    /// <see cref="ContentInsets"/>, <see cref="CloseButton"/>, title - before <see cref="Content"/> is sized.</summary>
    protected virtual void OnFit() { }
    /// <summary>(For a window laid out in advance, as <see cref="UISettingsWindow"/>: its layout made in the emptied
    /// <see cref="Content"/>, before <see cref="OnOpen"/>.)</summary>
    protected virtual void OnBuild() { }
    /// <summary>It's just been opened: build what's in it here, in <see cref="Content"/>.</summary>
    protected virtual void OnOpen() { }
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

    // Made anew for each opening: the frame fitted to the resolution, the content emptied and sized.
    private void Build()
    {
        FitLayout();
        Content.Clear();
        try { OnBuild(); }
        catch (Exception e) { Game.Log($"{GetType().Name}.OnBuild threw: {e}"); }
    }

    // Refit existing controls after a resolution change, preserving page contents and scroll position.
    private void FitLayout()
    {
        var frame = (WindowFrame)Frame;
        frame.Fit();
        try { OnFit(); }
        catch (Exception e) { Game.Log($"{GetType().Name}.OnFit threw: {e}"); }
        var insets = ContentInsets;
        Content.X = insets.Left;
        Content.Y = insets.Top;
        Content.Width = Math.Max(0, Frame.Width - insets.Left - insets.Right);
        Content.Height = Math.Max(0, Frame.Height - insets.Top - insets.Bottom);
        _fit = FitState();
    }

    protected override void OnUpdate(double deltaTime)
    {
        // (Over the whole screen: the dimming, and nothing under it clicked.)
        Width = Draw.Width;
        Height = Draw.Height;
        if (IsOpen && _fit != FitState())
            FitLayout();
        // The main menu kept waiting - its list may have been made since it opened (the main menu's room comes
        // a few seconds before its list).
        if (IsOpen && Gm.InstanceExists(GameObjectId.o_mainMenuNavContainer))
            foreach (var nav in Instances.All<GameInstance>(GameObjectId.o_mainMenuNavContainer))
                if (nav.Instance.Get("active").AsBool)
                    nav.Instance.Set("active", false);
    }

    protected override void OnDraw(double x, double y)
    {
        if (DimBackground)
            Scripts.scr_drawBG.Call(null, x, y, Width, Height, Draw.Black, 0.7);
    }

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

    // Escape: its stand-in tells it (scr_stonemod_gui_event "close" - on the native build, its user event 15 itself:
    // o_stonemod_modal's events are o_presset_town_encounter's, NativeHost).
    internal static void Install(ModContext loader)
    {
        if (Game.IsNative)
        {
            NativeHost.Install(loader, "o_stonemod_modal", "o_presset_town_encounter", new[] { "Create_0", "Other_10", "Other_25" },
                new Dictionary<string, Action<Instance>> { ["Other_25"] = CloseFor });
            return;
        }
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

    // Escape on a window's stand-in: the window closed.
    private static void CloseFor(Instance modal)
    {
        GmValue id = modal.Get("id");
        int key = id.Kind == GmKind.Instance ? id.AsInstance.Id : (int)id.AsReal;
        if (ByModal.TryGetValue(key, out var window))
            window.Close();
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
