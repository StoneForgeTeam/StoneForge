namespace StoneForge;

/// <summary>Text, as the game draws it. With <see cref="Wrap"/> it wraps at its width, and its height follows
/// the text.</summary>
public class UILabel : UIElement
{
    public string Text { get; set; } = "";
    public int? Colour { get; set; }
    public int Align { get; set; } = Draw.AlignLeft;
    public bool Wrap { get; set; }

    public UILabel() { HitTest = false; Height = 12; }
    public UILabel(string text, double x = 0, double y = 0, int? colour = null) : this()
    {
        Text = text; X = x; Y = y; Colour = colour;
    }

    protected override void OnUpdate(double deltaTime)
    {
        if (Wrap && Width > 0)
            Height = Draw.TextHeight(Text, Width);
    }

    protected override void OnDraw(double x, double y)
    {
        if (Wrap && Width > 0)
            Draw.TextWrapped(x, y, Text, Width, Colour);
        else if (Align == Draw.AlignCenter)
            Draw.Text(x + Width / 2, y, Text, Colour, Draw.AlignCenter);
        else if (Align == Draw.AlignRight)
            Draw.Text(x + Width, y, Text, Colour, Draw.AlignRight);
        else
            Draw.Text(x, y, Text, Colour);
    }
}
