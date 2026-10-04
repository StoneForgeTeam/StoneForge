using StoneForge;

// The game's units on its grid (Units): cells and their positions, and who stands on a cell (the position grid, laid
// out with FakeGame's input).
public class UnitsTests : FakeGame
{
    public UnitsTests() => Units.ResetForTests();

    [Fact]
    public void Cells_and_their_positions()
    {
        Assert.Equal(0, Units.CellOf(25.9));
        Assert.Equal(1, Units.CellOf(26));
        Assert.Equal(-1, Units.CellOf(-1));
        Assert.Equal(65, Units.PositionOf(2));
        Assert.Equal(2, Units.CellOf(Units.PositionOf(2)));
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
        Assert.Equal(Instance.FromId(4242), Units.At(1, 2));
        Assert.True(Units.At(0, 0).IsNone);
        Assert.True(Units.At(3, 0).IsNone);
        Assert.True(Units.At(-1, 0).IsNone);
        // (One that's gone: no one.)
        Input.Units.Clear();
        Assert.True(Units.At(1, 2).IsNone);
    }
}
