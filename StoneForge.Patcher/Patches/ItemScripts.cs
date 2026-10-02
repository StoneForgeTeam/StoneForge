
namespace StoneForge.Patcher;

/// <summary>Mods' items (StoneForge's Items.Add / Items.Give) - GML in GML\Items:
///   scr_stonemod_item_define - a mod's weapon or armour, a copy of a game item's table row with its changes;
///   scr_stonemod_item_give   - an item into the player's inventory, as the game's console command does;
///   scr_stonemod_item_sprite - one of its pictures (worn, corpse, equipped...) lined up as the game item's
///                              (scr_stonemod_item_sprite_like);
///   scr_stonemod_consum_define - a mod's consumable: a game consumable's stats row copied under its key.</summary>
internal static class ItemScripts
{
    public static void Add(GameDataEditor editor)
    {
        editor.AddFunction(LoaderGml.Read("Items/scr_stonemod_item_define.gml"), "scr_stonemod_item_define");
        editor.AddFunction(LoaderGml.Read("Items/scr_stonemod_item_give.gml"), "scr_stonemod_item_give");
        editor.AddFunction(LoaderGml.Read("Items/scr_stonemod_item_sprite_like.gml"), "scr_stonemod_item_sprite_like");
        editor.AddFunction(LoaderGml.Read("Items/scr_stonemod_item_sprite.gml"), "scr_stonemod_item_sprite");
        editor.AddFunction(LoaderGml.Read("Items/scr_stonemod_consum_define.gml"), "scr_stonemod_consum_define");
    }
}
