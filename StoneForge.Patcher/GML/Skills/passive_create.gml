// A mod's passive (o_pass_skill_{key}, on its tab of the skills menu): one of the game's passives, its name and
// description the mod's ({key} in the game's text maps), learnt with ability points alone (nothing else to unlock
// it). Its stats (in its data map) and its reactions are StoneForge's.
event_inherited()
scr_skill_atr("{key}")
tier_to_open = -4
attributes_names_to_open = []
attributes_value_to_open = 0
level_to_open = 0
