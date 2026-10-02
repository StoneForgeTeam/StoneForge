namespace StoneForge;

// A UIWindow's frame, as the game's Settings menu: its background, its sprite (the one for the game's
// resolution - scr_adaptiveMenusGetSprite), its title, in the middle of the screen; and where its content and
// close button go at that resolution (scr_adaptiveMenusGetOffset / TitleGetOffset / scr_adaptiveCloseButtonCreate
// for o_settings_menu, which return arrays C# can't read - their values for it here).
internal sealed class WindowFrame : UIElement
{
    private readonly UIWindow _window;
    private int _sprite = -1;
    private double _titleX, _titleY;
    private bool _smallClose;

    internal WindowFrame(UIWindow window) => _window = window;

    // Where its content starts, from its corner.
    internal (double X, double Y) ContentOffset { get; private set; }

    // Sized and placed for the game's resolution now.
    internal void Fit()
    {
        _sprite = Game.CallScript("scr_adaptiveMenusGetSprite", default, (int)Sprite.s_settings_menu).AsInt;
        if (_sprite < 0)
            _sprite = (int)Sprite.s_settings_menu;
        Width = Draw.SpriteWidth(_sprite);
        Height = Draw.SpriteHeight(_sprite);
        int cameraHeight = Game.Global["cameraHeight"].AsInt;
        ContentOffset = cameraHeight is 360 or 400 ? (27, 27) : (35, 34);
        string resolution = Game.Global["resolution"].AsString;
        _smallClose = resolution is "1280x720" or "1280x800";
        _titleX = 0;
        _titleY = _smallClose ? 9 : 13;
        // In the middle of the game's view (the window's frame - the game's own caption bar - aside).
        double left = Game.Global["gameframe_offset_left"].AsReal, top = Game.Global["gameframe_offset_top"].AsReal;
        double viewWidth = Game.Global["cameraWidth"].AsReal, viewHeight = Game.Global["cameraHeight"].AsReal;
        if (viewWidth <= 0 || viewHeight <= 0)
        {
            (left, top, viewWidth, viewHeight) = (0, 0, Draw.Width, Draw.Height);
        }
        X = Math.Floor(left + (viewWidth - Width) / 2);
        Y = Math.Floor(top + (viewHeight - Height) / 2);
    }

    internal UIElement MakeCloseButton()
        => _smallClose
            ? new WindowCloseButton(_window, (int)Sprite.s_gui_close_small, 472, 1)
            : new WindowCloseButton(_window, (int)Sprite.s_gui_close, 479, 3);

    protected override void OnDraw(double x, double y)
    {
        Scripts.scr_drawBG.Call(null, x, y, Width, Height, Draw.PanelColour, 1);
        Draw.Sprite(_sprite, x, y);
        Draw.GameText(x + _titleX + Width / 2, y + _titleY, _window.Title, Draw.White, Draw.AlignCenter, Draw.AlignMiddle, "f_digits", 0.5);
    }
}
