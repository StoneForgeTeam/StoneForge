namespace StoneForge;

/// <summary>Which of a world-map cell's two stores a value is read from (<see cref="WorldTile.Get"/>).</summary>
public enum TileLayer
{
    /// <summary>The saved value, else the generated one - as the game reads a cell.</summary>
    Any,
    /// <summary>What the save keeps for the cell (the game's globalmapLocationsDataMap): its seeds, its dungeon, what's
    /// happened there.</summary>
    Saved,
    /// <summary>What the world map generated for it from the world seed (the game's terrainGrid), made again each game and
    /// never saved.</summary>
    Generated,
}
