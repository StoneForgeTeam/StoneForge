using StoneForge;
using StoneForge.GameDamageTypes;

// Combat.Damage against the fake bridge: what reaches the game's damage scripts, and kinds of damage of a mod's own.
public class CombatTests : FakeGame
{
    private static GameInstance Unit => GameInstance.Wrap<GameInstance>(Instance.FromId(123));
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pure_damage_runs_as_a_room_instance_with_positions(bool sourced)
    {
        var world = DealerWorld(); world.Add(124, 300); world.Vars[124] = new();
        var scripts = new FakeScripts(); GameScripts = scripts;
        scripts.Add("scr_pure_damage", args => args[2]);
        var source = GameInstance.Wrap<GameInstance>(Instance.FromId(124));
        Assert.Equal(5, Combat.Damage(Unit, DamageType.Pure, 5, sourced ? source : null));
        Assert.Equal((IntPtr)(FakeWorld.PointerBase + (sourced ? 124 : 123)), scripts.LastSelf);
    }

    [Fact]
    public void Damage_goes_through_the_games_damage_dealer()
    {
        var world = DealerWorld();
        Combat.Damage(Unit, DamageType.Shock, 12, Unit);
        // (An o_damage_dealer at the target, its shock damage set, its user event 0 run.)
        var (dealer, ev) = Assert.Single(world.UserEvents);
        Assert.Equal(0, ev);
        Assert.Equal(12, world.Vars[dealer]["Shock_Damage"].AsReal);
    }

    // A room with the target in it, where an o_damage_dealer can be made.
    private FakeWorld DealerWorld()
    {
        var world = new FakeWorld();
        world.Add(123, 300);
        world.Vars[123] = new();
        World = world;
        return world;
    }

    [Fact]
    public void Nothing_to_deal_reaches_nothing()
    {
        int before = Calls.Count;
        Assert.Equal(0, Combat.Damage(Unit, DamageType.Fire, 0));
        Assert.Equal(0, Combat.Damage(Unit, new Dictionary<DamageType, double> { [DamageType.Fire] = -3 }));
        Assert.DoesNotContain("event_user", Calls.Skip(before));
    }

    [Fact]
    public void A_gone_target_takes_nothing()
    {
        Alive = false;
        int before = Calls.Count;
        Assert.Equal(0, Combat.Damage(Unit, DamageType.Frost, 5));
        Assert.DoesNotContain("event_user", Calls.Skip(before));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Armor_piercing_is_a_percentage(double piercing)
        => Assert.Throws<ArgumentOutOfRangeException>(() => Combat.Damage(Unit, DamageType.Blunt, 5, options: new DamageOptions { ArmorPiercing = piercing }));

    [Fact]
    public void The_games_kinds_are_generated_from_its_data()
    {
        Assert.Equal(13, DamageType.GameTypes.Count);
        Assert.Equal("Shock", DamageType.Shock.GameName);
        Assert.Equal("Shock_Resistance", DamageType.Shock.Resistance);
        Assert.IsType<Shock>(DamageType.Shock);
        Assert.Null(DamageType.Pure.GameName);
    }

    [Fact]
    public void A_kind_of_a_mods_own_is_dealt_as_the_games_it_inherits()
    {
        var lightning = new Lightning();
        Assert.Equal("Shock", lightning.GameName);
        Assert.Equal("Lightning", lightning.Name);
        var world = DealerWorld();
        Combat.Damage(Unit, lightning, 5, Unit);
        Assert.Equal(5, lightning.Asked);
        Assert.Equal(10, lightning.Dealt?.Amount); // (doubled by Modify)
        Assert.Single(world.UserEvents);
    }

    [Fact]
    public void A_kind_that_modifies_its_damage_away_deals_nothing()
    {
        var warded = new Warded();
        int before = Calls.Count;
        Assert.Equal(0, Combat.Damage(Unit, warded, 7));
        Assert.False(warded.WasDealt);
        Assert.DoesNotContain("event_user", Calls.Skip(before));
    }

    // (Tests see the API's internals, so these say "protected internal"; a mod writes "protected override".)
    private sealed class Lightning : Shock
    {
        public double Asked;
        public DamageHit? Dealt;
        public Lightning() { Name = "Lightning"; }
        protected internal override double Modify(DamageHit hit) { Asked = hit.Amount; return hit.Amount * 2; }
        protected internal override void OnDealt(DamageHit hit) => Dealt = hit;
    }

    private sealed class Warded : Pure
    {
        public bool WasDealt;
        protected internal override double Modify(DamageHit hit) => 0;
        protected internal override void OnDealt(DamageHit hit) => WasDealt = true;
    }
}
