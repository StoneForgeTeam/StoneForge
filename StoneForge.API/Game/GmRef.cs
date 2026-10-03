using System.Collections.Concurrent;

namespace StoneForge;

/// <summary>A GameMaker array or struct held from C# (<see cref="GmArray"/>, <see cref="GmStruct"/>): the game's own
/// value, by reference - changes through it are changes to the game's, and passing it back in passes that one. Kept
/// alive for the game's garbage collector as long as C# holds it; <see cref="Dispose"/> lets go of it at once,
/// otherwise it's let go of once C# no longer refers to it. Use it on the game's thread, as any game access.</summary>
public abstract class GmRef : IDisposable, IEquatable<GmRef>
{
    // (Let go of on the game's thread, at the start of a frame: a finalizer runs on its own.)
    private static readonly ConcurrentQueue<long> Released = new();
    private long _id;

    private protected GmRef(long id, IntPtr pointer)
    {
        _id = id;
        Pointer = pointer;
    }

    ~GmRef()
    {
        if (_id != 0)
            Released.Enqueue(_id);
    }

    // The game's own pointer to it: two references to the same array or struct are equal.
    internal IntPtr Pointer { get; }

    // Its id with the bridge, for passing it back in.
    internal long Id => _id != 0 ? _id : throw new ObjectDisposedException(GetType().Name, "This game array or struct was disposed.");

    /// <summary>Lets go of it now (it can't be used after).</summary>
    public void Dispose()
    {
        if (_id == 0)
            return;
        Released.Enqueue(_id);
        _id = 0;
        GC.SuppressFinalize(this);
    }

    internal static unsafe void ReleaseQueued()
    {
        // (A bridge without it - only a test's - holds nothing to let go of.)
        if (Released.IsEmpty || Game.Api == null || Game.Api->ReleaseRefs == null)
            return;
        Span<long> batch = stackalloc long[256];
        int count = 0;
        while (Released.TryDequeue(out long id))
        {
            batch[count++] = id;
            if (count == batch.Length)
            {
                fixed (long* ids = batch) Game.Api->ReleaseRefs(ids, count);
                count = 0;
            }
        }
        if (count > 0)
            fixed (long* ids = batch) Game.Api->ReleaseRefs(ids, count);
    }

    /// <summary>Its JSON (the game's json_stringify).</summary>
    public string ToJson() => Game.CallBuiltin("json_stringify", AsValue()).AsString;

    // As a GmValue, to pass to the game.
    internal abstract GmValue AsValue();

    public bool Equals(GmRef? other) => other is not null && other.GetType() == GetType() && other.Pointer == Pointer;
    public override bool Equals(object? obj) => obj is GmRef other && Equals(other);
    public override int GetHashCode() => Pointer.GetHashCode();
}
