namespace StoneForge;

/// <summary>A character's sprites built from a <see cref="CharacterLook"/>: the five the game draws the player with
/// (global.playerSpriteArray), each its animation frames. They're the caller's: <see cref="Dispose"/> deletes them.</summary>
public sealed class CharacterSprites : IDisposable
{
    internal const int Count = 5;
    private readonly int[] _sprites;

    internal CharacterSprites(int[] sprites) => _sprites = sprites;

    /// <summary>All five, as the game's playerSpriteArray holds them.</summary>
    public IReadOnlyList<int> All => _sprites;

    /// <summary>Drawn most of the time.</summary>
    public int Normal => _sprites[0];
    /// <summary>Drawn as it blinks (scr_unitBlinkUpdate).</summary>
    public int Blinking => _sprites[1];
    /// <summary>Drawn while its hit flash (diss) is below -5, and above 5.</summary>
    public int FlashNegative => _sprites[2];
    public int FlashPositive => _sprites[3];
    /// <summary>Its mask: the compositor's last row.</summary>
    public int Mask => _sprites[4];

    /// <summary>The sprite to draw now, as o_player picks its own (its Draw Begin): by its hit flash, then
    /// blinking.</summary>
    public int For(double flash, bool blinking)
        => Math.Abs(flash) > 5 ? flash < 0 ? FlashNegative : FlashPositive : blinking ? Blinking : Normal;

    /// <summary>Whether they've been deleted.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Deletes the sprites (some rows may be the same sprite: each is deleted once).</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        if (!Game.Running)
            return;
        foreach (int sprite in _sprites.Distinct())
            if (sprite >= 0 && Game.CallBuiltin("sprite_exists", sprite).AsBool)
                Game.CallBuiltin("sprite_delete", sprite);
    }
}
