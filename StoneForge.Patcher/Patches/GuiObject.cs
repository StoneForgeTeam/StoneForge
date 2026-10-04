using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>The mods' Draw GUI pass (StoneForge's ModContext.DrawGui and its UI) - GML in GML\Gui:
/// o_stonemod_gui, one instance (persistent, kept by the loader in every room) whose Draw GUI End - after the
/// game's own GUI drawing - calls into C# (scr_stonemod_draw_gui), then draws the game's cursor again on top;
/// scr_stonemod_gui_matrix scales the pass to the game's UI scale; scr_stonemod_clip_begin / _end clip mod UI
/// through a surface; o_stonemod_modal stands for an open mod window in the game's escape list; o_stonemod_hud's
/// Draw is the mods' HUD pass (hooked from C#), made at the game's HUD depth - under its windows.</summary>
internal static class GuiObject
{
    public static void Add(GameDataEditor editor)
    {
        var gui = GameObjects.Add(editor, "o_stonemod_gui", "o_guiSimple", "s_point");
        gui.ParentId = null;
        gui.Sprite = null;
        gui.Persistent = true;
        editor.AddFunction(LoaderGml.Read("Gui/scr_stonemod_draw_gui.gml"), "scr_stonemod_draw_gui");
        editor.AddFunction(LoaderGml.Read("Gui/scr_stonemod_gui_matrix.gml"), "scr_stonemod_gui_matrix");
        editor.AddNewEvent("o_stonemod_gui", LoaderGml.Read("Gui/stonemod_gui_draw.gml"), EventType.Draw, 75);
        // The mods' HUD pass (ModContext.DrawHud, ModUI.Hud): o_stonemod_hud's Draw, at the depth the loader makes it
        // at. The game draws its own UI in the Draw pass too, by depth, so its windows (inventory -12100, the Esc menu
        // -12400) draw over it - in Draw GUI it would be over everything. The event does nothing itself: the loader
        // hooks it (Hooks.DrawHud).
        var hud = GameObjects.Add(editor, "o_stonemod_hud", "o_guiSimple", "s_point");
        hud.ParentId = null;
        hud.Sprite = null;
        hud.Persistent = true;
        editor.AddNewEvent("o_stonemod_hud", "exit", EventType.Draw, 0);
        // Mod UI that clips what's in it (UIElement.ClipChildren), through a surface.
        editor.AddFunction(LoaderGml.Read("Gui/scr_stonemod_clip_begin.gml"), "scr_stonemod_clip_begin");
        editor.AddFunction(LoaderGml.Read("Gui/scr_stonemod_clip_end.gml"), "scr_stonemod_clip_end");
        // A mod window's place in the game's escape list (UIWindow): o_stonemod_modal, which Escape closes.
        var modal = GameObjects.Add(editor, "o_stonemod_modal", "o_guiSimple", "s_point");
        modal.ParentId = null;
        modal.Sprite = null;
        modal.Visible = false;
        modal.Persistent = true;
        editor.AddFunction(LoaderGml.Read("Gui/scr_stonemod_gui_event.gml"), "scr_stonemod_gui_event");
        editor.AddNewEvent("o_stonemod_modal", LoaderGml.Read("Gui/modal_close.gml"), EventType.Other, 25);
    }
}
