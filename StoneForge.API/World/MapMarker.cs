namespace StoneForge;

/// <summary>A marker a player put on the world map (<see cref="MapMarkers"/>): its sprite's name, which of the
/// sprite's images, and where it is in world-map pixels (<see cref="MapMarkers.CellSize"/> to a cell).</summary>
public readonly record struct MapMarker(string Sprite, int Image, Point Position)
{
    /// <summary>The world-map cell it's on.</summary>
    public WorldTile Tile => new((int)Math.Floor(Position.X / MapMarkers.CellSize), (int)Math.Floor(Position.Y / MapMarkers.CellSize));
}
