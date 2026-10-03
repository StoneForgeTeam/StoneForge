namespace StoneForge;

/// <summary>A GameMaker struct, the game's own (<see cref="GmRef"/>): a script's argument or result, json_parse's...
/// Read and change its members in place - <c>settings["Modification"]</c> - or make a new one (<see cref="Create"/>)
/// to hand to the game.</summary>
public sealed class GmStruct : GmRef
{
    internal GmStruct(long id, IntPtr pointer) : base(id, pointer) { }

    internal override GmValue AsValue() => this;

    /// <summary>A member (variable_struct_get / variable_struct_set); undefined if it has none by that name.</summary>
    public GmValue this[string name]
    {
        get => Game.CallBuiltin("variable_struct_get", this, name);
        set => Game.CallBuiltin("variable_struct_set", this, name, value);
    }

    /// <summary>Whether it has a member by that name (variable_struct_exists).</summary>
    public bool Has(string name) => Game.CallBuiltin("variable_struct_exists", this, name).AsBool;

    /// <summary>Takes a member out (variable_struct_remove).</summary>
    public void Remove(string name) => Game.CallBuiltin("variable_struct_remove", this, name);

    /// <summary>Its members' names (variable_struct_get_names).</summary>
    public string[] Names
    {
        get
        {
            using GmArray? names = Game.CallBuiltin("variable_struct_get_names", this).AsArray;
            return names == null ? Array.Empty<string>() : names.ToArray().Select(name => name.AsString).ToArray();
        }
    }

    /// <summary>How many members it has (variable_struct_names_count).</summary>
    public int Count => Game.CallBuiltin("variable_struct_names_count", this).AsInt;

    /// <summary>A new, empty game struct.</summary>
    public static GmStruct Create() => FromJson("{}") ?? throw GmArray.Unmade("json_parse");

    /// <summary>The struct a JSON text describes (json_parse), or null if it isn't one.</summary>
    public static GmStruct? FromJson(string json) => Game.CallBuiltin("json_parse", json).AsStruct;

    public override string ToString() => "struct";
}
