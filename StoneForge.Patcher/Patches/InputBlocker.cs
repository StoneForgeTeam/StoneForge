namespace StoneForge.Patcher;

/// <summary>The loader's input blocker (StoneForge's InputBlock): o_stonemod_blocker, a bare c_GUI - no
/// events - put over the mod UI the mouse is on, so the game's GUI and world take it for its own GUI and leave
/// the click alone.</summary>
internal static class InputBlocker
{
    public static void Add(GameDataEditor editor)
    {
        var blocker = GameObjects.Add(editor, "o_stonemod_blocker", "c_GUI", "s_point");
        blocker.Visible = false;
        blocker.Persistent = true;
    }
}
