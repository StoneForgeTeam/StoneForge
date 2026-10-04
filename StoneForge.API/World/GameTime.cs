using System.Globalization;

namespace StoneForge;

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
