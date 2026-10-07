namespace StoneForge;

/// <summary>What a GameMaker value holds.</summary>
public enum GmKind
{
    Undefined,
    Real,
    String,
    Bool,
    Instance,
    /// <summary>A GameMaker array (<see cref="GmArray"/>).</summary>
    Array,
    /// <summary>A GameMaker struct (<see cref="GmStruct"/>).</summary>
    Struct,
}
