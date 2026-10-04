namespace StoneForge;

/// <summary>The room's units on the game's grid - enemies, NPCs, animals, the player - as the game keeps them: the cell
/// each stands on, who stands on a cell (its position grid: targeting, the cursor, pathing), moving one to another cell
/// as its own movement does (the collision grid, the position grid, a big unit's extra cells), taking one out quietly,
/// making one, and the player's list of units to run each turn.
/// <code>
/// var (x, y) = Units.CellOf(enemy);
/// if (Units.CanTake(enemy, x + 1, y))
///     Units.Move(enemy, x + 1, y);
/// </code>
/// For units the game doesn't move itself - a mod's stand-in for someone, a unit kept in step with another game's.
/// Game thread only, in a game.</summary>
public static class Units
{
    /// <summary>A cell's size, in pixels.</summary>
    public const int CellSize = 26;

    /// <summary>The cell a unit stands on (its xx / yy: where it is on the grid, which its drawn x / y follow).</summary>
    public static (int X, int Y) CellOf(Instance unit) => (CellOf(unit.Get("xx").AsReal), CellOf(unit.Get("yy").AsReal));

    /// <summary>The cell a room position is in (x div 26).</summary>
    public static int CellOf(double position) => (int)Math.Floor(position / CellSize);

    /// <summary>A cell's middle, in room coordinates - where a unit standing on it is (its xx / yy).</summary>
    public static double PositionOf(int cell) => cell * CellSize + CellSize / 2;

    /// <summary>The game's controller and its grids - the collision grid (newgrid) and the position grid (posgrid) - for
    /// moving units, found once for many.</summary>
    public readonly record struct Grids(Instance Controller, GmValue Collisions, GmValue Positions);

    /// <summary>The room's grids (null with no controller: not in a game).</summary>
    public static Grids? Current()
        => Controller() is { IsNone: false } controller ? new Grids(controller, controller.Get("newgrid"), controller.Get("posgrid")) : null;

    /// <summary>Who stands on a cell, as the game's position grid has it (none: no one, off the room, or no game).</summary>
    public static Instance At(int cellX, int cellY)
    {
        if (_controller == -2)
            _controller = Gm.AssetGetIndex("o_controller");
        if (_controller < 0 || cellX < 0 || cellY < 0)
            return default;
        GmValue controller = Game.CallBuiltin("instance_find", _controller, 0);
        if (!Game.CallBuiltin("instance_exists", controller).AsBool)
            return default;
        GmValue grid = Game.CallBuiltin("variable_instance_get", controller, "posgrid");
        if (grid.Kind != GmKind.Real || !Game.CallBuiltin("ds_exists", grid, DsGrid).AsBool
            || cellX >= Game.CallBuiltin("ds_grid_width", grid).AsInt || cellY >= Game.CallBuiltin("ds_grid_height", grid).AsInt)
            return default;
        Instance standing = Instance.Of(Game.CallBuiltin("ds_grid_get", grid, cellX, cellY));
        return !standing.IsNone && Game.CallBuiltin("instance_exists", standing.Id).AsBool ? standing : default;
    }

    private const int DsGrid = 3;
    private static int _controller = -2;

    // (Tests: the game's objects looked up again.)
    internal static void ResetForTests() => _controller = _unitObject = -2;

    private static Instance At(Grids grids, int x, int y)
        => Instance.Of(Game.CallScript("ds_grid_get_ext", grids.Controller, grids.Positions, x, y, -4));

    /// <summary>Whether a unit may take a cell in the position grid: it's free, or it's the unit's own. Another unit there
    /// (the player, an area unit, for a moment) would be overwritten in it, and the game would then read the wrong unit
    /// there.</summary>
    public static bool CanTake(Instance unit, int cellX, int cellY)
    {
        if (Current() is not { } grids)
            return false;
        Instance occupant = At(grids, cellX, cellY);
        return occupant.IsNone || !occupant.Exists || occupant.Equals(unit.Persist())
            || !Gm.ObjectIsAncestor(occupant.Get("object_index").AsInt, UnitObject);
    }

