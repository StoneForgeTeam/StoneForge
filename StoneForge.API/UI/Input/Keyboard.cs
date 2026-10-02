namespace StoneForge;

/// <summary>The keyboard. Keys are GameMaker's codes: letters and digits are their capital character
/// (<c>'A'</c>, <c>'1'</c>), the rest are the constants here.</summary>
public static class Keyboard
{
    public const int Backspace = 8, Tab = 9, Enter = 13, Shift = 16, Control = 17, Alt = 18, Escape = 27, Space = 32,
        ArrowLeft = 37, ArrowUp = 38, ArrowRight = 39, ArrowDown = 40, Delete = 46,
        F1 = 112, F2 = 113, F3 = 114, F4 = 115, F5 = 116, F6 = 117, F7 = 118, F8 = 119, F9 = 120, F10 = 121, F11 = 122, F12 = 123;

    /// <summary>Pressed this frame.</summary>
    public static bool Pressed(int key) => Game.CallBuiltin("keyboard_check_pressed", key).AsBool;
    /// <summary>Released this frame.</summary>
    public static bool Released(int key) => Game.CallBuiltin("keyboard_check_released", key).AsBool;
    /// <summary>Held down.</summary>
    public static bool Down(int key) => Game.CallBuiltin("keyboard_check", key).AsBool;

    /// <summary>What's been typed (GameMaker's keyboard_string: typing adds to it, backspace takes off the
    /// end). <see cref="UITextBox"/> uses it while it has the focus.</summary>
    public static string Typed
    {
        get => Game.Global["keyboard_string"].AsString;
        set => Game.Global["keyboard_string"] = value;
    }
}
