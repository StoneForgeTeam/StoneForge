// A mod's weapon or armour (StoneForge's Items): a game item's table row (argument2, its name) copied
// under a new name (argument1), with some columns changed (argument3: "column=value" pairs joined by "|"),
// and its sprites and sounds copied from the game item's. argument0: true for armour (table_armor), false for
// a weapon (table_weapons). argument4: also in the random loot tables. Returns the new item's asset map (its
// sprites, for the loader to change), or -4 if there's no such game item.
function scr_stonemod_item_define(argument0, argument1, argument2, argument3, argument4)
{
    var _height = array_length(argument0 ? global.armor_csv : global.weapons_csv)
    var _header = argument0 ? global.armor_csv[0] : global.weapons_csv[0]
    var _width = array_length(_header)
    var _base = -1
    for (var _i = 1; _i < _height; _i++)
    {
        var _name = argument0 ? global.armor_csv[_i][0] : global.weapons_csv[_i][0]
        if (_name == argument2)
        {
            _base = _i
            break
        }
    }
    if (_base < 0)
        return -4;
    var _row = array_create(_width, "")
    for (var _j = 0; _j < _width; _j++)
        _row[_j] = argument0 ? global.armor_csv[_base][_j] : global.weapons_csv[_base][_j]
    _row[0] = argument1
    var _rest = argument3
    while (string_length(_rest) > 0)
    {
        var _end = string_pos("|", _rest)
        var _pair = _rest
        if (_end > 0)
        {
            _pair = string_copy(_rest, 1, (_end - 1))
            _rest = string_delete(_rest, 1, _end)
        }
        else
            _rest = ""
        var _equals = string_pos("=", _pair)
        if (_equals > 0)
        {
            var _column = string_copy(_pair, 1, (_equals - 1))
            var _value = string_delete(_pair, 1, _equals)
            for (var _k = 0; _k < _width; _k++)
            {
                if (_header[_k] == _column)
                {
                    _row[_k] = _value
                    break
                }
            }
        }
    }
    // The stats: as the game reads them from its tables (numbers but for its text columns).
    var _table = array_create(2)
    _table[0] = _header
    _table[1] = _row
    if ds_map_exists(global.weapons_stat, argument1)
        ds_map_delete(global.weapons_stat, argument1)
    scr_array2d_to_map(_table, global.weapons_stat, global.weapon_string_attribute)
    if argument4
    {
        if argument0
            array_push(global.armor_csv, _row)
        else
            array_push(global.weapons_csv, _row)
    }
    // The sprites and sounds: the game item's.
    var _assets = ds_map_create()
    var _baseAssets = ds_map_find_value(global.weapons_asset_data, argument2)
    if (!is_undefined(_baseAssets))
        ds_map_copy(_assets, _baseAssets)
    if ds_map_exists(global.weapons_asset_data, argument1)
        ds_map_delete(global.weapons_asset_data, argument1)
    ds_map_add_map(global.weapons_asset_data, argument1, _assets)
    return _assets;
}
