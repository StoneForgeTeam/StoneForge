using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>What the loader adds to the game data on the game's native (YYC) build - its GML compiled into the exe, so
/// none can be added: its objects only, with no code, each under a parent of the game's whose events it then has (the
/// loader hooks those for ours - StoneForge's NativeHost). o_stonemod_gui under o_cursorController (its Draw GUI: the
/// mods' pass, made at the front); o_stonemod_hud under o_disclaimer (its Draw: the mods' HUD pass, at the HUD's depth);
/// o_stonemod_modal under o_presset_town_encounter (its user event 15: Escape on a mod window); o_stonemod_blocker a bare
/// c_GUI, as on the VM build; o_stonemod_buff / _debuff and o_stonemod_fx under the game's buffs and effect animation,
/// whose events they run. Scripts are hooked by detours there, not here.</summary>
internal static class NativeLoaderPatches
{
    public static void Apply(GameDataEditor editor)
    {
        Hosted(editor, "o_stonemod_gui", "o_cursorController");
        Hosted(editor, "o_stonemod_hud", "o_disclaimer");
        Hosted(editor, "o_stonemod_modal", "o_presset_town_encounter").Visible = false;
        InputBlocker.Add(editor);
        // Mods' buffs and visual effects: plain children of the game's own (their events the parents'; the loader hooks
        // those for them - StoneForge's ObjectEvents).
        GameObjects.Add(editor, "o_stonemod_buff", "o_magical_buff", "s_point");
        GameObjects.Add(editor, "o_stonemod_debuff", "o_magical_debuff", "s_point");
        GameObjects.Add(editor, "o_stonemod_fx", "c_buff_anim", "s_point");
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
