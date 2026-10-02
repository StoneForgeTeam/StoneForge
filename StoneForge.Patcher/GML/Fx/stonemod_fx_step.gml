event_inherited()
if (!instance_exists(target))
    instance_destroy()
else
{
    x += stonemod_dx
    y += stonemod_dy
    // (Under: behind its unit, as the game's ground glows - o_a_crimsonbanner.)
    if stonemod_under
        depth = target.depth + 1
}