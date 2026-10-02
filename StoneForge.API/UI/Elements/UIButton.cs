namespace StoneForge;

/// <summary>A button, as the game's Settings menu buttons: its sprite (stretched to any size, its ends kept),
/// lit under the mouse, pressed in while held, with the game's sounds; <see cref="UIElement.Clicked"/> when
/// clicked. 100x26 is the game's own size.</summary>
public class UIButton : UIElement
{
    // The settings button's sprite: frames 0 idle, 1 pressed, 2 lit; its rounded ends this wide.
    private const double Cap = 10;

    public string Text { get; set; } = "";

    public UIButton() { Width = 100; Height = 26; }
    public UIButton(string text, double x, double y, double width = 100, double height = 26, Action? onClick = null) : this()
    {
        Text = text; X = x; Y = y; Width = width; Height = height;
        if (onClick != null)
            Clicked += _ => onClick();
    }

    protected override void OnDraw(double x, double y)
    {
        var sprite = Enabled ? Sprite.s_settings_button_down : Sprite.s_settings_button_down_deactivated;
        bool pressed = Enabled && IsPressed && IsHovered;
        int frame = !Enabled ? 0 : pressed ? 1 : IsHovered ? 2 : 0;
        Draw.SpriteSliced((int)sprite, frame, x, y, Width, Height, Cap);
        int colour = Enabled ? Draw.White : Draw.Rgb(80, 80, 87);
        Draw.GameText(x + Width / 2, y + Height / 2 + (pressed ? 1 : 0), Text, colour, Draw.AlignCenter, Draw.AlignMiddle, "f_digits", 0.5, pressed ? 0.5 : 1);
    }

    protected override void OnMouseEnter()
    {
        if (Enabled)
            UISounds.Hover();
    }

    protected override void OnPress() => UISounds.Play(Sound.snd_button_click);
}
