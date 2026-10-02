namespace StoneForge;

/// <summary>A section header, as the game's Settings pages: its title over the game's hover line.</summary>
public class UIHeader : UIElement
{
    public string Text { get; set; }

    public UIHeader(string text = "")
    {
        Text = text;
        HitTest = false;
        Width = 300;
        Height = 26;
    }

    protected override void OnDraw(double x, double y)
        => Game.CallScript("scr_drawCharacterHeader", default, x, y, Text, Width, Height, Draw.AlignLeft);
}
