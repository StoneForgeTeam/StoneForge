namespace StoneForge;

/// <summary>The base of the UI: an element with a place (<see cref="X"/>, <see cref="Y"/> from its
/// <see cref="Anchor"/> in its parent), a size, children drawn on top of it, and the mouse. Inherit from it
/// (or from <see cref="UIPanel"/>, <see cref="UIButton"/>...) and override <see cref="OnDraw"/> to draw,
/// <see cref="OnUpdate"/> to change things each frame, <see cref="OnClick"/> and the rest to react. Put
/// elements on the screen with <see cref="ModContext.UI"/>. Coordinates are the GUI's (as <see cref="Draw"/>).</summary>
public abstract class UIElement
{
    private readonly List<UIElement> _children = new();

    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public UIAnchor Anchor { get; set; } = UIAnchor.TopLeft;
    /// <summary>Hidden: neither drawn nor clicked, children included.</summary>
    public bool Visible { get; set; } = true;
    /// <summary>Disabled: drawn (as the element likes) but not clicked.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Whether the mouse reaches it (false: it's see-through - clicks go to what's under it).</summary>
    public bool HitTest { get; set; } = true;
    /// <summary>Its children are cut to <see cref="ClipArea"/> (by default, itself): drawn only inside it, and
    /// clicked only there - for scrolling areas.</summary>
    public bool ClipChildren { get; set; }
    /// <summary>Text shown in the game's hover frame when the mouse rests on it (or on a child without its
    /// own).</summary>
    public string? Tooltip { get; set; }

    public UIElement? Parent { get; private set; }
    public IReadOnlyList<UIElement> Children => _children;

    /// <summary>The mouse is over it (and nothing on top of it takes the mouse).</summary>
    public bool IsHovered { get; internal set; }
    /// <summary>The mouse was pressed on it and is still held.</summary>
    public bool IsPressed { get; internal set; }

    /// <summary>Clicked (pressed and released on it).</summary>
    public event Action<UIElement>? Clicked;

    // Hidden by its parent's layout (a scroll list's rows out of view): not drawn or clicked, still updated.
    internal bool OutOfView;
    // An overlay (UIScreen.ShowOverlay) belongs to the element that opened it.
    internal UIElement? OverlayOf;

