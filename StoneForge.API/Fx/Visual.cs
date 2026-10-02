namespace StoneForge;

/// <summary>An animation playing on a unit (<see cref="Fx.Play(GameInstance, int, FxOptions?)"/>).</summary>
public sealed class Visual
{
    internal Visual(int id) => Id = id;

    internal int Id { get; }
    /// <summary>The game's instance (an o_stonemod_fx).</summary>
    public Instance Instance => Instance.FromId(Id);
    /// <summary>Whether it's still playing.</summary>
    public bool Playing => Game.CallBuiltinTrusted("instance_exists", default, default, Id).AsBool;

    /// <summary>Stops it (a looping one plays until stopped, or its unit is gone).</summary>
    public void Stop()
    {
        if (Playing)
            Game.CallBuiltinTrusted("instance_destroy", default, default, Id);
    }
}
