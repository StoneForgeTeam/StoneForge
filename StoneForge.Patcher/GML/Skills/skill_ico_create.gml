// A mod's skill's icon (o_skill_{key}_ico, on its tab of the skills menu): an o_skill_ico for the mod's skill,
// learnt with ability points alone (nothing else to unlock it).
event_inherited()
child_skill = o_skill_{key}
tier_to_open = -4
attributes_names_to_open = []
attributes_value_to_open = 0
level_to_open = 0
ds_list_clear(attribute)
event_perform_object(child_skill, ev_create, 0)
