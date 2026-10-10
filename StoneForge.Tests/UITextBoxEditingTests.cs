using StoneForge;

public sealed class UITextBoxEditingTests : FakeGame
{
    private readonly ModContext _context = new("text_input_test");
    private readonly UITextBox _text;
    public UITextBoxEditingTests()
    {
        KeepGlobalWrites = true; Input = new FakeInput { KeyboardOnly = true };
        _text = _context.UI.InGame.Add(new UITextBox { Text = "abcd", MaxLength = 64 }); _text.Focus();
    }
    public override void Dispose() { UITextBox.ReleaseFocus(); Hooks.RemoveMod(_context.Id); base.Dispose(); }
    private void Key(int key) { Input!.PressedKeys.Add(key); _text.RunUpdate(0.1); Input.PressedKeys.Clear(); }
    [Fact]
    public void Multiline_enter_and_paste_keep_newlines_and_focus()
    {
        _text.Multiline = true;
        Key(Keyboard.Enter); Assert.Equal("abcd\n", _text.Text); Assert.True(_text.IsFocused);
        Input!.HeldKeys.Add(Keyboard.Control); Input.Clipboard = "two\r\nthree"; Key('V');
        Assert.Equal("abcd\ntwo\nthree", _text.Text);
        Input.HeldKeys.Clear(); Key(36); Assert.Equal(9, _text.CaretPosition);
        Key(Keyboard.ArrowUp); Assert.Equal(5, _text.CaretPosition);
    }
    [Theory]
    [InlineData("", 2, "")]
    [InlineData("ab\ncd", 2, "ab|cd")]
    [InlineData("abcd", 2, "ab|cd")]
    [InlineData("a\n", 2, "a|")]
    [InlineData("a\n\nb", 2, "a||b")]
    [InlineData("😀X", 1, "😀|X")]
    public void Multiline_wrap_preserves_all_text_and_unicode(string text, int width, string expected)
    {
        var lines = UITextBox.WrapLines(text, width, value => value.Length);
        Assert.Equal(expected, string.Join("|", lines.Select(line => text[line.Start..line.End])));
    }
    [Fact]
    public void Space_inserts_text_without_submitting_and_enter_submits()
    {
        int submissions = 0; _text.Submitted += _ => submissions++;
        Keyboard.Typed = "abcd "; Key(Keyboard.Space);
        Assert.Equal("abcd ", _text.Text); Assert.True(_text.IsFocused); Assert.Equal(0, submissions);
        Key(Keyboard.Enter);
        Assert.Equal(1, submissions); Assert.False(_text.IsFocused);
    }
    [Fact]
    public void Arrow_keys_move_the_caret_and_typing_and_paste_insert_in_the_middle()
    {
        Key(Keyboard.ArrowLeft); Key(Keyboard.ArrowLeft); Assert.Equal(2, _text.CaretPosition);
        Keyboard.Typed = "abX"; _text.RunUpdate(0.1); Assert.Equal("abXcd", _text.Text);
        Input!.HeldKeys.Add(Keyboard.Control); Input.Clipboard = "YZ"; Key('V');
        Assert.Equal("abXYZcd", _text.Text); Assert.Equal(5, _text.CaretPosition); Assert.Equal(1, Input.ClipboardReads);
        Input.HeldKeys.Clear(); Key(Keyboard.ArrowRight); Assert.Equal(6, _text.CaretPosition);
    }
    [Fact]
    public void Select_all_paste_cut_and_copy_preserve_selection_and_enforce_maximum_length()
    {
        Input!.HeldKeys.Add(Keyboard.Control); Key('A'); Assert.Equal(4, _text.SelectionLength);
        Key('C'); Assert.Equal("abcd", Input.Clipboard); Assert.Equal(0, Input.ClipboardReads);
        Key('X'); Assert.Equal("", _text.Text);
        _text.MaxLength = 5; Input.Clipboard = "0123456789"; Key('V'); Assert.Equal("01234", _text.Text);
        Key('A'); Keyboard.Typed = "01234Z"; Input.HeldKeys.Clear(); _text.RunUpdate(0.1);
        Assert.Equal("Z", _text.Text);
    }
    [Fact]
    public void Holding_an_arrow_repeats_after_the_initial_delay_and_shift_extends_selection()
    {
        Input!.HeldKeys.Add(Keyboard.ArrowLeft); Key(Keyboard.ArrowLeft); Assert.Equal(3, _text.CaretPosition);
        _text.RunUpdate(0.2); Assert.Equal(3, _text.CaretPosition);
        _text.RunUpdate(0.25); Assert.Equal(2, _text.CaretPosition);
        Input.HeldKeys.Clear(); Input.HeldKeys.Add(Keyboard.Shift); Key(Keyboard.ArrowLeft);
        Assert.Equal(1, _text.SelectionLength); Assert.Equal(1, _text.SelectionStart);
    }
    [Fact]
    public void Native_backspace_does_not_delete_twice_and_arrows_skip_surrogate_pairs()
    {
        Keyboard.Typed = "abc"; Key(Keyboard.Backspace); Assert.Equal("abc", _text.Text);
        Key(Keyboard.ArrowLeft); Key(Keyboard.Delete); Assert.Equal("ab", _text.Text);
        _text.Text = "A😀B"; Key(Keyboard.ArrowLeft); Assert.Equal(3, _text.CaretPosition);
        Key(Keyboard.ArrowLeft); Assert.Equal(1, _text.CaretPosition); Key(Keyboard.ArrowRight); Assert.Equal(3, _text.CaretPosition);
        Assert.Equal(0, Input!.ClipboardReads);
    }
}
