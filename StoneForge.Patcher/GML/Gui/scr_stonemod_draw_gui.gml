// The Draw GUI pass for C# mods (ModContext.DrawGui). Handled in C# (StoneForge, through the script
// hook channel).
function scr_stonemod_draw_gui()
{
    return string_concat("__stonemod_script__", "scr_stonemod_draw_gui", []);
}
