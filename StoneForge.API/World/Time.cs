namespace StoneForge;

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
    // date now, as it does then - with its own change event when the period changes. Only when it's changed: its
    // time_period is the period as TimeOfDay numbers it (a mod keeping the clock in step sets it every few frames).
    private static void RefreshTimeOfDay()
    {
        Instance controller = Game.CallBuiltin("instance_find", (int)GameObjectId.o_time_controller, 0);
        if (controller.IsNone)
            return;
        if (controller.Get("time_period") is { Kind: GmKind.Real } period && period.AsInt == (int)Now.OfDay)
            return;
        Game.CallBuiltinAs("event_user", controller, controller, 4);
    }

    private static DsMap? Clock() => Game.Global["timeDataMap"].AsDsMap is { } clock && Parts.All(part => clock.Has(part)) ? clock : null;

    private static DsMap Required() => Clock() ?? throw new InvalidOperationException("There's no game clock: no game is loaded or begun (Time.Available).");
}
