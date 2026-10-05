using StoneForge;

// The game's units on its grid (Units): cells and their positions, and who stands on a cell (the position grid, laid
// out with FakeGame's input).
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
}
