namespace StoneForge;

/// <summary>The game's fonts, for <see cref="Draw.Text(double, double, string, int?, int, int, GameFont, double)"/>.</summary>
public enum GameFont
{
    /// <summary>Its text font (f_dmg): names, descriptions, the log.</summary>
    Default,
    /// <summary>Its digit and title font (f_digits): numbers on bars, buttons' text.</summary>
    Digits,
}
