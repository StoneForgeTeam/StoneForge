namespace StoneForge;

/// <summary>A game call the bridge could not complete. Includes the function and native failure reason.</summary>
public sealed class GameCallException : InvalidOperationException
{
    public string Function { get; }
    internal GameCallException(string function, string reason)
        : base($"{function}: {reason}") => Function = function;
}
