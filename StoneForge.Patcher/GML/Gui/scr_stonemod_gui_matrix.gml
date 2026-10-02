// The mods' Draw GUI pass at the game's UI scale (StoneForge's Draw.Scale): everything drawn is scaled by
// argument0 - 1 puts it back.
function scr_stonemod_gui_matrix(argument0)
{
    if (argument0 == 1)
        matrix_set(matrix_world, matrix_build_identity())
    else
        matrix_set(matrix_world, matrix_build(0, 0, 0, 0, 0, 0, argument0, argument0, 1))
}
