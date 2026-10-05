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

    // Whether it's a pointer the game lent for the callback under way - good while that runs, even in the instance's own
    // Destroy event, where the game already counts it gone (instance_exists: false).
    internal bool IsLentNow => Pointer != IntPtr.Zero && _lifetime?.Active == true;

    internal bool CanUsePointer => Pointer != IntPtr.Zero && _lifetime?.Active == true
        && (Id < 0 || Game.CallBuiltin("instance_exists", Id).AsBool);

    /// <summary>An ID-based reference suitable for keeping between callbacks. Structs have no instance ID.</summary>
    public Instance Persist() => Id >= 0 ? FromId(Id) : IsNone ? default
        : throw new InvalidOperationException("Struct/global handles cannot be persisted as instances.");

    /// <summary>Refers to nothing at all.</summary>
    public bool IsNone => Pointer == IntPtr.Zero && Id < 0;

    /// <summary>Whether the room instance exists, or a temporary handle's callback is still active. An instance the game
    /// has culled - off screen, deactivated - doesn't exist to GameMaker either: see <see cref="IsCulled"/> and
    /// <see cref="IsGone"/>.</summary>
    public bool Exists => Id >= 0 ? Game.CallBuiltin("instance_exists", Id).AsBool : CanUsePointer;

    /// <summary>Whether the game has culled it: taken it off screen and deactivated it (o_cullingController - ground
    /// loot, decorations...). It's still in the world - it comes back when it's on screen again - but GameMaker's
    /// <c>with</c> and <c>instance_exists</c> skip it, and its own variables can't be read or set meanwhile: its
    /// built-ins can (<c>x</c>, <c>object_index</c>...). Keep a mod's data about it by its id, not on it.</summary>
    public bool IsCulled => Id >= 0 && !Game.CallBuiltin("instance_exists", Id).AsBool && Culling.Contains(Id);

    /// <summary>Whether it has really left the world (destroyed, picked up): it neither exists nor is culled - where
    /// <see cref="Exists"/> is false for an instance that's only off screen.</summary>
    public bool IsGone => Id >= 0 ? !Game.CallBuiltin("instance_exists", Id).AsBool && !Culling.Contains(Id) : !CanUsePointer;

    /// <summary>Destroys it - its Destroy event run, unless <paramref name="runDestroyEvent"/> is false - culled or not:
    /// a culled one is taken out of the culling controller's list first (destroying it there would leave the controller
    /// reading a destroyed instance). Nothing happens if it's already gone.</summary>
    public void Destroy(bool runDestroyEvent = true)
    {
        if (Id < 0)
            throw new InvalidOperationException("Only a room instance can be destroyed.");
        if (!Game.CallBuiltin("instance_exists", Id).AsBool && !Culling.Release(Id))
            return;
        Game.CallBuiltin("instance_destroy", Id, runDestroyEvent);
    }

    /// <summary>The instance with GameMaker instance id <paramref name="id"/>.</summary>
    public static Instance FromId(int id) => new(id);

    /// <summary>The instance a value the game keeps for one names - a reference, or its id as a number (a unit's target,
    /// an effect's owner, an entry in a list of units) - by its id; none if it names none (noone, -4, undefined).</summary>
    public static Instance Of(GmValue value) => value.Kind switch
    {
        GmKind.Instance => value.AsInstance.Persist(),
        GmKind.Real when value.AsReal >= 0 => FromId(value.AsInt),
        _ => default,
    };

    /// <summary>A variable's value (undefined if it's missing, or there's no such instance).</summary>
    public GmValue Get(string name)
    {
        if (CanUsePointer)
            return Game.GetVar(Pointer, name);
        if (Pointer != IntPtr.Zero && Id < 0)
            throw new InvalidOperationException("This temporary struct/global handle has expired.");
        if (Id < 0)
            return GmValue.Undefined;
        if (!Exists)
            // (Culled: its built-ins through the engine's accessors, its pointer found among the room's deactivated
            // instances; its own variables aren't reachable. Undefined if it can't be found.)
            return Culling.Contains(Id) && Game.CulledPointer(Id) is var culled && culled != IntPtr.Zero
                ? Game.GetVar(culled, name) : GmValue.Undefined;
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
        if (Id < 0)
            return false;
        if (!Exists)
            return Culling.Contains(Id)
                ? throw new InvalidOperationException($"Instance {Id} is culled (off screen): its variables can't be set until it's back. Keep a mod's data about it by its id.")
                : false;
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