    // (o_unit: what every unit is a child of.)
    private static int _unitObject = -2;
    private static int UnitObject => _unitObject == -2 ? _unitObject = Gm.AssetGetIndex("o_unit") : _unitObject;

    /// <summary>Moves a unit to a cell as its own movement does: out of the old cell and into the new one in the collision
    /// grid and the position grid (and a big unit's extra cells). Its drawn x / y follow only for a jump of more than two
    /// cells - for a short move, it walks there by its own step - or always with <paramref name="snap"/> (a unit drawn
    /// from elsewhere, which never walks). Check <see cref="CanTake"/> first: this takes the cell whoever's on it.</summary>
    public static void Move(Instance unit, int cellX, int cellY, bool snap = false)
    {
        if (Current() is { } grids)
            Move(unit, cellX, cellY, grids, snap: snap);
    }

    /// <summary>As <see cref="Move(Instance, int, int, bool)"/>, with the room's grids already found
    /// (<see cref="Current"/>) and whether it's a big unit (is_poly_cell: it never changes) if that's known - for moving
    /// many units.</summary>
    public static void Move(Instance unit, int cellX, int cellY, Grids grids, bool? poly = null, bool snap = false)
    {
        if (!unit.Exists)
            return;
        // (By its id: what the position grid holds, and what the game's scripts are handed.)
        unit = unit.Persist();
        double x = PositionOf(cellX), y = PositionOf(cellY);
        double oldXx = unit.Get("xx").AsReal, oldYy = unit.Get("yy").AsReal;
        if (oldXx == x && oldYy == y)
            return;
        int oldX = CellOf(oldXx), oldY = CellOf(oldYy);
        var (controller, collisions, positions) = grids;
        bool isPoly = poly ?? unit.Get("is_poly_cell").AsBool;
        Game.CallScript("scr_collision_clear", controller, collisions, oldX, oldY, true);
        if (isPoly)
        {
            Game.CallScript("scr_enemy_poly_cell_clear", unit, oldX, oldY);
            Game.CallScript("scr_enemy_poly_cell_posgrid_clear", unit, oldX, oldY);
            Game.CallScript("scr_enemy_poly_cell_posgrid_fill", unit, cellX, cellY);
        }
        else
        {
            if (At(grids, oldX, oldY).Equals(unit))
                Game.CallScript("ds_grid_set_ext", controller, positions, oldX, oldY, -4);
            Game.CallScript("ds_grid_set_ext", controller, positions, cellX, cellY, unit);
        }
        unit["xx"] = x;
        unit["yy"] = y;
        if (snap || Math.Abs(cellX - oldX) > 2 || Math.Abs(cellY - oldY) > 2)
        {
            unit["x"] = x;
            unit["y"] = y;
            unit["draw_x"] = x;
            unit["draw_y"] = y;
            unit["diff_x"] = 0;
            unit["diff_y"] = 0;
        }
        if (isPoly)
            Game.CallScript("scr_enemy_poly_cell_fill", unit, cellX, cellY);
        Game.CallScript("scr_collision_add_enemy", controller, collisions, cellX, cellY);
    }

    /// <summary>The free cell nearest one, for <paramref name="unit"/> to stand on, as the game finds one (its collision
    /// grid: scr_mpgridFindNearestFreeCell); null if there's none, or no game.</summary>
    public static (int X, int Y)? NearestFreeCell(Instance unit, int cellX, int cellY)
    {
        if (Current() is not { } grids)
            return null;
        using GmArray? free = Game.CallScript("scr_mpgridFindNearestFreeCell", unit, grids.Collisions, cellX, cellY).AsArray;
        return free is { Length: >= 2 } && free[0].AsReal >= 0 && free[1].AsReal >= 0 ? (free[0].AsInt, free[1].AsInt) : null;
    }

