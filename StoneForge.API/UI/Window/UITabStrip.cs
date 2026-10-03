namespace StoneForge;

/// <summary>A row or column of tabs (<see cref="UITab"/>), one of them open: the game's Settings menu has one down its
/// left. A column holds tabs of <see cref="TabHeight"/> one under another - more than fit scroll, beside a scrollbar; a
/// row shares its width between them. Set them with <see cref="SetTabs(string[])"/>, open one with
/// <see cref="UITab.Open"/>, and show what's in the open one on <see cref="TabOpened"/>:
/// <code>
/// var tabs = Content.Add(new UITabStrip(0, 0, 100, Content.Height));
/// tabs.TabOpened += tab => ShowPage(tab.Index);
/// tabs.SetTabs("General", "Advanced");
/// tabs.Tabs[0].Open();
/// </code></summary>
public class UITabStrip : UIElement
{
    private readonly List<UITab> _tabs = new();
    private UIScrollArea? _scroll;

    public UITabStrip() { Width = 100; Height = 247; HitTest = false; }

    public UITabStrip(double x, double y, double width, double height, bool vertical = true) : this()
    {
        X = x; Y = y; Width = width; Height = height;
        Vertical = vertical;
    }

    /// <summary>A column (default) or a row.</summary>
    public bool Vertical { get; set; } = true;
    /// <summary>A tab's height (default 26, the game's).</summary>
    public double TabHeight { get; set; } = 26;
    /// <summary>The tabs' sprite, stretched to them but for its ends: frames 0 idle, 1 pressed, 2 lit and open
    /// (default the Settings menu's).</summary>
    public int TabSprite { get; set; } = (int)Sprite.s_settings_button_down;

    public IReadOnlyList<UITab> Tabs => _tabs;
    /// <summary>The open tab (none before one's opened).</summary>
    public UITab? Selected { get; private set; }

    /// <summary>A tab was opened (clicked, or <see cref="UITab.Open"/>).</summary>
    public event Action<UITab>? TabOpened;

    /// <summary>Its tabs, in place of any it had (none open).</summary>
    public IReadOnlyList<UITab> SetTabs(params string[] names) => SetTabs((IEnumerable<string>)names);

    public IReadOnlyList<UITab> SetTabs(IEnumerable<string> names)
    {
        var list = names.ToList();
        Clear();
        _tabs.Clear();
        _scroll = null;
        Selected = null;
        if (Vertical && list.Count * TabHeight > Height)
        {
            // (Narrowed for the scrollbar at its right.)
            _scroll = Add(new UIScrollArea(0, 0, Width, Height) { Padding = 0, Spacing = 0 });
            for (int i = 0; i < list.Count; i++)
                _tabs.Add(_scroll.Add(new UITab(this, list[i], i) { Width = Width - 17, Height = TabHeight }));
        }
        else
        {
            double width = Vertical ? Width : list.Count == 0 ? 0 : Math.Floor(Width / list.Count);
            for (int i = 0; i < list.Count; i++)
                _tabs.Add(Add(new UITab(this, list[i], i)
                {
                    Width = width,
                    Height = TabHeight,
                    X = Vertical ? 0 : width * i,
                    Y = Vertical ? TabHeight * i : 0,
                }));
        }
        return _tabs;
    }

    // A tab opened: shown as the open one, scrolled to (in the middle where it can be) if they scroll.
    internal void Select(UITab tab)
    {
        Selected = tab;
        _scroll?.ScrollToShow(tab);
        try { TabOpened?.Invoke(tab); }
        catch (Exception e) { Game.Log($"Tab \"{tab.Text}\" TabOpened handler threw: {e}"); }
    }
}
