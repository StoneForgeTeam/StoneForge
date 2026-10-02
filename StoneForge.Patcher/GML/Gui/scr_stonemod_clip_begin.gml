// A mod UI element's clipped children (StoneForge's UIElement.ClipChildren) drawn into a surface: argument0,
// cleared, with the Draw GUI pass's scale (argument1) and the clipped area's top-left corner (argument2,
// argument3 - GUI pixels) at its corner. The pass's matrices are kept (nested clips stack) for
// scr_stonemod_clip_end.
function scr_stonemod_clip_begin(argument0, argument1, argument2, argument3)
{
    if (!variable_global_exists("stonemod_clip_stack"))
        global.stonemod_clip_stack = []
    array_push(global.stonemod_clip_stack, [matrix_get(matrix_view), matrix_get(matrix_projection), matrix_get(matrix_world)])
    surface_set_target(argument0)
    draw_clear_alpha(c_black, 0)
    matrix_set(matrix_world, matrix_build((-argument2), (-argument3), 0, 0, 0, 0, argument1, argument1, 1))
}
