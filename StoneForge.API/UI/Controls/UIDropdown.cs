namespace StoneForge;

/// <summary>A dropdown, as the game's Settings comboboxes: shows the chosen option; click it for the list
/// (drawn over everything), pick one with the mouse (the wheel scrolls a long list).</summary>
public class UIDropdown : UIElement
{
    private int _selected = -1;
    private DropdownList? _list;
    private UIScreen? _listScreen;

    public List<string> Options { get; } = new();
    /// <summary>The chosen option (-1: none).</summary>
    public int SelectedIndex
    {
        get => _selected;
        set => _selected = Options.Count == 0 ? -1 : Math.Clamp(value, -1, Options.Count - 1);
    }
    public string? Selected => _selected >= 0 && _selected < Options.Count ? Options[_selected] : null;
    /// <summary>The most rows the open list shows at once.</summary>
    public int MaxVisibleRows { get; set; } = 10;
    public bool IsOpen => _list != null;
    /// <summary>An option was picked (its index).</summary>
    public event Action<int>? Changed;

    public UIDropdown() { Width = 125; Height = 15; }
    public UIDropdown(IEnumerable<string> options, double x, double y, double width = 125, int selected = 0) : this()
    {
        Options.AddRange(options);
        X = x; Y = y; Width = width; SelectedIndex = selected;
    }

    public void Open()
    {
        var screen = Screen;
        if (_list != null || screen == null || Options.Count == 0)
            return;
        _list = new DropdownList(this);
        _listScreen = screen;
        screen.ShowOverlay(_list, this);
        UISounds.Play(Sound.snd_combobox_on, 4);
    }

    public void Close() => Close(sound: true);

    // (Its screen left its context.)
    internal void CloseQuietly() => Close(sound: false);

    private void Close(bool sound)
    {
        if (_list == null)
            return;
        _listScreen?.HideOverlay(_list);
        _list = null;
        _listScreen = null;
        if (sound)
            UISounds.Play(Sound.snd_combobox_off, 4);
    }

    internal void Pick(int index)
    {
        Close();
        if (index == _selected)
            return;
        SelectedIndex = index;
        Changed?.Invoke(index);
    }

    protected override void OnMouseEnter() => UISounds.Hover();

    protected override void OnClick()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    protected override void OnDraw(double x, double y)
    {
        DrawBox(x, y, Width, Height);
        Draw.Text(x + 5, y + 2, Selected ?? "", Enabled ? Draw.White : Draw.Muted);
        int frame = IsPressed ? 1 : IsHovered ? 2 : 0;
        Draw.Sprite(IsOpen ? Sprite.s_gui_arrow_up : Sprite.s_gui_arrow_down, x + Width - 10, y + 1, frame);
    }

    // The game's combobox box: its dark background with a grey line one pixel in.
    internal static readonly int BoxBack = Draw.Rgb(17, 16, 26), BoxLine = Draw.Rgb(67, 66, 77);

    internal static void DrawBox(double x, double y, double width, double height)
    {
        Draw.Rectangle(x, y, x + width - 1, y + height - 1, BoxBack);
        Draw.Rectangle(x + 1, y + 1, x + width - 2, y + height - 2, BoxLine, outline: true);
    }

    // The open list, under the box, over everything (a screen overlay).
    private sealed class DropdownList : UIElement
    {
        private const double RowHeight = 11;
        private readonly UIDropdown _owner;
        private int _first;

        public DropdownList(UIDropdown owner)
        {
            _owner = owner;
            _first = Math.Max(0, owner.SelectedIndex - owner.MaxVisibleRows + 1);
        }

        private int Rows => Math.Max(1, Math.Min(_owner.Options.Count, _owner.MaxVisibleRows));

        protected override void OnUpdate(double deltaTime)
        {
            // Its dropdown hidden or taken off the screen (and so no longer updated): it goes.
            if (!_owner.IsShown || _owner.Screen == null)
            {
                _owner.Close(sound: false);
                return;
            }
            X = _owner.ScreenX;
            Y = _owner.ScreenY + _owner.Height - 1;
            Width = _owner.Width;
            Height = Rows * RowHeight + 3;
            _first = Math.Clamp(_first, 0, Math.Max(0, _owner.Options.Count - Rows));
            double mx = Mouse.X, my = Mouse.Y;
            if ((Mouse.Pressed() && !Contains(mx, my) && !_owner.Contains(mx, my)) || Keyboard.Pressed(Keyboard.Escape))
                _owner.Close();
        }

        private int RowAt(double my) => (int)Math.Floor((my - ScreenY - 1) / RowHeight);

        protected override void OnClick()
        {
            int row = RowAt(Mouse.Y);
            if (row >= 0 && row < Rows && _first + row < _owner.Options.Count)
                _owner.Pick(_first + row);
        }

        protected override bool OnWheel(int delta)
        {
            _first = Math.Clamp(_first - delta, 0, Math.Max(0, _owner.Options.Count - Rows));
            return true;
        }

        protected override void OnDraw(double x, double y)
        {
            DrawBox(x, y, Width, Height);
            int hoveredRow = IsHovered ? RowAt(Mouse.Y) : -1;
            for (int row = 0; row < Rows && _first + row < _owner.Options.Count; row++)
            {
                int index = _first + row;
                double rowY = y + 1 + row * RowHeight;
                if (row == hoveredRow)
                    Draw.Rectangle(x + 2, rowY, x + Width - 3, rowY + RowHeight - 1, Draw.Rgb(144, 144, 155), 0.15);
                Draw.Text(x + 4, rowY, _owner.Options[index], index == _owner.SelectedIndex ? Draw.White : Draw.Muted);
            }
            // More above / below: the game's small arrows at the right.
            if (_first > 0)
                Draw.Sprite(Sprite.s_gui_arrow_up, x + Width - 11, y + 2);
            if (_first + Rows < _owner.Options.Count)
                Draw.Sprite(Sprite.s_gui_arrow_down, x + Width - 11, y + Height - 11);
        }
    }
}
