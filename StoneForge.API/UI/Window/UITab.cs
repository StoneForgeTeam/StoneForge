namespace StoneForge;

/// <summary>A tab in a <see cref="UITabStrip"/>: clicked - or <see cref="Open"/>ed - it's shown as the open one, and its
/// strip's <see cref="UITabStrip.TabOpened"/> and its own <see cref="Opened"/> run.</summary>
public class UITab : UIElement
{
    // A button sprite's rounded ends this wide; its frames 0 idle, 1 pressed, 2 lit - and open.
    private const double Cap = 10;
    private double _elapsed;
    private string? _measuredText;
    private double _textWidth;
    private double _measuredWidth;

    protected override void OnUpdate(double deltaTime)
    {
        if (!IsOpen || !IsShown) _elapsed = 0;
        else _elapsed += Math.Max(0, deltaTime);
    }

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
        _elapsed = 0;
        Strip.Select(this);
        try { Opened?.Invoke(this); }
        catch (Exception e) { Game.Log($"Tab \"{Text}\" Opened handler threw: {e}"); }
    }

    protected override void OnDraw(double x, double y)
    {
        bool pressed = !IsOpen && IsPressed && IsHovered;
        int frame = IsOpen ? 2 : pressed ? 1 : IsHovered ? 2 : 0;
        Draw.SpriteSliced(Strip.TabSprite, frame, x, y, Width, Height, Cap);
        double room = Math.Max(0, Width - Cap * 2);
        if (_measuredText != Text || _measuredWidth != Width)
        {
            var previous = Game.CallBuiltin("draw_get_font");
            try
            {
                Game.CallBuiltin("draw_set_font", Game.Global["f_digits"]);
                _textWidth = Game.CallBuiltin("string_width", Text).AsReal * 0.5;
            }
            finally { Game.CallBuiltin("draw_set_font", previous); }
            _measuredText = Text;
            _measuredWidth = Width;
            _elapsed = 0;
        }
        bool overflow = _textWidth > room;
        if (!overflow)
            Draw.GameText(x + Width / 2, y + Height / 2 + (pressed ? 1 : 0), Text, Draw.White, Draw.AlignCenter, Draw.AlignMiddle, "f_digits", 0.5, pressed ? 0.5 : 1);
        else if (Clip.Begin(this, (x + Cap, y, room, Height)))
        {
            try
            {
                // Read the beginning, travel to the end, pause, then return to the beginning.
                double travel = (_textWidth - room) / 20;
                double phase = _elapsed % (travel * 2 + 2);
                double offset = !IsOpen || phase < 1 ? 0 : phase < 1 + travel ? (phase - 1) * 20
                    : phase < 2 + travel ? _textWidth - room : (travel * 2 + 2 - phase) * 20;
                Draw.GameText(x + Cap - offset, y + Height / 2 + (pressed ? 1 : 0), Text, Draw.White,
                    Draw.AlignLeft, Draw.AlignMiddle, "f_digits", 0.5, pressed ? 0.5 : 1);
            }
            finally { Clip.End(this); }
        }
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
