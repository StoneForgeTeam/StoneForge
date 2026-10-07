using StoneForge;

// Attacks as events (Combat.OnAttack, OnHit): told after the game's outcome scripts - each run as the attacker, on its
// target - with the damage they dealt (laid out with FakeGame's room and scripts).
public class CombatEventsTests : FakeGame
{
    private const int Wolf = 100_001, PlayerId = 100_002;
    private readonly FakeWorld _world = new();
    private readonly FakeScripts _scripts = new();
    private readonly ModContext _context = new("combat_events_test");

    public CombatEventsTests()
    {
        World = _world;
        GameScripts = _scripts;
        _world.LendsIds = true;
        _world.ExistsByObject = true;
        _world.Add(Wolf, 300);
        _world.Add(PlayerId, (int)GameObjectId.o_player);
        _world.Vars[Wolf] = new() { ["shoot_attack"] = false };
        _scripts.Add("scr_attack_result_hit", _ => 12.0);
        _scripts.Add("scr_attack_result_dodge", _ => 0.0);
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    [Fact]
    public void Every_attack_is_told_with_its_outcome_and_hits_alone_too()
    {
        var attacks = new List<Attack>();
        var hits = new List<Attack>();
        Combat.OnAttack(_context, attacks.Add);
        Combat.OnHit(_context, hits.Add);

        // (The wolf's bite crits the player - a hit's seventh argument; then the player dodges.)
        Game.CallScript("scr_attack_result_hit", Instance.FromId(Wolf), Instance.FromId(PlayerId), 0, 0, 0, 0, 0, true);
        Game.CallScript("scr_attack_result_dodge", Instance.FromId(Wolf), Instance.FromId(PlayerId));

        Assert.Equal(new[] { AttackResult.Crit, AttackResult.Dodge }, attacks.Select(a => a.Result));
        var bite = attacks[0];
        Assert.Equal((Instance.FromId(Wolf), Instance.FromId(PlayerId), 12.0), (bite.Attacker.Instance, bite.Target.Instance, bite.Damage));
        Assert.True(bite.OnPlayer);
        Assert.False(bite.ByPlayer);
        Assert.False(bite.IsRanged);
        // (The dodge isn't a hit.)
        var hit = Assert.Single(hits);
        Assert.Equal((AttackResult.Crit, 12.0), (hit.Result, hit.Damage));
    }
}
