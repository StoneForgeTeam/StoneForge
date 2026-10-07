namespace StoneForge;

/// <summary>A row of buttons laid out across its width - any number: spread evenly, each <see cref="ButtonWidth"/> wide
/// (narrowed to fit), or packed to one side (<see cref="Align"/>). Add buttons with <see cref="Add(string, Action?)"/> (or any
/// element with <see cref="UIElement.Add{T}"/>); they're placed as they change. For a frame with places for buttons
/// drawn in it, <see cref="Positions"/> puts them there instead (<see cref="Pin"/> keeps one in its place).</summary>
public class UIButtonRow : UIElement
{
    public UIButtonRow() { Height = 26; HitTest = false; }

    public UIButtonRow(double x, double y, double width, double height = 26) : this()
    {
        X = x; Y = y; Width = width; Height = height;
    }

    /// <summary>A button's width (default 100, the game's) - less, when they don't fit.</summary>
    public double ButtonWidth { get; set; } = 100;
    /// <summary>The least space between buttons.</summary>
    public double Spacing { get; set; } = 4;
    /// <summary>How they're placed: <see cref="Draw.AlignCenter"/> (default) spread evenly across it;
    /// <see cref="Draw.AlignLeft"/> / <see cref="Draw.AlignRight"/> packed to that side, <see cref="Spacing"/> apart.</summary>
    public int Align { get; set; } = Draw.AlignCenter;
    /// <summary>Set places for the buttons - each one's left edge, across the row - as a frame's artwork has them (null:
    /// laid out by <see cref="Align"/>). Buttons fill them from the left, those <see cref="Pin"/>ned keep theirs; more
    /// buttons than places: laid out by <see cref="Align"/> after all.</summary>
    public IReadOnlyList<double>? Positions { get; set; }

    private readonly Dictionary<UIElement, int> _pinned = new(ReferenceEqualityComparer.Instance);

    /// <summary>Keeps <paramref name="button"/> (one of its own) in one of the <see cref="Positions"/> - a Close button
    /// in the last, say.</summary>
    public void Pin(UIElement button, int position) => _pinned[button] = position;

    /// <summary>Adds a button (the game's) with this text; returns it.</summary>
    public UIButton Add(string text, Action? onClick = null) => Add(new UIButton(text, 0, 0, ButtonWidth, Height, onClick));

    protected override void OnUpdate(double deltaTime) => Layout();

    protected override void OnDraw(double x, double y) => Layout();

    private void Layout()
    {
        var shown = Children.Where(c => c.Visible).ToList();
        int n = shown.Count;
        if (n == 0)
            return;
        if (Positions is { } places && n <= places.Count && PlaceAt(shown, places))
            return;
        double width = Math.Max(1, Math.Min(ButtonWidth, (Width - Spacing * (n - 1)) / n));
        // Spread: equal gaps, the outer ones half as wide; packed: Spacing apart from one side.
        double gap = Align == Draw.AlignCenter ? (Width - width * n) / n : Spacing;
        double x = Align switch
        {
            Draw.AlignCenter => gap / 2,
            Draw.AlignRight => Width - (width * n + Spacing * (n - 1)),
            _ => 0,
        };
        foreach (var child in shown)
        {
            child.Anchor = UIAnchor.TopLeft;
            child.X = Math.Floor(x);
            child.Y = 0;
            child.Width = width;
            child.Height = Height;
            x += width + gap;
        }
    }

    // Into the set places: the pinned ones in theirs, the rest in the free ones from the left (false: they don't fit).
    private bool PlaceAt(List<UIElement> shown, IReadOnlyList<double> places)
    {
        var taken = new UIElement?[places.Count];
        foreach (var child in shown)
            if (_pinned.TryGetValue(child, out int at) && at >= 0 && at < places.Count && taken[at] == null)
                taken[at] = child;
        int free = 0;
        foreach (var child in shown)
        {
            if (Array.IndexOf(taken, child) >= 0)
                continue;
            while (free < places.Count && taken[free] != null)
                free++;
            if (free >= places.Count)
                return false;
            taken[free] = child;
        }
        for (int i = 0; i < places.Count; i++)
        {
            if (taken[i] is not { } child)
                continue;
            child.Anchor = UIAnchor.TopLeft;
            child.X = places[i];
            child.Y = 0;
            child.Width = ButtonWidth;
            child.Height = Height;
        }
        return true;
    }
}
