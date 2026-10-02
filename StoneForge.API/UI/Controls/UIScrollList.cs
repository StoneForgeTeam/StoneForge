namespace StoneForge;

/// <summary>A list of elements stacked top to bottom (add them as children), scrolled a row at a time with
/// the wheel or the game's scrollbar, which shows when they don't all fit. Rows that don't fit aren't drawn,
/// so give it rows that are each shorter than the list.</summary>
public class UIScrollList : UIElement
{
    private const double BarWidth = 11, ArrowSize = 9, ThumbHeight = 21;
    private int _first, _maxFirst;
    private bool _dragging;

    public double Spacing { get; set; } = 2;
    public double Padding { get; set; } = 4;
    /// <summary>Draws the game's dark box behind the rows.</summary>
    public bool Background { get; set; } = true;
    /// <summary>Makes every row as wide as the list (less the scrollbar).</summary>
    public bool FitWidth { get; set; } = true;
    /// <summary>The first row shown.</summary>
    public int FirstVisible
    {
        get => _first;
        set => _first = Math.Clamp(value, 0, _maxFirst);
    }
    public bool CanScroll => _maxFirst > 0;

    public UIScrollList() { Width = 200; Height = 150; }
    public UIScrollList(double x, double y, double width, double height) : this()
    {
        X = x; Y = y; Width = width; Height = height;
    }

    /// <summary>Scrolls by rows (positive: down).</summary>
    public void Scroll(int rows) => FirstVisible += rows;

    /// <summary>Scrolls so this row shows.</summary>
    public void ScrollTo(UIElement row)
    {
        int index = Children.ToList().IndexOf(row);
        if (index >= 0 && (index < _first || row.OutOfView))
            FirstVisible = index;
    }

    private double BarX => ScreenX + Width - Padding - BarWidth;
    private double TrackTop => ScreenY + Padding + ArrowSize + 1;
    private double TrackLength => Height - Padding * 2 - (ArrowSize + 1) * 2;

    protected override void OnUpdate(double deltaTime)
    {
        var rows = Children;
        double room = Height - Padding * 2;
        // The last first row that still fills the list to the end.
        int maxFirst = rows.Count;
        double used = 0;
        for (int i = rows.Count - 1; i >= 0; i--)
        {
            double height = rows[i].Height + (used > 0 ? Spacing : 0);
            if (used + height > room)
                break;
            used += height;
            maxFirst = i;
        }
        _maxFirst = rows.Count == 0 ? 0 : Math.Min(maxFirst, rows.Count - 1);

        if (_dragging && IsPressed && CanScroll)
        {
            double t = Math.Clamp((Mouse.Y - TrackTop - ThumbHeight / 2) / Math.Max(1, TrackLength - ThumbHeight), 0, 1);
            _first = (int)Math.Round(t * _maxFirst);
        }
        else
            _dragging = false;
        _first = Math.Clamp(_first, 0, _maxFirst);

        double rowWidth = Width - Padding * 2 - (CanScroll ? BarWidth + 3 : 0);
        double y = Padding;
        bool full = false;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            row.Anchor = UIAnchor.TopLeft;
            if (FitWidth)
                row.Width = rowWidth;
            if (i < _first || full || (i > _first && y + row.Height > Height - Padding))
            {
                full |= i >= _first;
                row.OutOfView = true;
                continue;
            }
            row.OutOfView = false;
            row.X = Padding;
            row.Y = y;
            y += row.Height + Spacing;
        }
    }

    protected override void OnPress()
    {
        if (!CanScroll)
            return;
        double mx = Mouse.X, my = Mouse.Y;
        if (mx < BarX)
            return;
        UISounds.Play(Sound.snd_button_click);
        if (my < TrackTop)
            Scroll(-1);
        else if (my >= TrackTop + TrackLength)
            Scroll(1);
        else
            _dragging = true;
    }

    protected override bool OnWheel(int delta)
    {
        if (!CanScroll)
            return false;
        Scroll(-delta);
        return true;
    }

    protected override void OnDraw(double x, double y)
    {
        if (Background)
            UIDropdown.DrawBox(x, y, Width, Height);
        if (!CanScroll)
            return;
        double barX = BarX, mx = Mouse.X, my = Mouse.Y;
        bool overBar = IsHovered && mx >= barX;
        double upY = y + Padding, downY = y + Height - Padding - ArrowSize;
        Draw.Sprite(Sprite.s_gui_arrow_up, barX + 1, upY, ArrowFrame(overBar && my < upY + ArrowSize));
        Draw.Sprite(Sprite.s_gui_arrow_down, barX + 1, downY, ArrowFrame(overBar && my >= downY));
        double trackTop = TrackTop, trackLength = TrackLength;
        Draw.Rectangle(barX + 5, trackTop, barX + 5, trackTop + trackLength - 1, Draw.Rgb(67, 66, 77));
        double thumbY = trackTop + (trackLength - ThumbHeight) * (_maxFirst == 0 ? 0 : (double)_first / _maxFirst);
        bool overThumb = overBar && my >= thumbY && my < thumbY + ThumbHeight;
        Draw.Sprite(Sprite.s_scrollbar_vertical, barX, thumbY, overThumb || _dragging ? 1 : 0);
    }

    private int ArrowFrame(bool over) => over ? IsPressed ? 1 : 2 : 0;
}
