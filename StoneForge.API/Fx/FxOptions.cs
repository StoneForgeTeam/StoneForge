namespace StoneForge;

/// <summary>How an animation plays (<see cref="Fx.Play(GameInstance, int, FxOptions?)"/>).</summary>
public sealed class FxOptions
{
    /// <summary>Frames per game frame (the game's effects mostly play at 0.5).</summary>
    public double Speed { get; init; } = 0.5;
    /// <summary>Plays over and over until stopped (an aura) instead of once.</summary>
    public bool Loop { get; init; }
    /// <summary>Shifts it from where the unit is drawn (its feet, for the game's unit sprites): -16 is about
    /// chest height.</summary>
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    /// <summary>Its tint (<see cref="Draw.Rgb"/>; white: as drawn) and opacity.</summary>
    public int Colour { get; init; } = Draw.White;
    public double Alpha { get; init; } = 1;
    /// <summary>Drawn behind its unit instead of over it - a glow on the ground at its feet, as the game's banner
    /// buff draws.</summary>
    public bool Under { get; init; }
    /// <summary>The colour of the light it casts (the game's effects glow; default its warm orange).</summary>
    public int? Light { get; init; }
}
