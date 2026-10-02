// A mod's item picture (argument0, a sprite) lined up as one of the game's (argument1): the same origin, and
// for a worn picture the same anchor on the body (global.customizationAnchors - scr_itemCharSpritesInit sets
// a worn picture's origin from it each time it's put on).
function scr_stonemod_item_sprite_like(argument0, argument1)
{
    if (is_undefined(argument1) || argument1 == -4 || (!sprite_exists(argument1)))
        return;
    sprite_set_offset(argument0, sprite_get_xoffset(argument1), sprite_get_yoffset(argument1))
    var _anchor = ds_map_find_value(global.customizationAnchors, argument1)
    if (!is_undefined(_anchor))
        ds_map_set(global.customizationAnchors, argument0, _anchor)
}
