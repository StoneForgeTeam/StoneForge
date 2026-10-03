using System.Globalization;

namespace StoneForge;

/// <summary>A GameMaker value: a number, string, bool, instance, array (<see cref="GmArray"/>), struct
/// (<see cref="GmStruct"/>), or undefined. Converts implicitly to and from
/// the C# types, so <c>double hp = player.HP;</c> and <c>player.HP = 50;</c> just work. Reading a value as
/// the wrong type gives that type's default (a string as a number: 0) rather than throwing.</summary>
public readonly struct GmValue : IEquatable<GmValue>
{
    public GmKind Kind { get; }
    private readonly double _real;
    private readonly string? _string;
    private readonly Instance _instance;
    private readonly GmRef? _ref;

    private GmValue(GmKind kind, double real = 0, string? text = null, Instance instance = default, GmRef? reference = null)
    {
        Kind = kind;
        _real = real;
        _string = text;
        _instance = instance;
        _ref = reference;
    }

    public static readonly GmValue Undefined = default;

    public bool IsUndefined => Kind == GmKind.Undefined;

    /// <summary>The number (a bool: 1 / 0); 0 if it isn't one.</summary>
    public double AsReal => Kind is GmKind.Real or GmKind.Bool ? _real : 0;
    public int AsInt => (int)AsReal;
    /// <summary>GameMaker truthiness: a number above 0.5, or true.</summary>
    public bool AsBool => Kind is GmKind.Real or GmKind.Bool && _real > 0.5;
    /// <summary>The string, or the value as text.</summary>
    public string AsString => Kind == GmKind.String ? _string! : ToString();
    public Instance AsInstance => Kind == GmKind.Instance ? _instance : default;
    /// <summary>The array; null if it isn't one.</summary>
    public GmArray? AsArray => Kind == GmKind.Array ? (GmArray)_ref! : null;
    /// <summary>The struct; null if it isn't one.</summary>
    public GmStruct? AsStruct => Kind == GmKind.Struct ? (GmStruct)_ref! : null;

    /// <summary>The instance as the typed wrapper <typeparamref name="T"/> (an object class from the
    /// generated API, e.g. <c>Objects.o_player</c>).</summary>
    public T? As<T>() where T : GameInstance, new() => Kind == GmKind.Instance ? GameInstance.Wrap<T>(_instance) : null;

    public static implicit operator GmValue(double v) => new(GmKind.Real, v);
    public static implicit operator GmValue(int v) => new(GmKind.Real, v);
    public static implicit operator GmValue(float v) => new(GmKind.Real, v);
    public static implicit operator GmValue(bool v) => new(GmKind.Bool, v ? 1 : 0);
    public static implicit operator GmValue(string? v) => v == null ? Undefined : new(GmKind.String, text: v);
    public static implicit operator GmValue(Instance v) => v.IsNone ? Undefined : new(GmKind.Instance, instance: v);
    public static implicit operator GmValue(GameInstance? v) => v == null ? Undefined : (GmValue)v.Instance;
    public static implicit operator GmValue(GmArray? v) => v == null ? Undefined : new(GmKind.Array, reference: v);
    public static implicit operator GmValue(GmStruct? v) => v == null ? Undefined : new(GmKind.Struct, reference: v);
    /// <summary>An asset id (object, sprite, sound, room) from the generated enums.</summary>
    public static GmValue From<TEnum>(TEnum asset) where TEnum : Enum => Convert.ToDouble(asset, CultureInfo.InvariantCulture);

    public static implicit operator double(GmValue v) => v.AsReal;
    public static implicit operator int(GmValue v) => v.AsInt;
    public static implicit operator bool(GmValue v) => v.AsBool;
    public static implicit operator string(GmValue v) => v.AsString;
    public static implicit operator Instance(GmValue v) => v.AsInstance;
    public static implicit operator GmArray?(GmValue v) => v.AsArray;
    public static implicit operator GmStruct?(GmValue v) => v.AsStruct;

    // Arithmetic as GameMaker does it: + joins when either side is a string, otherwise numbers throughout.
    // (C# picks these over its own number operators, so player.HP + 10 needs no casts.)
    public static GmValue operator +(GmValue a, GmValue b) => a.Kind == GmKind.String || b.Kind == GmKind.String ? a.AsString + b.AsString : a.AsReal + b.AsReal;
    public static GmValue operator -(GmValue a, GmValue b) => a.AsReal - b.AsReal;
    public static GmValue operator *(GmValue a, GmValue b) => a.AsReal * b.AsReal;
    public static GmValue operator /(GmValue a, GmValue b) => a.AsReal / b.AsReal;
    public static GmValue operator %(GmValue a, GmValue b) => a.AsReal % b.AsReal;
    public static GmValue operator -(GmValue a) => -a.AsReal;
    public static bool operator <(GmValue a, GmValue b) => a.AsReal < b.AsReal;
    public static bool operator >(GmValue a, GmValue b) => a.AsReal > b.AsReal;
    public static bool operator <=(GmValue a, GmValue b) => a.AsReal <= b.AsReal;
    public static bool operator >=(GmValue a, GmValue b) => a.AsReal >= b.AsReal;

    public bool Equals(GmValue other) => Kind == other.Kind && _real.Equals(other._real) && _string == other._string && _instance.Equals(other._instance)
        && Equals(_ref, other._ref);
    public override bool Equals(object? obj) => obj is GmValue other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Kind, _real, _string, _instance, _ref);
    public static bool operator ==(GmValue a, GmValue b) => a.Equals(b);
    public static bool operator !=(GmValue a, GmValue b) => !a.Equals(b);

    public override string ToString() => Kind switch
    {
        GmKind.Real => _real.ToString(CultureInfo.InvariantCulture),
        GmKind.Bool => _real != 0 ? "true" : "false",
        GmKind.String => _string!,
        GmKind.Instance => _instance.ToString(),
        GmKind.Array or GmKind.Struct => _ref!.ToString(),
        _ => "undefined",
    };
}
