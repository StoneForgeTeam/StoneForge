namespace StoneForge;

/// <summary>A sprite (e.g. <see cref="ModContext.LoadSprite"/>), scaled to <see cref="Scale"/>; sized to it.</summary>
public class UIImage : UIElement
{
    public int Sprite { get; set; } = -1;
    public double Scale { get; set; } = 1;
    public int Frame { get; set; }

    public UIImage() { HitTest = false; }
    public UIImage(int sprite, double x = 0, double y = 0, double scale = 1) : this()
    {
        Sprite = sprite; X = x; Y = y; Scale = scale;
    }

    protected override void OnUpdate(double deltaTime)
    {
        if (Sprite >= 0)
        {
            Width = Draw.SpriteWidth(Sprite) * Scale;
            Height = Draw.SpriteHeight(Sprite) * Scale;
        }
    }

    protected override void OnDraw(double x, double y) => Draw.Sprite(Sprite, x, y, Scale, Frame);
}
