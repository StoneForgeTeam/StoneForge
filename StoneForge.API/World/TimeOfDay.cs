namespace StoneForge;

/// <summary>The time of day, as the game divides it (o_time_controller's time_period): what NPCs follow - where they
/// work, sleep, carry a lantern.</summary>
public enum TimeOfDay
{
    /// <summary>6:00 to 11:59.</summary>
    Morning,
    /// <summary>12:00 to 18:59.</summary>
    Day,
    /// <summary>19:00 to 22:59.</summary>
    Evening,
    /// <summary>23:00 to 5:59.</summary>
    Night,
}
