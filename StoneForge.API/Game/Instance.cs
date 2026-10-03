namespace StoneForge;

/// <summary>A GameMaker instance (an object in the room) or struct, untyped: variables by name. The generated
/// object classes (<c>Objects.o_player</c>...) wrap this with typed properties.</summary>
/// <remarks>Room instances capture their ID when received. Their pointer may only be used inside the
/// callback that lent it; later variable access resolves by ID. Struct/global pointers expire with the
/// callback and cannot be persisted. Calls requiring an instance pointer fail if the runner cannot resolve it.</remarks>
public readonly struct Instance : IEquatable<Instance>
{
    internal readonly IntPtr Pointer;
    // (Stored plus one, so default(Instance) - a field never set - is none, not instance id 0.)
    private readonly int _idPlusOne;
    internal int Id => _idPlusOne - 1;
    private readonly CallbackLifetime? _lifetime;
    internal unsafe Instance(IntPtr pointer)
    {
        Game.EnsureGameThread();
        Pointer = pointer;
        _lifetime = CallbackLifetime.Current;
        int id = pointer == IntPtr.Zero ? -1 : Game.Api->InstanceId(pointer);
        _idPlusOne = id < 0 ? 0 : checked(id + 1);
    }
    private Instance(int id) { Pointer = IntPtr.Zero; _idPlusOne = id < 0 ? 0 : checked(id + 1); _lifetime = null; }

    internal bool CanUsePointer => Pointer != IntPtr.Zero && _lifetime?.Active == true
        && (Id < 0 || Game.CallBuiltin("instance_exists", Id).AsBool);

    /// <summary>An ID-based reference suitable for keeping between callbacks. Structs have no instance ID.</summary>
    public Instance Persist() => Id >= 0 ? FromId(Id) : IsNone ? default
        : throw new InvalidOperationException("Struct/global handles cannot be persisted as instances.");

    /// <summary>Refers to nothing at all.</summary>
    public bool IsNone => Pointer == IntPtr.Zero && Id < 0;

    /// <summary>Whether the room instance exists, or a temporary handle's callback is still active.</summary>
    public bool Exists => Id >= 0 ? Game.CallBuiltin("instance_exists", Id).AsBool : CanUsePointer;

    /// <summary>The instance with GameMaker instance id <paramref name="id"/>.</summary>
    public static Instance FromId(int id) => new(id);

    /// <summary>A variable's value (undefined if it's missing, or there's no such instance).</summary>
    public GmValue Get(string name)
    {
        if (CanUsePointer)
            return Game.GetVar(Pointer, name);
        if (Pointer != IntPtr.Zero && Id < 0)
            throw new InvalidOperationException("This temporary struct/global handle has expired.");
        if (Id < 0 || !Exists)
            return GmValue.Undefined;
        return Game.CallBuiltin("variable_instance_get", Id, name);
    }

    /// <summary>Sets a variable (creating it on the instance if it doesn't exist). False if there's no
    /// such instance.</summary>
    public bool Set(string name, GmValue value)
    {
        if (CanUsePointer)
            return Game.SetVar(Pointer, name, value);
        if (Pointer != IntPtr.Zero && Id < 0)
            throw new InvalidOperationException("This temporary struct/global handle has expired.");
        if (Id < 0 || !Exists)
            return false;
        Game.CallBuiltin("variable_instance_set", Id, name, value);
        return true;
    }

    public GmValue this[string name]
    {
        get => Get(name);
        set => Set(name, value);
    }

    /// <summary>Its alarms, GameMaker's <c>alarm[0]</c> to <c>alarm[11]</c>: steps until each goes off, -1 when one is
    /// off. <c>instance.Alarm[2] = -1</c> stops one; <c>instance.Alarm[4] = 1</c> sets one for the next step.</summary>
    public Alarms Alarm => new(this);

    /// <summary>This instance as the typed wrapper <typeparamref name="T"/>.</summary>
    public T As<T>() where T : GameInstance, new() => GameInstance.Wrap<T>(this);

    public bool Equals(Instance other) => Pointer == other.Pointer && Id == other.Id;
    public override bool Equals(object? obj) => obj is Instance other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Pointer, Id);
    public override string ToString() => Pointer != IntPtr.Zero ? $"Instance 0x{Pointer:X}" : Id >= 0 ? $"Instance id {Id}" : "Instance (none)";
}
