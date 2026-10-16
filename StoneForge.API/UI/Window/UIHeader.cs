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
    {
        if (Draw.TitleFont(Text) == "f_digits")
            Game.CallScript("scr_drawCharacterHeader", default, x, y, Text, Width, Height, Draw.AlignLeft);
        else
        {
            Draw.GameText(x, y + Height / 2 - 4, Text, Draw.White, Draw.AlignLeft, Draw.AlignMiddle, "f_dmg", 0.5);
            Game.CallScript("scr_hoversDrawLine", default, x, y + Height / 2 + 4, Width, 4, 1);
        }
    }
}
