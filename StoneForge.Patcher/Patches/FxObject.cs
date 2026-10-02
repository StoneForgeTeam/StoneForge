using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>Mods' visual effects (StoneForge's Fx) - GML in GML\Fx: o_stonemod_fx, an animation on a unit as
/// the game's own effect animations are (c_buff_anim: follows its unit, drawn over it - or behind, "under" -
/// with a light). Played once, or looped (a buff's aura) until the loader takes it off; gone with its unit.</summary>
internal static class FxObject
{
    public static void Add(GameDataEditor editor)
    {
        GameObjects.Add(editor, "o_stonemod_fx", "c_buff_anim", "s_point");
        editor.AddNewEvent("o_stonemod_fx", LoaderGml.Read("Fx/stonemod_fx_create.gml"), EventType.Create, 0);
        editor.AddNewEvent("o_stonemod_fx", LoaderGml.Read("Fx/stonemod_fx_step.gml"), EventType.Step, 0);
        editor.AddNewEvent("o_stonemod_fx", LoaderGml.Read("Fx/stonemod_fx_animation_end.gml"), EventType.Other, 7);
    }
}
