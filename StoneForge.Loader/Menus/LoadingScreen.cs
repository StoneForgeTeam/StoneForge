namespace StoneForge.Loader;

/// <summary>StoneForge's loading screen: the whole screen, from when the game's window is ready (sized, its frame
/// drawn - Ready), while the mods compile and load - the game's own loading (o_gameLoader, in its first room) held
/// until they're done - then a moment with the result, and it fades into the game's loading screen.</summary>
internal sealed class LoadingScreen : UIElement
{
    // Once the mods are loaded and the screen is up: the result shown this long, then the fade. (The game's first
    // real frame comes a few seconds after it starts - the runner loads first - and a mod or two are often
    // loaded by then.) Should the screen never come up, the game is let go this long after.
    private const double ResultSeconds = 1.0, FadeSeconds = 0.7, NeverShownSeconds = 5;

    private static readonly int Background = Draw.Rgb(10, 8, 16);
    private static readonly int TitleColour = Draw.Rgb(236, 220, 186);
    private static readonly int BarBack = Draw.Rgb(36, 32, 48), BarFill = Draw.Rgb(201, 164, 98), BarOutline = Draw.Rgb(9, 10, 18),
        BarFrame = Draw.Rgb(118, 86, 52);
    private static readonly int ErrorColour = Draw.Rgb(214, 96, 77);

    private double _shown = -1;
    private readonly StartupProgress? _runtime;
    private StartupProgress Progress => _runtime ?? ModManager.Startup;
    private bool Showing => _runtime == null ? HoldsGame : !_runtime.Finished ||
        _runtime.SecondsSinceFinished < ResultSeconds + FadeSeconds;
    private double DisplayResultTime => _runtime?.SecondsSinceFinished ?? ResultTime;
    private static ModContext? _loader;
    private static RuntimeWindow? _runtimeWindow;
    internal static bool RuntimeVisible => _runtimeWindow?.IsOpen == true;

    internal static void ShowRuntime(StartupProgress progress)
    {
        if (_loader == null) throw new InvalidOperationException("Loading screen is not installed.");
        _splash = -2;
        _runtimeWindow = _loader.UI.Always.Add(new RuntimeWindow(progress));
        _runtimeWindow.Open();
    }

    internal static void EnsureRuntimeVisible()
    {
        if (_runtimeWindow is { IsOpen: false }) _runtimeWindow.Open();
    }

    private sealed class RuntimeWindow : UIWindow
    {
        private readonly LoadingScreen _screen;
        internal RuntimeWindow(StartupProgress progress)
        {
            DimBackground = false;
            Frame.Visible = false;
            _screen = new LoadingScreen(progress);
            Closed += _ => { if (progress.Finished) Parent?.Remove(this); };
        }
        protected override void OnUpdate(double deltaTime)
        {
            base.OnUpdate(deltaTime);
            _screen.RunUpdate(deltaTime);
            if (!_screen.Visible) Close();
        }
        protected override void OnDraw(double x, double y) => _screen.RunDraw();
    }
    // Since it was first shown (once the game's window is ready: Ready).
    private static readonly System.Diagnostics.Stopwatch SinceShown = new();

    /// <summary>The game's own loading is held while this is up (until it has faded).</summary>
    public static bool HoldsGame
    {
        get
        {
            var startup = ModManager.Startup;
            return !startup.Finished || (SinceShown.IsRunning ? ResultTime : startup.SecondsSinceFinished - NeverShownSeconds) < ResultSeconds + FadeSeconds;
        }
    }

    // Frames drawn, and frames since the screen's size last changed. (The game sets its resolution in its first
    // frames: o_cameraController's reset - user event 15 - a chain of alarms over 60 frames that resizes the window
    // from the first room's size and sets the display mode; "reset" is true until it's done.)
    private const int MinFrames = 10, StableFrames = 5;
    private static int _frames, _stable;
    private static (double, double, double, double) _size;
    private static bool _loggedReady;

    /// <summary>Whether it's up and settled - drawn a few frames, the resolution no longer changing: mods load
    /// once it is (a mod's Load holds the frame, so the screen stays as it was last drawn).</summary>
    public static bool Ready => _frames >= MinFrames && _stable >= StableFrames && !_cameraResetting;
    private static bool _cameraResetting = true;

    // How long the result has been on screen: since the mods loaded, or since it came up if that was later.
    private static double ResultTime => Math.Min(ModManager.Startup.SecondsSinceFinished, SinceShown.Elapsed.TotalSeconds);

