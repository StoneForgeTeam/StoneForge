// A mod UI window's place in the game (o_stonemod_modal - self) tells C# something happened to it: "close"
// (Escape: the game's escape list sends it user event 15). Handled in C# (StoneForge's UIWindow, through the
// script hook channel).
function scr_stonemod_gui_event(argument0)
{
    return string_concat("__stonemod_script__", "scr_stonemod_gui_event", [argument0]);
}
