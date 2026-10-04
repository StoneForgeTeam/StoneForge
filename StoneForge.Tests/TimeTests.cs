using StoneForge;

// The game's clock (Time, GameTime): the calendar's arithmetic - timestamps, the time of day's bounds - and reading,
// setting and advancing the clock through the game's own scripts (laid out with FakeGame's ds maps and scripts).
public class TimeTests : FakeGame
{
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    private readonly List<GmValue[]> _sets = new(), _advances = new();

    public TimeTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        _scripts.Add("scr_timeSet", args => { _sets.Add(args); return GmValue.Undefined; });
        _scripts.Add("scr_timePartsUpdate", args => { _advances.Add(args); return GmValue.Undefined; });
        _scripts.Add("scr_timeIsFrozen", _ => false);
    }

    // A game's clock, as scr_timeMapInit makes it.
    private DsMap Clock(int months, int days, int hours, int minutes, int seconds = 0)
    {
        var clock = DsMap.Create();
        clock["seconds"] = seconds;
        clock["minutes"] = minutes;
        clock["hours"] = hours;
        clock["days"] = days;
        clock["months"] = months;
        Globals["timeDataMap"] = clock;
        return clock;
    }

    [Fact]
    public void A_moment_has_the_games_timestamp()
    {
        var time = new GameTime(months: 1, days: 2, hours: 3, minutes: 4);
        Assert.Equal(4 + 3 * 60 + 2 * 1440 + 1 * 43200, time.Timestamp);
        Assert.Equal(time, GameTime.FromTimestamp(time.Timestamp));
        Assert.Equal(new GameTime(0, 0, 0, 0), GameTime.FromTimestamp(0));
        Assert.Equal(new GameTime(0, 29, 23, 59), GameTime.FromTimestamp(GameTime.MinutesPerMonth - 1));
        Assert.Equal(0.5, new GameTime(0, 0, 12, 0).DayFraction);
        Assert.Equal("month 1, day 2, 03:04", time.ToString());
    }

    [Theory]
    [InlineData(5, 59, TimeOfDay.Night)]
    [InlineData(6, 0, TimeOfDay.Morning)]
    [InlineData(11, 59, TimeOfDay.Morning)]
    [InlineData(12, 0, TimeOfDay.Day)]
    [InlineData(18, 59, TimeOfDay.Day)]
    [InlineData(19, 0, TimeOfDay.Evening)]
    [InlineData(22, 59, TimeOfDay.Evening)]
    [InlineData(23, 0, TimeOfDay.Night)]
    [InlineData(0, 0, TimeOfDay.Night)]
    public void The_time_of_day_is_the_games(int hours, int minutes, TimeOfDay expected)
        => Assert.Equal(expected, new GameTime(0, 0, hours, minutes).OfDay);

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, 30, 0, 0)]
    [InlineData(0, 0, 24, 0)]
    [InlineData(0, 0, 0, 60)]
    public void A_moment_outside_the_calendar_throws(int months, int days, int hours, int minutes)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new GameTime(months, days, hours, minutes));

    [Fact]
    public void A_negative_timestamp_throws() => Assert.Throws<ArgumentOutOfRangeException>(() => GameTime.FromTimestamp(-1));

    [Fact]
    public void The_clock_is_read_from_the_games_time_map()
    {
        Clock(months: 2, days: 5, hours: 20, minutes: 15, seconds: 30);
        Assert.True(Time.Available);
        Assert.Equal(new GameTime(2, 5, 20, 15, 30), Time.Now);
        Assert.Equal(TimeOfDay.Evening, Time.OfDay);
        Assert.Equal(new GameTime(2, 5, 20, 15).Timestamp, Time.Timestamp);
        Assert.False(Time.IsFrozen);
    }

    [Fact]
    public void With_no_game_there_is_no_clock()
    {
        Assert.False(Time.Available);
        Assert.Throws<InvalidOperationException>(() => Time.Now);
        Assert.Throws<InvalidOperationException>(() => Time.Set(new GameTime(0, 0, 10, 0)));
        Assert.False(Time.IsFrozen);
    }

    [Fact]
    public void Setting_goes_through_the_games_scr_timeSet()
    {
        Clock(0, 0, 10, 0);
        Time.Set(new GameTime(months: 1, days: 2, hours: 3, minutes: 4, seconds: 5));
        // (Its arguments: seconds, minutes, hours, days, months.)
        Assert.Equal(new GmValue[] { 5, 4, 3, 2, 1 }, Assert.Single(_sets));
        Assert.Empty(_advances);
    }

    [Fact]
    public void Advancing_goes_through_the_games_minute_by_minute_update()
    {
        Clock(0, 0, 10, 0);
        Time.Advance(90);
        Assert.Equal(new GmValue[] { 90 }, Assert.Single(_advances));
        Time.Advance(0);
        Assert.Single(_advances);
        Assert.Throws<ArgumentOutOfRangeException>(() => Time.Advance(-1));
    }
}
