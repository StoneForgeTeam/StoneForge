// User event 14: its skills placed - three to a row, as the game's own pages place theirs (columns 31, 81, 131;
// rows 55, 128, 201...), on the background StoneForge drew with their slots.
event_inherited()
var _count = array_length(skill)
for (var _i = 0; _i < _count; _i++)
{
    if (!is_string(skill[_i]))
        new ctr_SkillPoint(connectionsRender, skill[_i], (31 + 50 * (_i mod 3)), (55 + 73 * (_i div 3)))
}
