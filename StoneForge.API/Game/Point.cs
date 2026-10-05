namespace StoneForge;

/// <summary>A position, or an offset, in pixels - in the room, or on the screen, as the API it comes from says (a
/// sprite's origin, a unit's place...). Deconstructs as <c>var (x, y) = point</c>.</summary>
public readonly record struct Point(double X, double Y)
{
    /// <summary>How far apart two points are, in pixels.</summary>
    public double DistanceTo(Point other) => Math.Sqrt((other.X - X) * (other.X - X) + (other.Y - Y) * (other.Y - Y));

    public static Point operator +(Point a, Point b) => new(a.X + b.X, a.Y + b.Y);
    public static Point operator -(Point a, Point b) => new(a.X - b.X, a.Y - b.Y);
    public static Point operator *(Point a, double scale) => new(a.X * scale, a.Y * scale);

    public override string ToString() => $"({X}, {Y})";
}
