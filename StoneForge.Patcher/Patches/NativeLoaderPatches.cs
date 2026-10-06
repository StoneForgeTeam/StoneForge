using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>What the loader adds to the game data on the game's native (YYC) build - its GML compiled into the exe, so
/// none can be added: its objects only, with no code, each under a parent of the game's whose events it then has (the
/// loader hooks those for ours - StoneForge's NativeHost). o_stonemod_gui under o_cursorController (its Draw GUI: the
/// mods' pass, made at the front); o_stonemod_hud under o_disclaimer (its Draw: the mods' HUD pass, at the HUD's depth);
/// o_stonemod_modal under o_presset_town_encounter (its user event 15: Escape on a mod window); o_stonemod_blocker a bare
/// c_GUI, as on the VM build. Scripts are hooked by detours there, not here.</summary>
internal static class NativeLoaderPatches
{
    public static void Apply(GameDataEditor editor)
    {
        Hosted(editor, "o_stonemod_gui", "o_cursorController");
        Hosted(editor, "o_stonemod_hud", "o_disclaimer");
        Hosted(editor, "o_stonemod_modal", "o_presset_town_encounter").Visible = false;
        InputBlocker.Add(editor);
        PatcherConsole.Log("  the native build: the loader's objects added (no code)");
    }

    private static UndertaleGameObject Hosted(GameDataEditor editor, string name, string parent)
    {
        var obj = GameObjects.Add(editor, name, parent, "s_point");
        obj.Sprite = null;
        obj.Persistent = true;
        return obj;
    }
}
