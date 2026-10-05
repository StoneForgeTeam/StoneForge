namespace StoneForge;

/// <summary>When a mod's hook runs among every mod's hooks on the same script or code entry - an <c>order</c>: lower runs
/// first, then in the order they were added (load order) for the same number. 0 (<see cref="Normal"/>) unless said; these
/// are names for some, and any number between goes (<c>HookOrder.Late + 10</c>: just after the Late ones). A before hook
/// that replaces a script has its result used over those before it - so a mod that must have the last word on a script
/// hooks it <see cref="Last"/>, and one that only watches, <see cref="First"/>.</summary>
public static class HookOrder
{
    public const int First = -200;
    public const int Early = -100;
    public const int Normal = 0;
    public const int Late = 100;
    public const int Last = 200;

    /// <summary>An order as said in messages: its name if it has one ("Late"), else the number ("110").</summary>
    public static string Name(int order) => order switch
    {
        First => nameof(First),
        Early => nameof(Early),
        Normal => nameof(Normal),
        Late => nameof(Late),
        Last => nameof(Last),
        _ => order.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };
}
