namespace StoneForge;

// Nested callbacks get independent leases. An outer lease survives a nested call, but never its return.
internal sealed class CallbackLifetime : IDisposable
{
    [ThreadStatic] private static CallbackLifetime? _current;
    private readonly CallbackLifetime? _previous;
    internal bool Active { get; private set; } = true;
    internal static CallbackLifetime? Current => _current;

    internal CallbackLifetime()
    {
        _previous = _current;
        _current = this;
    }

    public void Dispose()
    {
        Active = false;
        _current = _previous;
    }
}