    // The game's loader (its first room's o_gameLoader: data, sounds, text, then on to the logos) waits for us:
    // its step is skipped while the mods load.
    // The cursor is hidden meanwhile: the game's (drawn in o_cursorController's Draw GUI - StoneForge draws it
    // again on top, through the same event) and Windows' (window_set_cursor; the game sets its own once its camera
    // has reset, scr_cursorDataUpdate) - and put back as the game sets it once the screen has gone.
    public static void Install(ModContext loader)
    {
        _loader = loader;
        loader.OnCode("gml_Object_o_gameLoader_Step_2", before: (_, _) => HoldsGame);
        loader.OnCode("gml_Object_o_cursorController_Draw_64", before: (_, _) => HoldsGame || RuntimeVisible);
        loader.Frame += KeepCursorHidden;
    }

    private const int CursorNone = -1;
    private static bool _cursorHidden;

    private static void KeepCursorHidden()
    {
        // (Only once the screen is up: in the runner's first frames the game can't be called.)
        if (_frames == 0)
            return;
        if (HoldsGame || RuntimeVisible)
        {
            Game.CallBuiltin("window_set_cursor", CursorNone);
            _cursorHidden = true;
            return;
        }
        if (!_cursorHidden)
            return;
        _cursorHidden = false;
        // (Should the camera not have reset yet, it sets the cursor itself when it has.)
        var camera = Instances.First<GameInstance>(GameObjectId.o_cameraController);
        if (camera != null && !camera.Instance.Get("reset").AsBool)
            Game.CallScript("scr_cursorDataUpdate", camera.Instance, camera.Instance.Get("displayMode"));
    }

    public LoadingScreen() : this(null) { }
    private LoadingScreen(StartupProgress? runtime)
    {
        _runtime = runtime;
        // (Over everything, taking the mouse: nothing under it is for clicking yet.)
        Width = 1;
        Height = 1;
    }

    protected override void OnUpdate(double deltaTime)
    {
        if (!Showing)
        {
            Visible = false;
            // (The splash art's texture freed: it's not shown again.)
            if (_splash >= 0)
                Game.CallBuiltinTrusted("sprite_delete", default, default, _splash);
            _splash = -1;
            return;
        }
        Width = Draw.Width;
        Height = Draw.Height;
        var startup = Progress;
        double target = startup.Total == 0 ? 1 : (double)startup.Done / startup.Total;
        _shown = _shown < 0 ? target : _shown + (target - _shown) * Math.Min(1, Math.Min(deltaTime, 0.25) * 8);
        if (_runtime != null) return;
        _frames++;
        var camera = Instances.First<GameInstance>(GameObjectId.o_cameraController);
        _cameraResetting = camera == null || camera.Instance.Get("reset").AsBool;
        var size = (Width, Height, Game.CallBuiltin("window_get_width").AsReal, Game.CallBuiltin("window_get_height").AsReal);
        if (size != _size && _frames > 1)
            Game.Log($"Loading screen: the screen changed size at frame {_frames}: GUI {_size.Item1}x{_size.Item2} -> {size.Item1}x{size.Item2}, window {_size.Item3}x{_size.Item4} -> {size.Item3}x{size.Item4}");
        _stable = size == _size ? _stable + 1 : 0;
        _size = size;
        // (Shown from here on - not while the game's window is still being sized, and its frame drawn.)
        if (Ready && !SinceShown.IsRunning)
            SinceShown.Start();
        if (Ready && !_loggedReady)
        {
            _loggedReady = true;
            Game.Log($"Loading screen up and settled after {_frames} frames (GUI {Width}x{Height}, window {size.Item3}x{size.Item4})");
        }
    }

