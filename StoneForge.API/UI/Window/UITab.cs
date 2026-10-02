namespace StoneForge;

/// <summary>A tab down a <see cref="UIWindow"/>'s left (<see cref="UIWindow.SetTabs(string[])"/>), as the
/// Settings menu's: clicked - or <see cref="Open"/>ed - it's shown as the open one, and its window's
/// <c>OnTabOpened</c> and its <see cref="Opened"/> run, to fill the window's page.</summary>
public class UITab : UIElement
{
    // The settings tab's sprite (its button's): frames 0 idle, 1 pressed, 2 lit - and open.
    private const double Cap = 10;

    internal UITab(UIWindow window, string text, int index, double width)
    {
        Window = window;
        Text = text;
        Index = index;
        Width = width;
        Height = 26;
    }

    public UIWindow Window { get; }
    public string Text { get; set; }
    /// <summary>Its place among the window's tabs (from 0).</summary>
    public int Index { get; }
    public bool IsOpen => Window.SelectedTab == this;

    /// <summary>Opened: clicked, or <see cref="Open"/> (after its window's OnTabOpened).</summary>
    public event Action<UITab>? Opened;

    /// <summary>Opens it (as a click, without the sound).</summary>
    public void Open()
    {
        Window.Select(this);
        Opened?.Invoke(this);
    }

    protected override void OnDraw(double x, double y)
    {
        bool pressed = !IsOpen && IsPressed && IsHovered;
        int frame = IsOpen ? 2 : pressed ? 1 : IsHovered ? 2 : 0;
        Draw.SpriteSliced((int)Sprite.s_settings_button_down, frame, x, y, Width, Height, Cap);
        Draw.GameText(x + Width / 2, y + Height / 2 + (pressed ? 1 : 0), Text, Draw.White, Draw.AlignCenter, Draw.AlignMiddle, "f_digits", 0.5, pressed ? 0.5 : 1);
    }

    protected override void OnMouseEnter()
    {
        if (!IsOpen)
            UISounds.Hover();
    }

    protected override void OnPress()
    {
        if (!IsOpen)
            UISounds.Play(Sound.snd_button_click);
    }

    protected override void OnClick()
    {
        if (IsOpen)
            return;
        UISounds.Play(Sound.snd_ui_menu_push_down_button_release_close_window, 2);
        Open();
    }
}
