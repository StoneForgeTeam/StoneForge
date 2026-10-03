namespace StoneForge;

/// <summary>An instance's alarms, GameMaker's <c>alarm[0]</c> to <c>alarm[11]</c> (<see cref="Instance.Alarm"/>): the
/// steps until each goes off (its Alarm event), -1 when it's off. Read through the engine's own alarm accessor.</summary>
public sealed class Alarms
{
    /// <summary>How many alarms an instance has.</summary>
    public const int Count = 12;

    private readonly Instance _instance;

    internal Alarms(Instance instance) => _instance = instance;

    /// <summary>Alarm <paramref name="index"/> (0 to 11): steps until it goes off, -1 when it's off.</summary>
    public int this[int index]
    {
        get => Game.GetVarAt(_instance, "alarm", Check(index)).AsInt;
        set
        {
            if (!Game.SetVarAt(_instance, "alarm", Check(index), value))
                throw new GameCallException("alarm", $"couldn't set alarm[{index}] on {_instance}");
        }
    }

    private static int Check(int index) => (uint)index < Count
        ? index : throw new ArgumentOutOfRangeException(nameof(index), index, $"An instance has alarms 0 to {Count - 1}.");
}
