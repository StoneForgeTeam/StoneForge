namespace StoneForge;

/// <summary>A panel: the game's menu background with a border (or, <see cref="Framed"/>, the frame of the
/// game's hover windows). Holds other elements.</summary>
public class UIPanel : UIElement
{
    public int? Colour { get; set; }
    public double Alpha { get; set; } = 0.95;
    /// <summary>Drawn with the game's hover-window frame (corners and edges) instead of a plain border.</summary>
    public bool Framed { get; set; }

    public UIPanel() { }
    public UIPanel(double x, double y, double width, double height, UIAnchor anchor = UIAnchor.TopLeft)
    {
        X = x; Y = y; Width = width; Height = height; Anchor = anchor;
    }

    protected override void OnDraw(double x, double y)
    {
        if (Framed)
            Draw.Frame(x, y, Width, Height);
        else
            Draw.Panel(x, y, Width, Height, Colour, Alpha);
    }
}
