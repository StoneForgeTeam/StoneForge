namespace StoneForge;

/// <summary>A mod's setting of type <typeparamref name="T"/> (<see cref="ModContext.Settings"/>): read
/// <see cref="Value"/> whenever it's needed, or follow <see cref="Changed"/>. Setting <see cref="Value"/> saves
/// it (and raises <see cref="Changed"/>).</summary>
public abstract class ModSetting<T> : ModSetting
{
    private T _value;

    private protected ModSetting(ModSettings owner, string key, string label, T defaultValue, string? tooltip) : base(owner, key, label, tooltip)
    {
        Default = defaultValue;
        _value = defaultValue;
    }

    public T Default { get; }

    public T Value
    {
        get => _value;
        set
        {
            value = Fit(value);
            if (EqualityComparer<T>.Default.Equals(value, _value))
                return;
            _value = value;
            Owner.MarkChanged();
            if (Changed == null)
                return;
            foreach (Action<T> handler in Changed.GetInvocationList())
            {
                try { handler(value); }
                catch (Exception e) { Game.Log($"[{Owner.Mod}] setting {Key}: Changed handler threw: {e}"); }
            }
        }
    }

    /// <summary>The player (or the mod) changed it: its new value.</summary>
    public event Action<T>? Changed;

    public override void Reset() => Value = Default;

    public static implicit operator T(ModSetting<T> setting) => setting.Value;

    // A value made one it can have (a slider's in its range, on its steps...).
    private protected virtual T Fit(T value) => value;

    // Loaded from the save, without saving it again or raising Changed.
    private protected void SetLoaded(T value) => _value = Fit(value);
}
