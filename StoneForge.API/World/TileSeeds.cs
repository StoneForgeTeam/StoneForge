namespace StoneForge;

/// <summary>The seeds a world-map cell's areas are built from (scr_globaltile_seed_get): the same seed, the same area.
/// Each is -1 while it hasn't been set (the cell's not been visited), and -2 when it's to be rolled again (its area
/// respawns).</summary>
public readonly struct TileSeeds
{
    /// <summary>The game's names for them.</summary>
    public static readonly IReadOnlyList<string> Names = new[] { "seed", "growSeed", "mobsSeed", "presetSeed", "containersSeed", "Trade_Seed" };

    private readonly WorldTile _tile;

    internal TileSeeds(WorldTile tile) => _tile = tile;

    /// <summary>The layout ("seed").</summary>
    public double Layout => this["seed"];
    /// <summary>What grows ("growSeed").</summary>
    public double Growth => this["growSeed"];
    /// <summary>Which mobs ("mobsSeed").</summary>
    public double Mobs => this["mobsSeed"];
    /// <summary>Which room presets ("presetSeed").</summary>
    public double Preset => this["presetSeed"];
    /// <summary>What's in its containers ("containersSeed").</summary>
    public double Containers => this["containersSeed"];
    /// <summary>What its traders have ("Trade_Seed").</summary>
    public double Trade => this["Trade_Seed"];

    /// <summary>A seed by the game's name (<see cref="Names"/>), saved; -1 if it hasn't been set.</summary>
    public double this[string name] => _tile.Get(name, TileLayer.Saved) is { Kind: GmKind.Real } seed ? seed.AsReal : -1;
}
