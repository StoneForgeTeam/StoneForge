namespace StoneForge;

/// <summary>A text box, as the game's text input: click it to type (it takes GameMaker's typed text while it
/// has the focus); Enter submits, Esc or a click elsewhere lets go.</summary>
public class UITextBox : UIElement
{
    private static UITextBox? _focused;
    private string _text = "";
    private double _blink;
    private string? _shownFor, _shown;

    public string Text
    {
        get => _text;
        set
        {
            _text = value.Length > MaxLength ? value[..MaxLength] : value;
            if (IsFocused)
                Keyboard.Typed = _text;
        }
    }
    /// <summary>Shown (dimmed) while it's empty and not being typed in.</summary>
    public string Placeholder { get; set; } = "";
    public int MaxLength { get; set; } = 64;
    public bool IsFocused => _focused == this;
    /// <summary>The text changed (as it's typed).</summary>
    public event Action<string>? TextChanged;
    /// <summary>Enter was pressed (with the text).</summary>
    public event Action<string>? Submitted;

    public UITextBox() { Width = 120; Height = 15; }
    public UITextBox(double x, double y, double width = 120, string text = "", string placeholder = "") : this()
    {
        X = x; Y = y; Width = width; Text = text; Placeholder = placeholder;
    }

    /// <summary>Takes the keyboard (from any other text box).</summary>
    public void Focus()
    {
        if (IsFocused)
            return;
        _focused = this;
        Keyboard.Typed = _text;
        _blink = 0;
    }

    /// <summary>Lets go of the keyboard.</summary>
    public void Blur()
    {
        if (IsFocused)
            _focused = null;
    }

    // Whether any text box has the keyboard (the game's hotkeys are held off meanwhile - InputBlock).
    internal static bool AnyFocused => _focused != null;

    // (A mod switched off: whatever text box has the keyboard lets go - it may be the mod's.)
    internal static void ReleaseFocus() => _focused = null;

    // (A screen left its context: its text box, if one has the keyboard, lets go.)
    internal static void ReleaseFocusIn(UIScreen screen)
    {
        if (_focused != null && _focused.Screen == screen)
            _focused = null;
    }

    protected override void OnPress() => Focus();

    protected override void OnUpdate(double deltaTime)
    {
        if (!IsFocused)
            return;
        if (!IsShown || !Enabled || Screen == null || (Mouse.Pressed() && !IsHovered) || Keyboard.Pressed(Keyboard.Escape))
        {
            Blur();
            return;
        }
        string typed = Keyboard.Typed;
        if (typed.Length > MaxLength)
        {
            typed = typed[..MaxLength];
            Keyboard.Typed = typed;
        }
        if (typed != _text)
        {
            _text = typed;
            _blink = 0;
            TextChanged?.Invoke(_text);
        }
        _blink += deltaTime;
        if (Keyboard.Pressed(Keyboard.Enter))
        {
            Submitted?.Invoke(_text);
            Blur();
        }
    }

    // The end of the text that fits in the box.
    private string Shown(double room)
    {
        if (_shownFor == _text && _shown != null)
            return _shown;
        string shown = _text;
        while (shown.Length > 0 && Draw.TextWidth(shown) > room)
            shown = shown[1..];
        _shownFor = _text;
        _shown = shown;
        return shown;
    }

    protected override void OnDraw(double x, double y)
    {
        Draw.Rectangle(x, y, x + Width - 1, y + Height - 1, IsFocused ? Draw.Rgb(88, 83, 123) : Draw.Rgb(67, 66, 77));
        Draw.Rectangle(x + 1, y + 1, x + Width - 2, y + Height - 2, Draw.Rgb(37, 38, 49));
        Draw.Rectangle(x + 2, y + 2, x + Width - 3, y + Height - 3, Draw.Rgb(44, 45, 57));
        double textY = y + Height / 2;
        if (_text.Length == 0 && !IsFocused)
        {
            Draw.Text(x + 4, textY, Placeholder, Draw.Rgb(100, 90, 85), Draw.AlignLeft, Draw.AlignMiddle);
            return;
        }
        string shown = Shown(Width - 10);
        Draw.Text(x + 4, textY, shown, Enabled ? Draw.White : Draw.Muted, Draw.AlignLeft, Draw.AlignMiddle);
        if (IsFocused && _blink % 1 < 0.5)
        {
            double caretX = x + 5 + Draw.TextWidth(shown);
            Draw.Rectangle(caretX, y + 3, caretX, y + Height - 4, Draw.White);
        }
    }
}
