// A mod's consumable (StoneForge's Consumable): a game consumable's row of table_items_stats (argument1, its
// id) copied under the mod's key (argument0), with some columns changed (argument2: "column=value" pairs joined
// by "|") - as the game reads its own (global.consum_csv, and its stats in global.consum_stat_data). Its object,
// o_inv_<key> (a child of the game consumable's), the patcher added. Returns false if there's no such row.
function scr_stonemod_consum_define(argument0, argument1, argument2)
{
    var _csv = global.consum_csv
    var _height = array_length(_csv)
    var _header = -1
    var _base = -1
    var _existing = -1
    for (var _i = 0; _i < _height; _i++)
    {
        var _id = _csv[_i][0]
        if (_id == "id")
            _header = _i
        else if (_id == argument1)
            _base = _i
        else if (_id == argument0)
            _existing = _i
    }
    if (_header < 0 || _base < 0)
        return false;
    var _width = array_length(_csv[_header])
    var _row = array_create(_width, "")
    for (var _j = 0; _j < _width && _j < array_length(_csv[_base]); _j++)
        _row[_j] = _csv[_base][_j]
    _row[0] = argument0
    var _rest = argument2
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
                if (_csv[_header][_k] == _column)
                {
                    _row[_k] = _value
                    break
                }
            }
        }
    }
    // (Its row in the table - defined again, in place - and its stats, as the game reads them.)
    if (_existing >= 0)
        global.consum_csv[_existing] = _row
    else
        array_push(global.consum_csv, _row)
    var _table = array_create(2)
    _table[0] = _csv[_header]
    _table[1] = _row
    if ds_map_exists(global.consum_stat_data, argument0)
        ds_map_delete(global.consum_stat_data, argument0)
    scr_array2d_to_map(_table, global.consum_stat_data, global.consum_string_attribute)
    return true;
}
