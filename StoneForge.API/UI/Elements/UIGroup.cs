namespace StoneForge;

/// <summary>A plain container: draws nothing itself, holds other elements (placed from its corner).</summary>
public class UIGroup : UIElement
{
    public UIGroup() => HitTest = false;
    public UIGroup(double x, double y, double width, double height) : this()
    {
        X = x; Y = y; Width = width; Height = height;
    }
}
