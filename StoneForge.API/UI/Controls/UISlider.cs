namespace StoneForge;

/// <summary>A slider, as the game's volume sliders: drag the knob (or click the bar) to pick a value between
/// <see cref="Min"/> and <see cref="Max"/>, in <see cref="Step"/>s if set. The value shows to its right.</summary>
public class UISlider : UIElement
{
    private const double BarWidth = 151, BarHeight = 10, KnobWidth = 9;
    private double _value;

    public double Min { get; set; }
    public double Max { get; set; } = 1;
    /// <summary>Snaps the value to this step (0: any value).</summary>
    public double Step { get; set; }
    public double Value
    {
        get => _value;
        set => _value = Snap(Math.Clamp(value, Math.Min(Min, Max), Math.Max(Min, Max)));
    }
    /// <summary>Shows the value to the right of the bar.</summary>
    public bool ShowValue { get; set; } = true;
    /// <summary>How the value is shown (default: 0-1 sliders as a percentage, like the game's, others as the
    /// number).</summary>
    public Func<double, string>? Format { get; set; }
    /// <summary>The value changed (while dragging, every change).</summary>
    public event Action<double>? Changed;

    public UISlider() { Width = BarWidth; Height = 14; }
    public UISlider(double x, double y, double min = 0, double max = 1, double value = 0, double width = BarWidth) : this()
    {
        X = x; Y = y; Min = min; Max = max; Width = width; Value = value;
    }

    /// <summary>Where the value sits between Min and Max (0-1).</summary>
    public double Fraction => Max == Min ? 0 : (Value - Min) / (Max - Min);

    private double Snap(double value) => Step > 0 ? Min + Math.Round((value - Min) / Step) * Step : value;

    private void FollowMouse()
    {
        double t = Math.Clamp((Mouse.X - ScreenX) / Math.Max(1, Width - 1), 0, 1);
        double old = Value;
        Value = Min + t * (Max - Min);
        if (Value != old)
            Changed?.Invoke(Value);
    }

    protected override void OnMouseEnter() => UISounds.Hover();

    protected override void OnPress()
    {
        UISounds.Play(Sound.snd_combobox_on);
        FollowMouse();
    }

    protected override void OnUpdate(double deltaTime)
    {
        if (IsPressed)
            FollowMouse();
    }

    protected override bool OnWheel(int delta)
    {
        // (In a scrolling area the wheel scrolls it: scrolling past a slider mustn't change it.)
        for (var parent = Parent; parent != null; parent = parent.Parent)
            if (parent is UIScrollArea)
                return false;
        double old = Value;
        Value += delta * (Step > 0 ? Step : (Max - Min) / 20);
        if (Value != old)
            Changed?.Invoke(Value);
        return true;
    }

    protected override void OnDraw(double x, double y)
    {
        double barY = y + (Height - BarHeight) / 2;
        Draw.Sprite(Sprite.s_music_bar, x, barY, 0, Width / BarWidth);
        double knobX = x + Fraction * (Width - 1) - (KnobWidth - 1) / 2;
        Draw.Sprite(Sprite.s_music_slide, knobX, barY - 2, IsHovered || IsPressed ? 1 : 0);
        if (ShowValue)
            Draw.Text(x + Width + 8, y + Height / 2, Format?.Invoke(Value) ?? DefaultFormat(), Draw.White, Draw.AlignLeft, Draw.AlignMiddle);
    }

    private string DefaultFormat()
        => Min >= 0 && Max <= 1 && Step == 0 ? Math.Round(Value * 100).ToString() : Math.Round(Value, 2).ToString();
}
