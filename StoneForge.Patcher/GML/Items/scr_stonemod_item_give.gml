// Gives the player an item (StoneForge's Items.Give), as the game's own console command does: a weapon
// or armour by its name (argument0), with a quality (argument1, -4: rolled) and durability in % (argument2,
// -4: as rolled); or a game consumable by its o_inv_ object's name. Returns whether it went into the
// inventory (a weapon that doesn't fit is dropped at the player's feet, as the game does).
function scr_stonemod_item_give(argument0, argument1, argument2)
{
    var _given = false
    with (o_inventory)
    {
        var _object = asset_get_index("o_inv_" + argument0)
        if (_object >= 0 && object_exists(_object))
            _given = scr_inventory_add_item(_object) != -4
        else
        {
            var _item = scr_inventory_add_weapon(argument0, argument1)
            if (_item != -4)
            {
                _given = true
                if (argument2 != -4)
                {
                    with (_item)
                        ds_map_replace(data, "Duration", argument2)
                }
            }
        }
    }
    return _given;
}
