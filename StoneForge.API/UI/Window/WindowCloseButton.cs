namespace StoneForge;

// A UIWindow's close button, at its frame's top right: the game's (s_gui_close, small at 1280x720/800).
internal sealed class WindowCloseButton : UIElement
{
    private readonly UIWindow _window;
    private readonly int _sprite;

    internal WindowCloseButton(UIWindow window, int sprite, double x, double y)
    {
        _window = window;
        _sprite = sprite;
        X = x;
        Y = y;
        Width = Draw.SpriteWidth(sprite);
        Height = Draw.SpriteHeight(sprite);
        Tooltip = "Close (Esc)";
    }

    protected override void OnDraw(double x, double y)
        => Draw.Sprite(_sprite, x, y, 1, IsPressed && IsHovered ? 1 : IsHovered ? 2 : 0);

    protected override void OnMouseEnter() => UISounds.Hover();

    protected override void OnPress() => UISounds.Play(Sound.snd_button_click);

    protected override void OnClick() => _window.Close();
}
