namespace StoneForge;

/// <summary>A mod's UI (<see cref="ModContext.UI"/>), on screens for where it's shown: add elements to
/// <see cref="MainMenu"/> for the main menu, <see cref="InGame"/> for while playing, <see cref="Always"/> for
/// everywhere, <see cref="Hud"/> for with the game's HUD (under its windows), or a screen of your own with
/// <see cref="When(Func{bool}, UILayer)"/>. A screen out of its context isn't drawn, updated or clicked - its elements
/// keep their own <see cref="UIElement.Visible"/> for when it's back.</summary>
public sealed class ModUI
{
    private readonly ModContext _context;
    private UIScreen? _mainMenu, _inGame, _always, _hud;

    internal ModUI(ModContext context) => _context = context;

    /// <summary>Shown on the main menu.</summary>
    public UIScreen MainMenu => _mainMenu ??= When(() => Gm.InMainMenu);
    /// <summary>Shown while a game is played (<see cref="Gm.InGame"/>).</summary>
    public UIScreen InGame => _inGame ??= When(() => Gm.InGame);
    /// <summary>Shown everywhere: the main menu, the game, its loading and menus.</summary>
    public UIScreen Always => _always ??= When(() => true);
    /// <summary>Shown while a game is played, with the game's HUD (<see cref="UILayer.Hud"/>): under the game's
    /// windows and bottom panel, and hidden when its HUD is - its UI turned off, a cutscene.</summary>
    public UIScreen Hud => _hud ??= When(() => Gm.InGame && Game.Global["UI_is_on"].AsBool && !Game.IsCutscene, UILayer.Hud);

    /// <summary>A screen of your own, shown while <paramref name="active"/> says so (asked each frame), on
    /// <paramref name="layer"/>. Screens on a layer are drawn in the order they were made, later ones over
    /// earlier.</summary>
    public UIScreen When(Func<bool> active, UILayer layer = UILayer.Gui)
    {
        var screen = new UIScreen(active, _context.Id, layer);
        if (layer == UILayer.Hud)
            _context.DrawHud += screen.Frame;
        else
            _context.DrawGui += screen.Frame;
        return screen;
    }
}
