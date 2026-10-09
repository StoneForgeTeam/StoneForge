namespace StoneForge;

/// <summary>The state of a response with a registered dialogue condition.</summary>
public enum DialogConditionResult
{
    /// <summary>Show the response, but do not allow selecting it.</summary>
    Visible,
    /// <summary>Show the response and allow selecting it, subject to the game's own locks.</summary>
    Enabled,
    /// <summary>Do not show the response.</summary>
    Hidden,
}

/// <summary>Registers a synchronous static method returning DialogConditionResult,
/// with no arguments or one DialogOptionContext, as modid:key.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DialogConditionAttribute : Attribute
{
    public DialogConditionAttribute(string key) { ModIdentity.CheckKey(key, "dialogue condition"); Key = key; }
    public string Key { get; }
}
