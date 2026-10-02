namespace StoneForge;

/// <summary>A column of elements scrolled smoothly, as the game's Settings page: added elements go down it
/// (their <see cref="UIElement.X"/> is an indent), cut at its edges, and once they don't all fit it scrolls
/// with the game's scrollbar at its right - the wheel over it, the arrows, the thumb (drag it) and the track.
/// <see cref="UIWindow.Page"/> is one; <see cref="AddHeader"/>, <see cref="AddText"/>... fill it as a settings
/// page.</summary>
public class UIScrollArea : UIElement
{
    // The scrollbar beside the content (as the settings menu's): its track's width, the thumb's height, the
    // arrows' size and where they sit.
    private const double BarSpace = 15, ThumbHeight = 21, ArrowSize = 9;

    private double _offset, _speed, _contentHeight, _dragOffset;
    private enum Part { None, Up, Down, Thumb, Track }
    private Part _held;
    private bool _laidOut;
    private readonly Dictionary<UIElement, double> _indents = new(ReferenceEqualityComparer.Instance);

    /// <summary>Space around the column's elements.</summary>
    public double Padding { get; set; } = 5;
    /// <summary>Space between them (none under a <see cref="UIHeader"/>).</summary>
    public double Spacing { get; set; } = 8;
    /// <summary>The width its elements have: less the scrollbar.</summary>
    public double ContentWidth => Width - BarSpace;
    /// <summary>How far down it's scrolled.</summary>
    public double ScrollOffset
    {
        get => _offset;
        set => _offset = Math.Clamp(value, 0, MaxScroll);
    }
    public double MaxScroll => Math.Max(0, _contentHeight - Height);
    public bool CanScroll => MaxScroll > 0;

    public UIScrollArea()
    {
        ClipChildren = true;
        Width = 325;
        Height = 247;
    }

    public UIScrollArea(double x, double y, double width, double height) : this()
    {
        X = x; Y = y; Width = width; Height = height;
    }

    protected override (double X, double Y, double Width, double Height) ClipArea => (ScreenX, ScreenY, ContentWidth, Height);

    /// <summary>Scrolls so this far down shows at its top.</summary>
    public void ScrollTo(double offset)
    {
        _speed = 0;
        Layout();
        ScrollOffset = offset;
    }

    /// <summary>Scrolls so <paramref name="element"/> (one of its own) is in the middle, where it can be.</summary>
    public void ScrollToShow(UIElement element)
    {
        Layout();
        ScrollTo(element.Y + _offset - (Height - element.Height) / 2);
    }

    /// <summary>Empties it, back at its top.</summary>
    public new void Clear()
    {
        base.Clear();
        _indents.Clear();
        _offset = _speed = 0;
    }

    // ---- filling it as a settings page ----

    /// <summary>A section header (the Settings menu's), across it.</summary>
    public UIHeader AddHeader(string text) => Add(new UIHeader(text));

    /// <summary>Text (the game's), wrapped at its width, in <paramref name="colour"/> (default white).</summary>
    public UILabel AddText(string text, int? colour = null)
        => Add(new UILabel(text, Indent, 0, colour ?? Draw.White) { Wrap = true });

    /// <summary>A picture, fitted into a <paramref name="box"/>-sized square (small pixel art scaled up whole).</summary>
    public UIImage AddImage(int sprite, double box = 64)
    {
        double scale = 1;
        double size = Math.Max(Draw.SpriteWidth(sprite), Draw.SpriteHeight(sprite));
        if (size > 0)
        {
            scale = box / size;
            if (scale >= 1)
                scale = Math.Floor(scale);
        }
        return Add(new UIImage(sprite, Indent, 0, scale));
    }

    /// <summary>A checkbox (the game's), with <paramref name="tooltip"/> in the game's hover frame.</summary>
    public UICheckbox AddCheckbox(string text, bool isChecked, string? tooltip = null)
        => Add(new UICheckbox(text, Indent, 0, isChecked) { Tooltip = tooltip });

    // Under a header, a settings page's rows are set in a little.
    private double Indent => Children.Any(c => c is UIHeader) ? 5 : 0;

    // ---- the column ----

