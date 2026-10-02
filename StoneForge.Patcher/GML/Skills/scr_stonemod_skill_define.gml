// A mod's skill (StoneForge's ModSkill): a game skill's row of the skills table (argument1: its object's id,
// "active_defence" - the row's name matched without case) copied under the mod's key (argument0), with some
// columns changed (argument2: "column=value" pairs joined by "|"), and its checks (global.skill_extra_validate_map)
// as the game skill's. Its objects, o_skill_<key> and its icon, the patcher added. Returns false if there's no
// such row.
function scr_stonemod_skill_define(argument0, argument1, argument2)
{
    var _csv = global.skills_stat_csv
    var _height = array_length(_csv)
    var _header = -1
    var _base = -1
    var _existing = -1
    for (var _i = 0; _i < _height; _i++)
    {
        var _row = _csv[_i]
        if (array_length(_row) < 2)
            continue
        if (_row[1] == "Object")
            _header = _i
        else if (_row[0] == argument0)
            _existing = _i
        else if (_base < 0 && string_lower(_row[0]) == string_lower(argument1))
            _base = _i
    }
    if (_header < 0 || _base < 0)
        return false;
    var _baseName = _csv[_base][0]
    var _width = array_length(_csv[_header])
    var _new = array_create(_width, "")
    for (var _j = 0; _j < _width && _j < array_length(_csv[_base]); _j++)
        _new[_j] = _csv[_base][_j]
    _new[0] = argument0
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
            for (var _k = 1; _k < _width; _k++)
            {
                if (_csv[_header][_k] == _column)
                {
                    _new[_k] = _value
                    break
                }
            }
        }
    }
    if (_existing >= 0)
        global.skills_stat_csv[_existing] = _new
    else
        array_push(global.skills_stat_csv, _new)
    if ds_map_exists(global.skill_extra_validate_map, _baseName)
        ds_map_set(global.skill_extra_validate_map, argument0, ds_map_find_value(global.skill_extra_validate_map, _baseName))
    return true;
}