    /// <summary>Takes a unit out of the world quietly: out of the grids and the player's list of units to run each turn,
    /// the effects on it with it (<see cref="UnitEffects.RemoveAll"/>), then destroyed without its Destroy event - no
    /// loot, corpse or kill credit.</summary>
    public static void Remove(Instance unit)
    {
        if (!unit.Exists)
            return;
        unit = unit.Persist();
        var (x, y) = CellOf(unit);
        if (Current() is { } grids)
        {
            Game.CallScript("scr_collision_clear", grids.Controller, grids.Collisions, x, y, true);
            if (At(grids, x, y).Equals(unit))
                Game.CallScript("ds_grid_set_ext", grids.Controller, grids.Positions, x, y, -4);
        }
        Game.CallScript("scr_enemy_poly_cell_clear", unit, x, y);
        Game.CallScript("scr_enemy_poly_cell_posgrid_clear", unit, x, y);
        // (Even mid-turn: a destroyed unit can't stay in the list the turn walks.)
        RemoveFromTurns(listed => listed.Equals(unit), betweenTurnsOnly: false);
        UnitEffects.RemoveAll(unit);
        unit.Destroy(runDestroyEvent: false);
    }

    /// <summary>Makes a unit of <paramref name="obj"/> (an enemy, an animal...) on a cell, as the game spawns one
    /// (scr_enemy_create); none if it wasn't made.</summary>
    public static Instance Create(int obj, int cellX, int cellY)
        => Instance.Of(Game.CallScript("scr_enemy_create", default, PositionOf(cellX), PositionOf(cellY), obj, false, false));

    /// <inheritdoc cref="Create(int, int, int)"/>
    public static Instance Create(GameObjectId obj, int cellX, int cellY) => Create((int)obj, cellX, cellY);

    /// <summary>Gives a unit one of the game's mob records (scr_param: its type, stats, resistances, icons - "Caravan
    /// Dummy", "Bandit Thug"...), as the game sets a mob up.</summary>
    public static void SetRecord(Instance unit, string record) => Game.CallScript("scr_param", unit, record);

    /// <summary>Whether a unit is the player, as the game tells (is_player: the player's character, whichever object
    /// it is).</summary>
    public static bool IsPlayer(GmValue unit) => !unit.IsUndefined && Game.CallScript("is_player", default, unit).AsBool;

    /// <summary>Ends a unit's turn, as its own actions end it (scr_unitTurnNext), after <paramref name="delay"/> frames
    /// (the game's TurnDelay; null: it, or 3 for a unit out of sight, as the game does).</summary>
    public static void EndTurn(Instance unit, double? delay = null)
        => Game.CallScript("scr_unitTurnNext", unit, delay ?? (unit.Get("visible").AsBool ? Game.Global["TurnDelay"].AsReal : 3));

    /// <summary>How many units the player's list of units to run each turn holds (-1 with no player).</summary>
    public static int TurnsCount()
        => Player() is { IsNone: false } player && player.Get("enemylist").AsDsList is { } units ? units.Count : -1;

    /// <summary>Takes units out of the player's list of units to run each turn (o_player's enemylist): their AI isn't run
    /// - a unit another game runs. With <paramref name="betweenTurnsOnly"/>, only between turns (its enemy_iteration 0),
    /// never while the turn walks the list.</summary>
    public static void RemoveFromTurns(Func<Instance, bool> remove, bool betweenTurnsOnly = true)
    {
        Instance player = Player();
        if (player.IsNone || (betweenTurnsOnly && player.Get("enemy_iteration").AsReal != 0) || player.Get("enemylist").AsDsList is not { } units)
            return;
        for (int i = units.Count - 1; i >= 0; i--)
            if (Instance.Of(units[i]) is { IsNone: false } listed && remove(listed))
                units.RemoveAt(i);
    }

    private static Instance Controller() => Instances.All(GameObjectId.o_controller).FirstOrDefault();
    private static Instance Player() => Instances.All(GameObjectId.o_player).FirstOrDefault();
}
