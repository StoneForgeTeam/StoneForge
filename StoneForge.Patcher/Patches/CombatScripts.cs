namespace StoneForge.Patcher;

/// <summary>Mods' damage (StoneForge's Combat.Damage) - GML in GML\Combat:
///   scr_stonemod_damage - damage of some types, from someone, dealt as the game deals it (o_damage_dealer).</summary>
internal static class CombatScripts
{
    public static void Add(GameDataEditor editor)
        => editor.AddFunction(LoaderGml.Read("Combat/scr_stonemod_damage.gml"), "scr_stonemod_damage");
}
