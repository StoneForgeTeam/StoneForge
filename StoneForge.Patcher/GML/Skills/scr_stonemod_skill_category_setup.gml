// A mod's page of the skills menu (argument0, an o_skill_category_stonemod) filled: its name (argument1, a key of
// global.tier_name), its skills' icon objects (argument2, a ds_list StoneForge filled - empty: none) and its
// background (argument3, a sprite).
function scr_stonemod_skill_category_setup(argument0, argument1, argument2, argument3)
{
    var _list = argument2
    with (argument0)
    {
        text = argument1
        skill = []
        for (var _i = 0; _i < ds_list_size(_list); _i++)
            array_push(skill, ds_list_find_value(_list, _i))
        if (array_length(skill) == 0)
            skill = ["1"]
        if sprite_exists(argument3)
            branch_sprite = argument3
    }
}
