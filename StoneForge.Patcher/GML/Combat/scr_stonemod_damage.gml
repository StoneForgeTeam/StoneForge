// Damage dealt as the game deals it outside a weapon's swing (its traps, spells' splashes, backfires): an
// o_damage_dealer at the target, its damage by type, through scr_damage_with_calc - so armour, protection,
// resistances, the damage number over the target and the combat log are the game's own.
// argument0: the target. argument1: who did it (noone: nobody) - the dealer's owner, so the damage is theirs (who
// hit whom, crimes, kills). argument2: the damage, "Shock_Damage=12|Fire_Damage=3". argument3: armour piercing
// (0: none). argument4: logged. argument5: its name in the log ("" : the owner's). argument6: names for kinds of
// damage in the log's breakdown, "Shock_Damage=overcharge" (a mod's own kind, dealt as the game's) - the game's
// global.actionsLogDamages entries swapped for this hit only.
// Returns the damage done (after armour and resistances).
function scr_stonemod_damage(argument0, argument1, argument2, argument3, argument4, argument5, argument6)
{
    if (!instance_exists(argument0))
        return 0;
    // The log's names, swapped in (and their own kept to put back).
    var _labels = ds_map_create()
    var _logNames = variable_global_exists("actionsLogDamages") ? global.actionsLogDamages : -1
    var _rest = argument6
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
        if (_equals > 0 && _logNames != -1)
        {
            var _key = string_copy(_pair, 1, (_equals - 1))
            ds_map_set(_labels, _key, ds_map_find_value(_logNames, _key))
            ds_map_set(_logNames, _key, string_delete(_pair, 1, _equals))
        }
    }
    var _done = 0
    with (instance_create_depth(argument0.x, argument0.y, argument0.depth, o_damage_dealer))
    {
        target = argument0
        if instance_exists(argument1)
        {
            owner = argument1
            name = scr_actionsLogGetName(argument1)
        }
        if (argument5 != "")
            name = argument5
        default_log = argument4
        Armor_Piercing = argument3
        _rest = argument2
        while (string_length(_rest) > 0)
        {
            _end = string_pos("|", _rest)
            _pair = _rest
            if (_end > 0)
            {
                _pair = string_copy(_rest, 1, (_end - 1))
                _rest = string_delete(_rest, 1, _end)
            }
            else
                _rest = ""
            _equals = string_pos("=", _pair)
            if (_equals > 0)
                variable_instance_set(id, string_copy(_pair, 1, (_equals - 1)), real(string_delete(_pair, 1, _equals)))
        }
        event_user(0)
        _done = damage
    }
    // (The game's names back.)
    var _swapped = ds_map_find_first(_labels)
    while (!is_undefined(_swapped))
    {
        var _was = ds_map_find_value(_labels, _swapped)
        if is_undefined(_was)
            ds_map_delete(_logNames, _swapped)
        else
            ds_map_set(_logNames, _swapped, _was)
        _swapped = ds_map_find_next(_labels, _swapped)
    }
    ds_map_destroy(_labels)
    return _done;
}
