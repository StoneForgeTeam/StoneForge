namespace StoneForge.Patcher;

/// <summary>Everything the loader adds to the game data, in the order it's added. Scripts and objects come
/// before the events that use them (an event is compiled when added, against what's there); after that the order
/// is only kept so the same patcher builds the same data.win, byte for byte.</summary>
internal static class LoaderPatches
{
    public static void Apply(GameDataEditor editor)
    {
        ItemScripts.Add(editor);
        CombatScripts.Add(editor);
        GuiObject.Add(editor);
        InputBlocker.Add(editor);
        FxObject.Add(editor);
        BuffObjects.Add(editor);
        HotkeyGuard.Add(editor);
        PatcherConsole.Log("  hotkeys held off while a mod's text box is typed in: added");
    }
}
