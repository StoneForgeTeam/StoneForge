namespace StoneForge;

public static partial class Game
{
    // (GameMaker's seeds: 0 to 2^31 - 2, as random_set_seed keeps them.)
    private const long SeedRange = int.MaxValue;

    /// <summary>Runs <paramref name="action"/> with the game's random generator seeded with <paramref name="seed"/> -
    /// its irandom, random, choose... draw the same numbers every time, in every game - then lets the generator carry on.
    /// <para>GameMaker can't save where its generator is: random_get_seed gives only the seed it started from, and setting
    /// that back replays the numbers drawn since. So before seeding, the seed it carries on with is drawn from it: a game
    /// that's random stays random, one that's seeded stays the same in every game, and no number comes round again. Also
    /// when <paramref name="action"/> throws, and nested.</para>
    /// <para>Any whole number will do: it's brought into GameMaker's range (0 to 2^31 - 2) the same way everywhere.</para></summary>
    public static void WithSeed(long seed, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        WithSeed(seed, () =>
        {
            action();
            return 0;
        });
    }

    /// <inheritdoc cref="WithSeed(long, Action)"/>
    /// <returns>What <paramref name="action"/> returns.</returns>
    public static T WithSeed<T>(long seed, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        GmValue next = CallBuiltin("irandom", SeedRange - 1);
        CallBuiltin("random_set_seed", (double)Seed(seed));
        try
        {
            return action();
        }
        finally
        {
            CallBuiltin("random_set_seed", next);
        }
    }

    // A seed in GameMaker's range, the same for the same number everywhere.
    internal static long Seed(long seed) => (seed % SeedRange + SeedRange) % SeedRange;
}
