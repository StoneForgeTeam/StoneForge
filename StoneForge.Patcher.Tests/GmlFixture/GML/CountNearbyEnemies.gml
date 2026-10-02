/// @stoneforge return int
/// @stoneforge param originX double
/// @stoneforge param originY double
/// @stoneforge param radius double
function CountNearbyEnemies(originX, originY, radius)
{
    var found = 0;
    var total = instance_number(o_enemy);
    for (var i = 0; i < total; i++)
    {
        var enemy = instance_find(o_enemy, i);
        if (instance_exists(enemy) && point_distance(originX, originY, enemy.x, enemy.y) <= radius)
            found++;
    }
    return found;
}
