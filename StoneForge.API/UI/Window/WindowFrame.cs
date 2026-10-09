namespace StoneForge;

// A UIWindow's frame: its sprite (the game's version for the resolution, as scr_adaptiveMenusGetSprite picks: <name>_<view
// height>) at its own size or 9-sliced to the window's FrameWidth x FrameHeight - or, without one, a plain panel - and
// its title, in the middle of the game's view.
internal sealed class WindowFrame : UIElement
{
    private readonly UIWindow _window;
    private int _sprite = -1;

    internal WindowFrame(UIWindow window) => _window = window;

    // Sized and placed for the game's resolution now.
    internal void Fit()
    {
        _sprite = _window.FrameSprite;
        if (_sprite >= 0 && _window.AdaptiveSprite)
        {
            int adaptive = Game.CallScript("scr_adaptiveMenusGetSprite", default, _sprite).AsInt;
            if (adaptive >= 0)
                _sprite = adaptive;
        }
        if (_sprite >= 0 && _window.Slice == null)
        {
            Width = Draw.SpriteWidth(_sprite);
            Height = Draw.SpriteHeight(_sprite);
        }
        else
        {
            Width = _window.FrameWidth;
            Height = _window.FrameHeight;
        }
        // In the middle of the game's view (the window's frame - the game's own caption bar - aside).
        double left = Game.Global["gameframe_offset_left"].AsReal, top = Game.Global["gameframe_offset_top"].AsReal;
        double viewWidth = Game.Global["cameraWidth"].AsReal, viewHeight = Game.Global["cameraHeight"].AsReal;
        left += Game.Global["window_offset_x"].AsReal / Draw.Scale;
        top += Game.Global["window_offset_y"].AsReal / Draw.Scale;
        if (viewWidth <= 0 || viewHeight <= 0)
            (left, top, viewWidth, viewHeight) = (0, 0, Draw.Width, Draw.Height);
        X = Math.Floor(left + (viewWidth - Width) / 2);
        Y = Math.Floor(top + (viewHeight - Height) / 2);
        ((WindowCloseButton)_window.CloseButton).Fit();
    }

    protected override void OnDraw(double x, double y)
    {
        if (_sprite < 0)
            Draw.Panel(x, y, Width, Height);
        else
        {
            Scripts.scr_drawBG.Call(null, x, y, Width, Height, Draw.PanelColour, 1);
            if (_window.Slice is { } slice)
                Draw.SpriteNineSlice(_sprite, 0, x, y, Width, Height, slice);
            else
                Draw.Sprite(_sprite, x, y);
        }
        if (_window.Title.Length > 0)
            Draw.GameText(x + Width / 2 + _window.TitleX, y + _window.TitleY, _window.Title, Draw.White, Draw.AlignCenter, Draw.AlignMiddle, "f_digits", 0.5);
    }
}
