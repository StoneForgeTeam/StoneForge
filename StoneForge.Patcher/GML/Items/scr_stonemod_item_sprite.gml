// One of a mod's item's pictures (StoneForge's Items): argument2, a sprite, put in its asset map (argument0) as
// argument1 ("char_sprite", "corpse_sprite"...), lined up as the game item's picture it replaces.
function scr_stonemod_item_sprite(argument0, argument1, argument2)
{
    scr_stonemod_item_sprite_like(argument2, ds_map_find_value(argument0, argument1))
    ds_map_set(argument0, argument1, argument2)
}
