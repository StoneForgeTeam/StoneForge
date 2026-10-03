using System.Collections;

namespace StoneForge;

/// <summary>A GameMaker array, the game's own (<see cref="GmRef"/>): <c>global.playerSpriteArray</c>, a script's
/// argument or result... Read and change it in place - <c>parts[0]</c>, <c>parts[0] = sprite</c>, <c>Push</c> - or make
/// a new one (<see cref="Create"/>, <see cref="From"/>) to hand to the game.</summary>
public sealed class GmArray : GmRef, IReadOnlyList<GmValue>
{
    internal GmArray(long id, IntPtr pointer) : base(id, pointer) { }

    internal override GmValue AsValue() => this;

    /// <summary>How many elements it has (array_length).</summary>
    public int Length => Game.CallBuiltin("array_length", this).AsInt;

    int IReadOnlyCollection<GmValue>.Count => Length;

    /// <summary>An element (array_get / array_set). Setting past the end grows it, as GameMaker does.</summary>
    public GmValue this[int index]
    {
        get => Game.CallBuiltin("array_get", this, index);
        set => Game.CallBuiltin("array_set", this, index, value);
    }

    /// <summary>Adds values at the end (array_push).</summary>
    public void Push(params GmValue[] values)
    {
        var args = new GmValue[values.Length + 1];
        args[0] = this;
        values.CopyTo(args, 1);
        Game.CallBuiltin("array_push", args);
    }

    /// <summary>Puts values in at <paramref name="index"/>, moving the rest along (array_insert).</summary>
    public void Insert(int index, params GmValue[] values)
    {
        var args = new GmValue[values.Length + 2];
        args[0] = this;
        args[1] = index;
        values.CopyTo(args, 2);
        Game.CallBuiltin("array_insert", args);
    }

    /// <summary>Takes out <paramref name="count"/> elements from <paramref name="index"/> (array_delete).</summary>
    public void Delete(int index, int count = 1) => Game.CallBuiltin("array_delete", this, index, count);

    /// <summary>Its elements, copied into a C# array.</summary>
    public GmValue[] ToArray()
    {
        int length = Length;
        var values = new GmValue[length];
        for (int i = 0; i < length; i++)
            values[i] = this[i];
        return values;
    }

    public IEnumerator<GmValue> GetEnumerator() => ((IEnumerable<GmValue>)ToArray()).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>A new game array of <paramref name="length"/> elements, each <paramref name="fill"/> (array_create).</summary>
    public static GmArray Create(int length = 0, GmValue fill = default)
        => Game.CallBuiltin("array_create", length, fill).AsArray ?? throw Unmade("array_create");

    /// <summary>A new game array holding <paramref name="values"/>.</summary>
    public static GmArray From(IEnumerable<GmValue> values)
    {
        var all = values.ToArray();
        var array = Create(all.Length);
        for (int i = 0; i < all.Length; i++)
            array[i] = all[i];
        return array;
    }

    /// <summary>The array a JSON text describes (json_parse), or null if it isn't one.</summary>
    public static GmArray? FromJson(string json) => Game.CallBuiltin("json_parse", json).AsArray;

    internal static InvalidOperationException Unmade(string how) => new($"{how} didn't give an array or struct back.");

    public override string ToString() => "array";
}
