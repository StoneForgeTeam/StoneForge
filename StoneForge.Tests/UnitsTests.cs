using StoneForge;

// The game's units on its grid (Units): cells and their positions, who stands on a cell (the position grid, laid out
// with FakeGame's input), and a unit taken out quietly (laid out with FakeGame's room and scripts).
public class UnitsTests : FakeGame
{
    public UnitsTests() => Units.ResetForTests();

    [Fact]
    public void A_cell_its_position_and_its_neighbours()
    {
        Assert.Equal(new Cell(0, 1), Cell.At(25.9, 26));
        Assert.Equal(new Cell(-1, 0), Cell.At(-1, 0));
        Assert.Equal(new Point(65, 39), new Cell(2, 1).Center);
        Assert.Equal(new Point(52, 26), new Cell(2, 1).Corner);
        Assert.Equal(new Cell(2, 1), Cell.At(new Cell(2, 1).Center));
        Assert.Equal(2, new Cell(2, 1).DistanceTo(new Cell(0, 2)));
        Assert.True(new Cell(2, 1).IsNextTo(new Cell(3, 2)));
        Assert.False(new Cell(2, 1).IsNextTo(new Cell(2, 1)));
        Assert.Equal(8, new Cell(2, 1).Neighbours.Distinct().Count());
        Assert.DoesNotContain(new Cell(2, 1), new Cell(2, 1).Neighbours);
        Assert.Equal(new Cell(3, 3), new Cell(2, 1) + new Cell(1, 2));
        var (x, y) = new Cell(4, 5);
        Assert.Equal((4, 5), (x, y));
    }

    [Fact]
    public void Who_stands_on_a_cell_comes_from_the_position_grid()
    {
        Input = new FakeInput { Positions = new int[3, 3] };
        for (int x = 0; x < 3; x++)
            for (int y = 0; y < 3; y++)
                Input.Positions[x, y] = -4;
        Input.Positions[1, 2] = 4242;
        Input.Units.Add(4242);
        Assert.Equal(Instance.FromId(4242), Units.At(new Cell(1, 2)));
        Assert.True(Units.At(new Cell(0, 0)).IsNone);
        Assert.True(Units.At(new Cell(3, 0)).IsNone);
        Assert.True(Units.At(new Cell(-1, 0)).IsNone);
        // (One that's gone: no one.)
        Input.Units.Clear();
        Assert.True(Units.At(new Cell(1, 2)).IsNone);
    }

    [Fact]
    public void A_removed_unit_leaves_its_faction_and_is_no_one_s_target_any_more()
    {
        const int Removed = 100_001, Other = 100_002, Third = 100_003;
        var scripts = new FakeScripts();
        scripts.Add("scr_enemy_poly_cell_clear", _ => GmValue.Undefined);
        scripts.Add("scr_enemy_poly_cell_posgrid_clear", _ => GmValue.Undefined);
        var leftFaction = new List<double>();
        scripts.Add("scr_faction_map_remove", args => { leftFaction.Add(args[0].AsReal); return GmValue.Undefined; });
        GameScripts = scripts;
        var world = new FakeWorld();
        World = world;
        foreach (int id in new[] { Removed, Other, Third })
            world.Add(id, (int)GameObjectId.o_unit);
        world.Vars[Removed] = new() { ["x"] = 13, ["y"] = 13 };
        world.Vars[Other] = new() { ["target"] = Removed, ["last_attacker"] = Third, ["skill_target"] = Removed };

        Units.Remove(Instance.FromId(Removed));

        Assert.Equal(new[] { Removed }, world.Destroyed);
        Assert.Equal(new double[] { Removed }, leftFaction);
        Assert.Equal(-4, world.Vars[Other]["target"].AsReal);
        Assert.Equal(-4, world.Vars[Other]["skill_target"].AsReal);
        // (One naming a unit still here keeps it.)
        Assert.Equal(Third, world.Vars[Other]["last_attacker"].AsReal);
    }
}
