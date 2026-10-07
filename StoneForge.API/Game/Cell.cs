namespace StoneForge;

/// <summary>A cell of the room's grid - the 26-pixel squares units stand on and move between (<see cref="Units"/>,
/// <see cref="Mouse.Cell"/>). Deconstructs as <c>var (x, y) = cell</c>. (The world map's cells are
/// <see cref="WorldTile"/>s.)</summary>
public readonly record struct Cell(int X, int Y)
{
    /// <summary>A cell's size, in pixels.</summary>
    public const int Size = 26;

    /// <summary>The cell a room position is in (as the game's <c>x div 26</c>, rounding down).</summary>
    public static Cell At(double x, double y) => new(Of(x), Of(y));

    /// <summary>The cell a room position is in.</summary>
    public static Cell At(Point position) => At(position.X, position.Y);

    /// <summary>Its middle, in room coordinates - where a unit standing on it is (its xx / yy).</summary>
    public Point Center => new(X * Size + Size / 2.0, Y * Size + Size / 2.0);

    /// <summary>Its top-left corner, in room coordinates.</summary>
    public Point Corner => new(X * Size, Y * Size);

    /// <summary>How many steps apart two cells are, diagonals counting as one (the game's tile distance).</summary>
    public int DistanceTo(Cell other) => Math.Max(Math.Abs(other.X - X), Math.Abs(other.Y - Y));

    /// <summary>Whether another cell is next to this one (one of its 8 neighbours).</summary>
    public bool IsNextTo(Cell other) => DistanceTo(other) == 1;

    /// <summary>The cell <paramref name="dx"/> across and <paramref name="dy"/> down from this one.</summary>
    public Cell Offset(int dx, int dy) => new(X + dx, Y + dy);

    /// <summary>The 8 cells around it.</summary>
    public IEnumerable<Cell> Neighbours
    {
        get
        {
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    if (dx != 0 || dy != 0)
                        yield return Offset(dx, dy);
        }
    }

    public static Cell operator +(Cell a, Cell b) => new(a.X + b.X, a.Y + b.Y);
    public static Cell operator -(Cell a, Cell b) => new(a.X - b.X, a.Y - b.Y);

    public override string ToString() => $"({X}, {Y})";

    // (GameMaker's div for positions, rounding down.)
    private static int Of(double position) => (int)Math.Floor(position / Size);
}
