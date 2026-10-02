namespace StoneForge.Patcher;

/// <summary>The game's hotkey checks (every bound control goes through one) see no keys while a mod's text box
/// has the focus (StoneForge's InputBlock sets global.stonemod_typing): GML\Input\hotkey_guard.gml at the top
/// of each. Plain GML - no call into C# - since the game runs these many times a frame.</summary>
internal static class HotkeyGuard
{
    private static readonly string[] KeyChecks = { "scr_check_keyboard_array", "scr_check_keyboard_pressed_array", "scr_check_keyboard_released_array" };

    public static void Add(GameDataEditor editor)
    {
        string guard = LoaderGml.Read("Input/hotkey_guard.gml");
        foreach (string name in KeyChecks)
            ScriptEditor.InsertAtBodyStart(editor, name, guard);
    }
}