    // Its elements down it - each at its indent (its X, as added), wrapped text and headers made its width -
    // from where it's scrolled to.
    private void Layout()
    {
        double y = Padding - _offset;
        double height = Padding;
        UIElement? previous = null;
        foreach (var child in Children)
        {
            if (!child.Visible)
                continue;
            if (!_indents.TryGetValue(child, out double indent))
                _indents[child] = indent = child.X;
            if (previous != null)
            {
                double gap = previous is UIHeader ? 0 : Spacing;
                y += gap;
                height += gap;
            }
            child.Anchor = UIAnchor.TopLeft;
            child.X = Padding + indent;
            child.Y = y;
            if (child is UIHeader)
                child.Width = ContentWidth - Padding * 2 - indent;
            else if (child is UILabel { Wrap: true } label && label.Width <= 0)
                label.Width = ContentWidth - Padding * 2 - indent - 5;
            y += child.Height;
            height += child.Height;
            previous = child;
        }
        _contentHeight = height + Padding;
        _offset = Math.Clamp(_offset, 0, MaxScroll);
        _laidOut = true;
    }

    protected override void OnUpdate(double deltaTime)
    {
        double frames = Math.Min(deltaTime, 0.25) * 60;
        if (_held != Part.None && !Mouse.Down())
            _held = Part.None;
        // Held arrows (or the track) push it along, as the game's: a pixel a frame more each frame.
        if (_held is Part.Up or Part.Down)
            _speed += (_held == Part.Up ? -1 : 1) * frames;
        else if (_held == Part.Track)
        {
            double thumb = ThumbTop + ThumbHeight / 2, mouse = Mouse.Y - ScreenY;
            if (Math.Abs(mouse - thumb) > ThumbHeight / 2)
                _speed += (mouse < thumb ? -1 : 1) * frames;
            else
                _held = Part.None;
        }
        else if (_held == Part.Thumb && PositionEnd > 0)
            _offset = Math.Clamp((Mouse.Y - ScreenY - _dragOffset - 13) / PositionEnd, 0, 1) * MaxScroll;
        if (_speed != 0)
        {
            ScrollOffset = _offset + _speed * frames;
            _speed *= Math.Pow(0.75, frames);
            if (Math.Abs(_speed) < 0.05)
                _speed = 0;
        }
    }

    protected override bool OnWheel(int delta)
    {
        if (!CanScroll)
            return false;
        // (A wheel tick: 4 pixels a frame, slowing - about 16 pixels, as the game's.)
        _speed -= delta * 4;
        return true;
    }

    // ---- its scrollbar ----

    // The thumb's travel (as o_scrollbar_vertical: its track less its height, plus 2) and where it is.
    private double PositionEnd => Height - 28 - ThumbHeight + 2;
    private double ThumbTop => 13 + (MaxScroll > 0 ? _offset / MaxScroll * PositionEnd : 0);

    private Part PartAt(double mx, double my)
    {
        double x = mx - ScreenX - ContentWidth, y = my - ScreenY;
        if (!CanScroll || x < 0 || x >= BarSpace)
            return Part.None;
        if (y >= 2 && y < 2 + ArrowSize)
            return Part.Up;
        if (y >= Height - 11 && y < Height - 11 + ArrowSize)
            return Part.Down;
        double thumb = ThumbTop;
        if (y >= thumb && y < thumb + ThumbHeight)
            return Part.Thumb;
        return y >= 13 && y < Height - 13 ? Part.Track : Part.None;
    }

    protected override void OnPress()
    {
        _held = PartAt(Mouse.X, Mouse.Y);
        if (_held == Part.None)
            return;
        if (_held == Part.Thumb)
            _dragOffset = Mouse.Y - ScreenY - ThumbTop;
        _speed = 0;
        UISounds.Play(Sound.snd_button_click);
    }

    protected override void OnDraw(double x, double y)
    {
        // (Laid out here as well as before its elements were updated: added this frame, they're placed now.)
        Layout();
    }

    protected override void OnDrawAfter(double x, double y)
    {
        if (!_laidOut)
            return;
        double barX = x + ContentWidth;
        int track = (int)Sprite.s_settings_scrollbar_track;
        Game.CallBuiltin("draw_sprite_ext", track, 0, barX, y - 1, 1, (Height + 2) / Draw.SpriteHeight(track), 0, Draw.White, 1);
        if (!CanScroll)
            return;
        var hover = IsHovered ? PartAt(Mouse.X, Mouse.Y) : Part.None;
        Draw.Sprite((int)Sprite.s_scrollbar_vertical, barX + 2, y + ThumbTop, 1, hover == Part.Thumb || _held == Part.Thumb ? 1 : 0);
        Draw.Sprite((int)Sprite.s_gui_arrow_up, barX + 3, y + 2, 1, ArrowFrame(Part.Up, hover));
        Draw.Sprite((int)Sprite.s_gui_arrow_down, barX + 3, y + Height - 11, 1, ArrowFrame(Part.Down, hover));
    }

    // A game button's frames: 1 pressed, 2 lit, 0 idle.
    private int ArrowFrame(Part arrow, Part hover) => _held == arrow ? 1 : hover == arrow ? 2 : 0;
}
