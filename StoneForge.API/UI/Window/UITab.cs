namespace StoneForge;

/// <summary>A tab in a <see cref="UITabStrip"/>: clicked - or <see cref="Open"/>ed - it's shown as the open one, and its
/// strip's <see cref="UITabStrip.TabOpened"/> and its own <see cref="Opened"/> run.</summary>
public class UITab : UIElement
{
    // A button sprite's rounded ends this wide; its frames 0 idle, 1 pressed, 2 lit - and open.
    private const double Cap = 10;

    internal UITab(UITabStrip strip, string text, int index)
    {
        Strip = strip;
        Text = text;
        Index = index;
        Height = 26;
    }

    public UITabStrip Strip { get; }
    public string Text { get; set; }
    /// <summary>Its place among its strip's tabs (from 0).</summary>
    public int Index { get; }
    public bool IsOpen => Strip.Selected == this;

    /// <summary>Opened: clicked, or <see cref="Open"/> (after its strip's <see cref="UITabStrip.TabOpened"/>).</summary>
    public event Action<UITab>? Opened;

    /// <summary>Opens it (as a click, without the sound).</summary>
    public void Open()
    {
        Strip.Select(this);
        try { Opened?.Invoke(this); }
        catch (Exception e) { Game.Log($"Tab \"{Text}\" Opened handler threw: {e}"); }
    }

    protected override void OnDraw(double x, double y)
    {
        bool pressed = !IsOpen && IsPressed && IsHovered;
        int frame = IsOpen ? 2 : pressed ? 1 : IsHovered ? 2 : 0;
        Draw.SpriteSliced(Strip.TabSprite, frame, x, y, Width, Height, Cap);
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
