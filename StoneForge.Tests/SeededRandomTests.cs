using StoneForge;

// Game.WithSeed: the game's random generator seeded for a while, then carrying on - without replaying the numbers it
// drew before, as setting random_get_seed's seed back would (laid out with FakeGame's random generator).
public class SeededRandomTests : FakeGame
{
    private readonly FakeRandom _random = new(5);

    public SeededRandomTests() => Rng = _random;

    private static long Draw() => (long)Game.CallBuiltin("irandom", 1_000_000).AsReal;

    private static long[] Draws(int count) => Enumerable.Range(0, count).Select(_ => Draw()).ToArray();

    [Fact]
    public void The_same_seed_draws_the_same_numbers()
    {
        var first = Game.WithSeed(42, () => Draws(5));
        Draw();
        var second = Game.WithSeed(42, () => Draws(5));
        Assert.Equal(first, second);
        Assert.NotEqual(first, Game.WithSeed(43, () => Draws(5)));
    }

    [Fact]
    public void After_it_the_generator_carries_on_without_replaying()
    {
        var before = Draws(3);
        Game.WithSeed(42, () => Draws(5));
        var after = Draws(3);
        // (Setting random_get_seed's seed back would start the sequence over: the same three again.)
        Assert.NotEqual(before, after);
        Assert.NotEqual(5, _random.Seed);
    }

    [Fact]
    public void A_seeded_game_stays_the_same_everywhere()
    {
        long[] Play()
        {
            Game.CallBuiltin("random_set_seed", 7);
            Draws(2);
            Game.WithSeed(42, () => Draws(5));
            return Draws(3);
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void It_carries_on_when_the_action_throws()
    {
        Game.CallBuiltin("random_set_seed", 7);
        Assert.Throws<InvalidOperationException>(() => Game.WithSeed(42, () => throw new InvalidOperationException()));
        var thrown = Draws(3);
        Game.CallBuiltin("random_set_seed", 7);
        Game.WithSeed(42, () => { });
        Assert.Equal(Draws(3), thrown);
    }

    [Fact]
    public void Nested_seeds_each_carry_on()
    {
        var (outer, inner, rest) = Game.WithSeed(1, () =>
        {
            long a = Draw();
            var nested = Game.WithSeed(2, () => Draws(2));
            return (a, nested, Draw());
        });
        Assert.Equal(Game.WithSeed(2, () => Draws(2)), inner);
        Assert.Equal(outer, Game.WithSeed(1, Draw));
        Assert.Equal(rest, Game.WithSeed(1, () =>
        {
            Draw();
            Game.WithSeed(2, () => { });
            return Draw();
        }));
    }

    [Theory]
    [InlineData(42L, 42L)]
    [InlineData(-1L, int.MaxValue - 1L)]
    [InlineData(int.MaxValue, 0L)]
    [InlineData(long.MaxValue, long.MaxValue % int.MaxValue)]
    public void Any_number_is_brought_into_the_games_range(long seed, long set)
    {
        Game.WithSeed(seed, () => { });
        // (Set: the seed, then the one it carries on with.)
        Assert.Equal(set, _random.SeedsSet[^2]);
    }
}
