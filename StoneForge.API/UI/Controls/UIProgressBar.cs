namespace StoneForge;

/// <summary>A bar filled to <see cref="Value"/> / <see cref="Max"/>, as the game's health and energy bars;
/// it slides to a new value. Optional <see cref="Text"/> over it.</summary>
public class UIProgressBar : UIElement
{
    private double _shown = -1;

    public double Value { get; set; }
    public double Max { get; set; } = 1;
    public UIBarStyle Style { get; set; } = UIBarStyle.Health;
    /// <summary>Slides to a new value instead of jumping.</summary>
    public bool Smooth { get; set; } = true;
    public string? Text { get; set; }

    public UIProgressBar() { Width = 158; Height = 8; }
    public UIProgressBar(double x, double y, double value = 0, double max = 1, UIBarStyle style = UIBarStyle.Health, double width = 158) : this()
    {
        X = x; Y = y; Value = value; Max = max; Style = style; Width = width;
    }

    public double Fraction => Max <= 0 ? 0 : Math.Clamp(Value / Max, 0, 1);

    protected override void OnUpdate(double deltaTime)
    {
        double target = Fraction;
        _shown = !Smooth || _shown < 0 ? target : _shown + (target - _shown) * Math.Min(1, deltaTime * 10);
    }

    protected override void OnDraw(double x, double y)
    {
        var (back, fill) = Style == UIBarStyle.Energy ? (Sprite.s_holdbar_mana, Sprite.s_hp_n) : (Sprite.s_holdbar, Sprite.s_healthbar);
        double backWidth = Draw.SpriteWidth(back), backHeight = Draw.SpriteHeight(back);
        double fillWidth = Draw.SpriteWidth(fill), fillHeight = Draw.SpriteHeight(fill);
        Draw.SpritePart((int)back, 0, 0, 0, backWidth, backHeight, x, y, Width / backWidth, Height / backHeight);
        Draw.SpritePart((int)fill, 0, 0, 0, Math.Round(fillWidth * Math.Max(0, _shown)), fillHeight, x, y, Width / fillWidth, Height / fillHeight);
        if (!string.IsNullOrEmpty(Text))
            Draw.Text(x + Width / 2, y + Height / 2, Text, Draw.White, Draw.AlignCenter, Draw.AlignMiddle);
    }
}
