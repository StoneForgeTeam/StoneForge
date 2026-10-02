// The clipped children done (scr_stonemod_clip_begin): back to drawing where it was, with its matrices, and
// the surface (argument0) drawn at argument2, argument3 (the pass's units) - shrunk back by the pass's scale
// (argument1), which it's drawn under.
function scr_stonemod_clip_end(argument0, argument1, argument2, argument3)
{
    surface_reset_target()
    var _matrices = array_pop(global.stonemod_clip_stack)
    matrix_set(matrix_view, _matrices[0])
    matrix_set(matrix_projection, _matrices[1])
    matrix_set(matrix_world, _matrices[2])
    draw_surface_ext(argument0, argument2, argument3, (1 / argument1), (1 / argument1), 0, c_white, 1)
}
