namespace StoneForge;

/// <summary>One of the game's object events (generated as <c>Events.o_player.Step_0</c>...), handing your
/// handler the instance as its typed class.</summary>
public sealed class CodeEvent<T> where T : GameInstance, new()
{
    public string CodeName { get; }
    public CodeEvent(string codeName) => CodeName = codeName;

    /// <summary>Runs before the game's code. Return true to skip the game's code.</summary>
    public void Before(ModContext context, Func<T, bool> handler, int order = HookOrder.Normal)
        => context.OnCode(CodeName, before: (self, _) => handler(GameInstance.Wrap<T>(self)), order: order);

    /// <summary>Runs before the game's code, with the event's "other" instance. Return true to skip it.</summary>
    public void Before(ModContext context, Func<T, Instance, bool> handler, int order = HookOrder.Normal)
        => context.OnCode(CodeName, before: (self, other) => handler(GameInstance.Wrap<T>(self), other), order: order);

    /// <summary>Runs after the game's code.</summary>
    public void After(ModContext context, Action<T> handler, int order = HookOrder.Normal)
        => context.OnCode(CodeName, after: (self, _) => handler(GameInstance.Wrap<T>(self)), order: order);

    /// <summary>Runs after the game's code, with the event's "other" instance.</summary>
    public void After(ModContext context, Action<T, Instance> handler, int order = HookOrder.Normal)
        => context.OnCode(CodeName, after: (self, other) => handler(GameInstance.Wrap<T>(self), other), order: order);

    public override string ToString() => CodeName;
}
