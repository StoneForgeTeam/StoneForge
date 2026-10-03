namespace StoneForge;

/// <summary>Space in from each edge: a window's frame borders (<see cref="UIWindow.ContentInsets"/>,
/// <see cref="UIWindow.Slice"/>), in the UI's units.</summary>
public readonly record struct UIInsets(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The same on every side.</summary>
    public UIInsets(double all) : this(all, all, all, all) { }

    /// <summary>Left and right the same, top and bottom the same.</summary>
    public UIInsets(double horizontal, double vertical) : this(horizontal, vertical, horizontal, vertical) { }
}