    protected override void OnDraw(double x, double y)
    {
        if (_runtime == null && !SinceShown.IsRunning)
            return;
        var startup = Progress;
        double alpha = startup.Finished ? Math.Clamp(1 - (DisplayResultTime - ResultSeconds) / FadeSeconds, 0, 1) : 1;
        double cx = Width / 2, cy = Height / 2;

        Draw.Rectangle(0, 0, Width, Height, Background, alpha);
        double barY, barWidth = 320;
        int splash = Splash();
        if (splash >= 0)
        {
            // The splash art (its logo and title in it) covering the screen, centred; the bar under the title - a point of the art at (artX, artY) is on screen at (cx + (artX - 960) * scale, ...).
            double scale = Math.Max(Width / SplashWidth, Height / SplashHeight);
            Draw.Sprite(splash, cx - SplashWidth / 2 * scale, cy - SplashHeight / 2 * scale, scale, alpha: alpha);
            barY = cy + (BarArtY - SplashHeight / 2) * scale;
            // (As wide as the logo and title together.)
            barWidth = Math.Max(barWidth, (BarArtRight - BarArtLeft) * scale);
            Draw.Text(cx, barY - 20, $"v{LoaderVersion.Text}", Draw.Muted, Draw.AlignCenter, alpha: alpha);
        }
        else
        {
            TextScaled(cx, cy - 48, "StoneForge", TitleColour, 1.5, alpha);
            Draw.Text(cx, cy - 12, $"v{LoaderVersion.Text}", Draw.Muted, Draw.AlignCenter, alpha: alpha);
            barY = cy + 10;
        }

        const double barHeight = 4;
        double barX = cx - barWidth / 2;
        // (Framed as the art is: a pixel of bronze round it, and its dark outline round that.)
        Draw.Rectangle(barX - 2, barY - 2, barX + barWidth + 2, barY + barHeight + 2, BarOutline, alpha);
        Draw.Rectangle(barX - 1, barY - 1, barX + barWidth + 1, barY + barHeight + 1, BarFrame, alpha);
        Draw.Rectangle(barX, barY, barX + barWidth, barY + barHeight, BarBack, alpha);
        if (_shown > 0)
            Draw.Rectangle(barX, barY, barX + barWidth * Math.Clamp(_shown, 0, 1), barY + barHeight, BarFill, alpha);

        string status;
        if (!startup.Finished)
            status = Localization.Get(_runtime == null ? "loading.current" : "loading.enabling", startup.Current, startup.CurrentIndex + 1, startup.Total);
        else if (startup.Loaded == 0)
            status = Localization.Get("loading.none");
        else
            status = Localization.Get(startup.Loaded == 1 ? "loading.loaded_one" : "loading.loaded_many", startup.Loaded);
        Draw.Text(cx, barY + 12, status, Draw.Muted, Draw.AlignCenter, alpha: alpha);
        if (startup.Failed > 0)
            Draw.Text(cx, barY + 28, Localization.Get("loading.failed", startup.Failed), ErrorColour, Draw.AlignCenter, alpha: alpha);
        if (_runtime != null) ModManager.RuntimeLoadingDrawn();
    }

    // The splash art: dotnet\StoneForge.Splash.png (branding\splash.png, 1920x1080) as a sprite, loaded the first
    // time it's drawn (-1: missing - the title drawn as text instead). BarArtY: where the bar goes in it, under the
    // title; BarArtLeft/Right: the ends of the logo and title, the bar as wide.
    private const double SplashWidth = 1920, SplashHeight = 1080, BarArtY = 640, BarArtLeft = 385, BarArtRight = 1555;
    private static int _splash = -2;

    private static int Splash()
    {
        if (_splash != -2)
            return _splash;
        _splash = -1;
        string path = Path.Combine(Path.GetDirectoryName(typeof(LoadingScreen).Assembly.Location)!, "StoneForge.Splash.png");
        if (File.Exists(path))
        {
            _splash = Game.CallBuiltinTrusted("sprite_add", default, default, path, 1, false, false, 0, 0).AsInt;
            if (_splash < 0)
                Game.Log($"Loading screen: couldn't load {path}");
        }
        return _splash;
    }

    // The game's text font at a size of our own (Draw.Text is its half size), with its shadow (a pixel of the
    // screen at the game's scale).
    private static void TextScaled(double x, double y, string text, int colour, double scale, double alpha)
    {
        GmValue previous = Game.CallBuiltin("draw_get_font");
        Game.CallBuiltin("draw_set_font", Game.Global["f_dmg"]);
        Game.CallBuiltin("draw_set_halign", Draw.AlignCenter);
        Game.CallBuiltin("draw_set_valign", Draw.AlignTop);
        Game.CallBuiltin("draw_set_alpha", alpha);
        Game.CallBuiltin("draw_set_colour", Draw.Black);
        const double shadow = 1;
        Game.CallBuiltin("draw_text_transformed", x + shadow, y + shadow, text, scale, scale, 0);
        Game.CallBuiltin("draw_set_colour", colour);
        Game.CallBuiltin("draw_text_transformed", x, y, text, scale, scale, 0);
        Game.CallBuiltin("draw_set_colour", Draw.White);
        Game.CallBuiltin("draw_set_alpha", 1);
        Game.CallBuiltin("draw_set_halign", Draw.AlignLeft);
        Game.CallBuiltin("draw_set_font", previous);
    }
}
