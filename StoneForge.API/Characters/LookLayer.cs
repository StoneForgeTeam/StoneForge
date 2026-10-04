namespace StoneForge;

/// <summary>One layer of a <see cref="CharacterLook"/>: the compositor's values for it (scr_playerSpriteInit's - the
/// sprite, its frame, offset, clipping, clip sprite, mask sprite and mask colour) and the origins its sprite and mask had
/// where the look was read.</summary>
public sealed record LookLayer(IReadOnlyList<GmValue> Values, (double X, double Y) SpriteOrigin, (double X, double Y) MaskOrigin)
{
    internal const int ValueCount = 13;

    /// <summary>Its sprite (-4: none - nothing drawn for it).</summary>
    public GmValue Sprite => Values[0];
    /// <summary>Its frame.</summary>
    public GmValue Frame => Values[1];
    /// <summary>Its mask sprite (-4: its own sprite, in its mask colour).</summary>
    public GmValue Mask => Values[11];
}
