namespace StoneForge;

/// <summary>One call of a hooked GML script.</summary>
public sealed class ScriptCall
{
    internal ScriptCall(string name, Instance self, Instance other, GmValue[] args)
    {
        Name = name;
        Self = self;
        Other = other;
        Args = args;
    }

    /// <summary>The script's name.</summary>
    public string Name { get; }
    /// <summary>The instance it runs as.</summary>
    public Instance Self { get; }
    /// <summary>Its "other" instance.</summary>
    public Instance Other { get; }
    /// <summary>Its arguments, shared with other handlers for this call. Do not dispose array/struct handles
    /// obtained from these values; the caller owns them.</summary>
    public GmValue[] Args { get; }
    /// <summary>What the script returns: set by a before handler that replaces the call; in an after handler, what it
    /// returned - change it to change what the caller gets.</summary>
    public GmValue Result { get; set; }
}
