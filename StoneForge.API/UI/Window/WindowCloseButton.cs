namespace StoneForge;

// A UIWindow's close button (UIWindow.CloseButton): the game's - s_gui_close, small at 1280x720 / 1280x800 as the game's
// windows have it (scr_adaptiveCloseButtonCreate) - placed where the window says (by default its frame's top right).
internal sealed class WindowCloseButton : UIElement
{
    private readonly UIWindow _window;
    private int _sprite = (int)Sprite.s_gui_close;

    internal WindowCloseButton(UIWindow window)
    {
        _window = window;
        Tooltip = "Close (Esc)";
    }

    // Its sprite for the game's resolution, and its size (when the window opens).
    internal void Fit()
    {
        string resolution = Game.Global["resolution"].AsString;
        _sprite = (int)(resolution is "1280x720" or "1280x800" ? Sprite.s_gui_close_small : Sprite.s_gui_close);
        Width = Draw.SpriteWidth(_sprite);
        Height = Draw.SpriteHeight(_sprite);
    }

    protected override void OnDraw(double x, double y)
        => Draw.Sprite(_sprite, x, y, 1, IsPressed && IsHovered ? 1 : IsHovered ? 2 : 0);

    protected override void OnMouseEnter() => UISounds.Hover();

    protected override void OnPress() => UISounds.Play(Sound.snd_button_click);

    protected override void OnClick() => _window.Close();
}
