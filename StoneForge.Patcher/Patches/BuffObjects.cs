using UndertaleModLib.Models;

namespace StoneForge.Patcher;

/// <summary>Mods' buffs and debuffs (StoneForge's Buffs): one effect object each, as the game's own effects
/// (a magical buff / debuff - a debuff's duration shortened by Fortitude). Their events only run the parent's
/// (GML\Buffs); the loader hooks them by name and makes each instance the mod's buff - its name, icon, stats (its
/// data map) and number, kept in save_counter, which saves restore before user event 5.</summary>
internal static class BuffObjects
{
    public static void Add(GameDataEditor editor)
    {
        string inherited = LoaderGml.Read("Buffs/stonemod_buff_inherited.gml");
        foreach (var (effect, parent) in new[] { ("o_stonemod_buff", "o_magical_buff"), ("o_stonemod_debuff", "o_magical_debuff") })
        {
            GameObjects.Add(editor, effect, parent, "s_point");
            editor.AddNewEvent(effect, inherited, EventType.Create, 0);
            editor.AddNewEvent(effect, inherited, EventType.Other, 10);
            editor.AddNewEvent(effect, inherited, EventType.Other, 15);
            editor.AddNewEvent(effect, inherited, EventType.Destroy, 0);
        }
    }
}
