namespace StoneForge;

/// <summary>Visual effects: animations on units - the game's own sprites (<c>Sprite.s_weapondamage_electricity</c>)
/// or a mod's (<see cref="ModContext.LoadSprite"/>, a strip of frames side by side) - following the unit, over
/// it, as the game's own effect animations are.</summary>
public static class Fx
{
    /// <summary>Plays an animation on a unit; null if there's no such unit (or the game data is missing the
    /// loader's effect object - run the patcher).</summary>
    public static Visual? Play(GameInstance on, int sprite, FxOptions? options = null)
    {
        Game.CheckRunning("Fx.Play");
        options ??= new FxOptions();
        int fx = Game.CallBuiltinTrusted("asset_get_index", default, default, "o_stonemod_fx").AsInt;
        if (fx < 0 || sprite < 0)
            return null;
        // (No unit: never on to the game - an id of -1 would mean "self" there.)
        if (on == null || on.Instance.IsNone)
            return null;
        GmValue target = on.Instance.Pointer == IntPtr.Zero ? on.Instance.Id : on.Instance.Get("id");
        if (!Game.CallBuiltinTrusted("instance_exists", default, default, target).AsBool)
            return null;
        Instance made = Game.CallBuiltinTrusted("instance_create_depth", default, default, 0, 0, 0, fx);
        if (made.IsNone)
            return null;
        made.Set("target", target);
        made.Set("sprite_index", sprite);
        made.Set("image_index", 0);
        made.Set("image_speed", options.Speed);
        made.Set("image_blend", options.Colour);
        made.Set("image_alpha", options.Alpha);
        made.Set("stonemod_loop", options.Loop);
        made.Set("stonemod_dx", options.OffsetX);
        made.Set("stonemod_dy", options.OffsetY);
        made.Set("stonemod_under", options.Under);
        if (options.Light is int light)
        {
            Instance glow = made.Get("light");
            if (!glow.IsNone)
                glow.Set("blend", light);
        }
        return new Visual(made.Id);
    }

    public static Visual? Play(GameInstance on, Sprite sprite, FxOptions? options = null) => Play(on, (int)sprite, options);
}
