namespace StoneForge;

/// <summary>A text box, as the game's text input: click it to type (it takes GameMaker's typed text while it
/// has the focus); Enter submits, Esc or a click elsewhere lets go.</summary>
public class UITextBox : UIElement
{
    private static UITextBox? _focused;
    private string _text = "";
    private double _blink;
    private int _caret, _anchor, _firstShown;
    private string _typedBuffer = "";
    private int _repeatKey = -1;
    private double _repeatWait;
    public int CaretPosition => _caret;
    public int SelectionStart => Math.Min(_caret, _anchor);
    public int SelectionLength => Math.Abs(_caret - _anchor);

    public string Text
    {
        get => _text;
        set
        {
            _text = value.Length > MaxLength ? value[..MaxLength] : value;
            _caret = _anchor = _text.Length;
            if (IsFocused) SyncInput();
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
        _caret = _anchor = _text.Length; SyncInput();
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
        bool control = Keyboard.Down(Keyboard.Control), shift = Keyboard.Down(Keyboard.Shift);
        string typed = Keyboard.Typed;
        bool nativeChanged = typed != _typedBuffer;
        if (control && Keyboard.Pressed('V'))
        {
            var clipboard = Game.CallBuiltinTrusted("clipboard_get_text", default, default);
            if (clipboard.Kind == GmKind.String) Insert(clipboard.AsString);
            else SyncInput();
        }
        else if (control && Keyboard.Pressed('A')) { _anchor = 0; _caret = _text.Length; SyncInput(); }
        else if (control && (Keyboard.Pressed('C') || Keyboard.Pressed('X')))
        {
            if (SelectionLength > 0)
            {
                Game.CallBuiltinTrusted("clipboard_set_text", default, default, _text.Substring(SelectionStart, SelectionLength));
                if (Keyboard.Pressed('X')) Insert("");
            }
            SyncInput();
        }
        else if (typed != _typedBuffer)
        {
            if (SelectionLength > 0)
            {
                int shared = 0;
                while (shared < typed.Length && shared < _typedBuffer.Length && typed[shared] == _typedBuffer[shared]) shared++;
                Insert(typed[shared..]);
            }
            else
            {
                string suffix = _text[_caret..];
                int room = Math.Max(0, MaxLength - suffix.Length);
                string prefix = typed[..Math.Min(typed.Length, room)];
                Change(prefix + suffix, prefix.Length);
            }
        }
        if (Repeat(Keyboard.ArrowLeft, deltaTime)) Move(SelectionLength > 0 && !shift ? SelectionStart : Previous(_caret), shift);
        if (Repeat(Keyboard.ArrowRight, deltaTime)) Move(SelectionLength > 0 && !shift ? SelectionStart + SelectionLength : Next(_caret), shift);
        if (Keyboard.Pressed(36)) Move(0, shift); // Home
        if (Keyboard.Pressed(35)) Move(_text.Length, shift); // End
        if (Keyboard.Pressed(Keyboard.Delete))
        { if (SelectionLength == 0) _anchor = Next(_caret); Insert(""); }
        if (Keyboard.Pressed(Keyboard.Backspace) && !nativeChanged)
        { if (SelectionLength == 0) _anchor = Previous(_caret); Insert(""); }
        _blink += deltaTime;
        if (Keyboard.Pressed(Keyboard.Enter))
        {
            Submitted?.Invoke(_text);
            Blur();
        }
    }
    private bool Repeat(int key, double deltaTime)
    {
        if (Keyboard.Pressed(key)) { _repeatKey = key; _repeatWait = 0.4; return true; }
        if (_repeatKey != key) return false;
        if (!Keyboard.Down(key)) { _repeatKey = -1; return false; }
        _repeatWait -= deltaTime;
        if (_repeatWait > 0) return false;
        _repeatWait += 0.05; return true;
    }
    private void SyncInput() { _typedBuffer = _text[.._caret]; Keyboard.Typed = _typedBuffer; _blink = 0; }
    private void Move(int position, bool extend) { _caret = position; if (!extend) _anchor = _caret; SyncInput(); }
    private int Previous(int at) => at > 1 && char.IsLowSurrogate(_text[at - 1]) && char.IsHighSurrogate(_text[at - 2]) ? at - 2 : Math.Max(0, at - 1);
    private int Next(int at) => at + 1 < _text.Length && char.IsHighSurrogate(_text[at]) && char.IsLowSurrogate(_text[at + 1]) ? at + 2 : Math.Min(_text.Length, at + 1);
    private void Insert(string text)
    {
        int start = SelectionStart, length = SelectionLength;
        int room = Math.Max(0, MaxLength - (_text.Length - length));
        text = text[..Math.Min(text.Length, room)];
        Change(_text[..start] + text + _text[(start + length)..], start + text.Length);
    }
    private void Change(string text, int caret)
    {
        bool changed = _text != text; _text = text; _caret = _anchor = caret; SyncInput();
        if (changed) TextChanged?.Invoke(_text);
    }

    // The end of the text that fits in the box.
    private string Shown(double room)
    {
        _firstShown = Math.Min(_firstShown, _caret);
        while (_firstShown < _caret && Draw.TextWidth(_text[_firstShown.._caret]) > room) _firstShown++;
        string shown = _text[_firstShown..];
        while (shown.Length > 0 && Draw.TextWidth(shown) > room) shown = shown[..^1];
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
        int first = Math.Max(SelectionStart, _firstShown), last = Math.Min(SelectionStart + SelectionLength, _firstShown + shown.Length);
        if (last > first)
            Draw.Rectangle(x + 4 + Draw.TextWidth(_text[_firstShown..first]), y + 2,
                x + 4 + Draw.TextWidth(_text[_firstShown..last]), y + Height - 3, Draw.Rgb(88, 83, 123));
        Draw.Text(x + 4, textY, shown, Enabled ? Draw.White : Draw.Muted, Draw.AlignLeft, Draw.AlignMiddle);
        if (IsFocused && _blink % 1 < 0.5)
        {
            double caretX = x + 5 + Draw.TextWidth(_text[_firstShown.._caret]);
            Draw.Rectangle(caretX, y + 3, caretX, y + Height - 4, Draw.White);
        }
    }
}
