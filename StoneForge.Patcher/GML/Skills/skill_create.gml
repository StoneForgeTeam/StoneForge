// A mod's skill (StoneForge's ModSkill, o_skill_{key}): the game skill it's based on, made again under the mod's
// key - its row of the skills table (which StoneForge adds), its name and description.
event_inherited()
skill = "{key}"
ds_list_clear(mid_text)
scr_skill_atr(0)
