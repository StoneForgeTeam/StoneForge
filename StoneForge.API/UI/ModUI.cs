namespace StoneForge;

/// <summary>A mod's UI (<see cref="ModContext.UI"/>), on screens for where it's shown: add elements to
/// <see cref="MainMenu"/> for the main menu, <see cref="InGame"/> for while playing, <see cref="Always"/> for
/// everywhere, or a screen of your own with <see cref="When"/>. A screen out of its context isn't drawn,
/// updated or clicked - its elements keep their own <see cref="UIElement.Visible"/> for when it's back.</summary>
public sealed class ModUI
{
    private readonly ModContext _context;
    private UIScreen? _mainMenu, _inGame, _always;

    internal ModUI(ModContext context) => _context = context;

    /// <summary>Shown on the main menu.</summary>
    public UIScreen MainMenu => _mainMenu ??= When(() => Gm.InMainMenu);
    /// <summary>Shown while a game is played (<see cref="Gm.InGame"/>).</summary>
    public UIScreen InGame => _inGame ??= When(() => Gm.InGame);
    /// <summary>Shown everywhere: the main menu, the game, its loading and menus.</summary>
    public UIScreen Always => _always ??= When(() => true);

    /// <summary>A screen of your own, shown while <paramref name="active"/> says so (asked each frame). Screens
    /// are drawn in the order they were made, later ones over earlier.</summary>
    public UIScreen When(Func<bool> active)
    {
        var screen = new UIScreen(active, _context.Id);
        _context.DrawGui += screen.Frame;
        return screen;
    }
}