    /// <summary>Adds a child (drawn over this, placed in it); returns it, for chaining.</summary>
    public T Add<T>(T child) where T : UIElement
    {
        child.Parent?._children.Remove(child);
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    public void Remove(UIElement child)
    {
        if (_children.Remove(child))
            child.Parent = null;
    }

    public void Clear()
    {
        foreach (var child in _children)
            child.Parent = null;
        _children.Clear();
    }

    /// <summary>Its top-left corner on the screen.</summary>
    public double ScreenX
    {
        get
        {
            double parentX = Parent?.ScreenX ?? 0, parentWidth = Parent?.Width ?? Draw.Width;
            return Anchor switch
            {
                UIAnchor.Top or UIAnchor.Center or UIAnchor.Bottom => parentX + (parentWidth - Width) / 2 + X,
                UIAnchor.TopRight or UIAnchor.Right or UIAnchor.BottomRight => parentX + parentWidth - Width - X,
                _ => parentX + X,
            };
        }
    }

    public double ScreenY
    {
        get
        {
            double parentY = Parent?.ScreenY ?? 0, parentHeight = Parent?.Height ?? Draw.Height;
            return Anchor switch
            {
                UIAnchor.Left or UIAnchor.Center or UIAnchor.Right => parentY + (parentHeight - Height) / 2 + Y,
                UIAnchor.BottomLeft or UIAnchor.Bottom or UIAnchor.BottomRight => parentY + parentHeight - Height - Y,
                _ => parentY + Y,
            };
        }
    }

    /// <summary>Whether a screen point is inside it.</summary>
    public bool Contains(double screenX, double screenY)
    {
        double x = ScreenX, y = ScreenY;
        return screenX >= x && screenX < x + Width && screenY >= y && screenY < y + Height;
    }

    /// <summary>Whether it and all its parents are visible (and in view).</summary>
    public bool IsShown
    {
        get
        {
            UIElement? element = this;
            for (; element != null; element = element.Parent)
                if (!element.Visible || element.OutOfView)
                    return false;
            return true;
        }
    }

    /// <summary>The screen it's on, through its parents (null until it's on one).</summary>
    protected UIScreen? Screen
    {
        get
        {
            UIElement element = this;
            while (element.Parent != null)
                element = element.Parent;
            return element as UIScreen ?? element.OverlayOf?.Screen;
        }
    }

    /// <summary>Where its children are cut when it <see cref="ClipChildren"/>, on the screen: by default, itself.</summary>
    protected virtual (double X, double Y, double Width, double Height) ClipArea => (ScreenX, ScreenY, Width, Height);

    /// <summary>Every frame, before drawing (with the seconds since the last).</summary>
    protected virtual void OnUpdate(double deltaTime) { }
    /// <summary>Draws it, its top-left corner at (x, y) on the screen; its children are drawn after.</summary>
    protected virtual void OnDraw(double x, double y) { }
    /// <summary>Draws over its children, after them (unclipped).</summary>
    protected virtual void OnDrawAfter(double x, double y) { }
    /// <summary>Clicked (before <see cref="Clicked"/>'s handlers).</summary>
    protected virtual void OnClick() { }
    protected virtual void OnMouseEnter() { }
    protected virtual void OnMouseLeave() { }
    /// <summary>The mouse pressed on it.</summary>
    protected virtual void OnPress() { }
    /// <summary>The mouse wheel turned over it (1 up, -1 down). Return true if it used it; otherwise it goes
    /// on to the parent.</summary>
    protected virtual bool OnWheel(int delta) => false;

    // ---- what the loader runs ----

    internal void RunUpdate(double deltaTime)
    {
        if (!Visible)
            return;
        OnUpdate(deltaTime);
        // (A copy: an update may add or remove elements.)
        foreach (var child in _children.ToArray())
            child.RunUpdate(deltaTime);
    }

    internal void RunDraw()
    {
        if (!Visible || OutOfView)
            return;
        double x = ScreenX, y = ScreenY;
        OnDraw(x, y);
        if (ClipChildren && _children.Count > 0)
        {
            var area = ClipArea;
            if (Clip.Begin(this, area))
            {
                try
                {
                    foreach (var child in _children.ToArray())
                        child.RunDraw();
                }
                finally { Clip.End(this); }
            }
        }
        else
        {
            foreach (var child in _children.ToArray())
                child.RunDraw();
        }
        OnDrawAfter(x, y);
    }

    // The topmost element under the point that takes the mouse (children over parents, later over earlier).
    internal UIElement? HitAt(double x, double y)
    {
        if (!Visible || OutOfView)
            return null;
        if (ClipChildren && !InClipArea(x, y))
            return HitTest && Contains(x, y) ? this : null;
        for (int i = _children.Count - 1; i >= 0; i--)
        {
            var hit = _children[i].HitAt(x, y);
            if (hit != null)
                return hit;
        }
        return HitTest && Contains(x, y) ? this : null;
    }

    private bool InClipArea(double x, double y)
    {
        var area = ClipArea;
        return x >= area.X && x < area.X + area.Width && y >= area.Y && y < area.Y + area.Height;
    }

    internal void SetHovered(bool hovered)
    {
        if (hovered == IsHovered)
            return;
        IsHovered = hovered;
        if (hovered)
            OnMouseEnter();
        else
            OnMouseLeave();
    }

    internal void RaisePress() => OnPress();

    internal bool RaiseWheel(int delta) => OnWheel(delta);

    internal void RaiseClick()
    {
        OnClick();
        Clicked?.Invoke(this);
    }
}
