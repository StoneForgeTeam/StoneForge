using System.Globalization;

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

/// <summary>A moment of the game's calendar: months of 30 days, days of 24 hours, hours of 60 minutes (and seconds,
/// which the game only counts toward minutes). Its <see cref="Timestamp"/> is the game's: minutes since the calendar
/// began.</summary>
public readonly record struct GameTime
{
    public const int MinutesPerHour = 60, HoursPerDay = 24, DaysPerMonth = 30;
    public const int MinutesPerDay = MinutesPerHour * HoursPerDay, MinutesPerMonth = MinutesPerDay * DaysPerMonth;

    /// <summary>A moment: <paramref name="months"/> 0 up, <paramref name="days"/> 0-29, <paramref name="hours"/> 0-23,
    /// <paramref name="minutes"/> and <paramref name="seconds"/> 0-59. Anything else throws.</summary>
    public GameTime(int months, int days, int hours, int minutes, int seconds = 0)
    {
        Months = Check(months, 0, int.MaxValue, nameof(months));
        Days = Check(days, 0, DaysPerMonth - 1, nameof(days));
        Hours = Check(hours, 0, HoursPerDay - 1, nameof(hours));
        Minutes = Check(minutes, 0, MinutesPerHour - 1, nameof(minutes));
        Seconds = Check(seconds, 0, 59, nameof(seconds));
    }

    public int Months { get; }
    public int Days { get; }
    public int Hours { get; }
    public int Minutes { get; }
    public int Seconds { get; }

    /// <summary>Minutes since the calendar began (the game's scr_timeGetTimestamp).</summary>
    public long Timestamp => Minutes + (long)Hours * MinutesPerHour + (long)Days * MinutesPerDay + (long)Months * MinutesPerMonth;

    /// <summary>Its time of day.</summary>
    public TimeOfDay OfDay => Hours switch
    {
        >= 6 and < 12 => TimeOfDay.Morning,
        >= 12 and < 19 => TimeOfDay.Day,
        >= 19 and < 23 => TimeOfDay.Evening,
        _ => TimeOfDay.Night,
    };

    /// <summary>How far through its day, 0 at midnight to just under 1 (the game's scr_timeDayNormalized).</summary>
    public double DayFraction => (Hours * MinutesPerHour + Minutes) / (double)MinutesPerDay;

    /// <summary>The moment <paramref name="timestamp"/> minutes after the calendar began (0 up).</summary>
    public static GameTime FromTimestamp(long timestamp, int seconds = 0)
    {
        if (timestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(timestamp), timestamp, "A timestamp is 0 or more.");
        long months = timestamp / MinutesPerMonth;
        long rest = timestamp % MinutesPerMonth;
        return new GameTime(checked((int)months), (int)(rest / MinutesPerDay), (int)(rest % MinutesPerDay / MinutesPerHour),
            (int)(rest % MinutesPerHour), seconds);
    }

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"month {Months}, day {Days}, {Hours:00}:{Minutes:00}");

    private static int Check(int value, int min, int max, string name)
        => value >= min && value <= max ? value : throw new ArgumentOutOfRangeException(name, value, $"{name} is {min} to {max}.");
}

/// <summary>The game's clock: what time it is in the world (<see cref="Now"/>), setting it and moving it on, and how many
/// turns have passed. Game thread only, and only in a game (no clock before one's loaded or begun: <see cref="Available"/>).</summary>
public static class Time
{
    private static readonly string[] Parts = { "seconds", "minutes", "hours", "days", "months" };

    /// <summary>Whether there's a clock: a game loaded or begun.</summary>
    public static bool Available => Clock() != null;

    /// <summary>What time it is in the world.</summary>
    public static GameTime Now
    {
        get
        {
            DsMap clock = Required();
            int Part(string name) => clock[name].AsInt;
            return new GameTime(Part("months"), Part("days"), Part("hours"), Part("minutes"), Part("seconds"));
        }
    }

    /// <summary>Minutes since the calendar began (the game's scr_timeGetTimestamp).</summary>
    public static long Timestamp => Now.Timestamp;

    /// <summary>The time of day now.</summary>
    public static TimeOfDay OfDay => Now.OfDay;

    /// <summary>Whether the game is holding time still (the Black Tablet's ritual: scr_timeIsFrozen).</summary>
    public static bool IsFrozen => Available && Game.CallScript("scr_timeIsFrozen", default).AsBool;

    /// <summary>How many turns the game has completed (o_controller's count); -1 outside a game.</summary>
    public static int Turns
    {
        get
        {
            Instance controller = Game.CallBuiltin("instance_find", (int)GameObjectId.o_controller, 0);
            return controller.IsNone ? -1 : controller.Get("turns").AsInt;
        }
    }

    /// <summary>Sets the clock to <paramref name="time"/> at once (scr_timeSet): nothing in between happens - no hour's
    /// upkeep, no NPC moving on, no contract counting down. To have time pass as it does in play, <see cref="Advance"/>.
    /// The time of day is brought up to date (NPCs follow it from their next turn).</summary>
    public static void Set(GameTime time)
    {
        Required();
        Game.CallScript("scr_timeSet", default, time.Seconds, time.Minutes, time.Hours, time.Days, time.Months);
        RefreshTimeOfDay();
    }

    /// <summary>Lets <paramref name="minutes"/> of game time pass as play does (scr_timePartsUpdate): minute by minute,
    /// with the game's every-minute, hour, day and month effects - upkeep, villages restocking, dungeons resetting,
    /// contracts' deadlines. Each minute runs them, so a long stretch (days) takes a moment. The time of day is brought up
    /// to date. Run as the player, as play runs it (in the player's turn: scr_global_turn) - some of those effects need
    /// it, so with no player it throws. 0 does nothing; less throws.</summary>
    public static void Advance(int minutes)
    {
        if (minutes < 0)
            throw new ArgumentOutOfRangeException(nameof(minutes), minutes, "Time only moves on: 0 minutes or more.");
        Required();
        if (minutes == 0)
            return;
        Instance player = Game.CallBuiltin("instance_find", (int)GameObjectId.o_player, 0);
        if (player.IsNone)
            throw new InvalidOperationException("Time passes in the player's turn: there's no player to run it as.");
        Game.CallScript("scr_timePartsUpdate", player, minutes);
        RefreshTimeOfDay();
    }

    // The time controller works the time of day out when a room starts (its user event 4), then holds it: brought up to
    // date now, as it does then - with its own change event when the period changes.
    private static void RefreshTimeOfDay()
    {
        Instance controller = Game.CallBuiltin("instance_find", (int)GameObjectId.o_time_controller, 0);
        if (!controller.IsNone)
            Game.CallBuiltinAs("event_user", controller, controller, 4);
    }

    private static DsMap? Clock() => Game.Global["timeDataMap"].AsDsMap is { } clock && Parts.All(part => clock.Has(part)) ? clock : null;

    private static DsMap Required() => Clock() ?? throw new InvalidOperationException("There's no game clock: no game is loaded or begun (Time.Available).");
}
