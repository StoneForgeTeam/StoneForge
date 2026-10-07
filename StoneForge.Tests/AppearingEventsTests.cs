using StoneForge;

// Things coming into play and leaving it, as events: an item onto the ground or off it (GroundItems.OnAdded,
// OnRemoved), a unit spawned or dying (Units.OnSpawned, OnDied). What a place has as it loads - its loaders' doing, or
// the room being built - isn't news (laid out with FakeGame's room).
public class AppearingEventsTests : FakeGame
{
    private const int ControllerId = 100_001, LoaderId = 100_002, Item = 100_010, Loaded = 100_011, Built = 100_012,
        Wolf = 100_020, Bandit = 100_021, PlayerId = 100_030;
    private readonly FakeWorld _world = new();
    private readonly ModContext _context = new("appearing_test");

    public AppearingEventsTests()
    {
        World = _world;
        _world.LendsIds = true;
        _world.Add(ControllerId, (int)GameObjectId.o_controller);
        _world.Add(LoaderId, 300);
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    // An instance made as the game makes it: in the room, then its Create event run.
    private void Make(int id, GameObjectId obj, string create)
    {
        _world.Add(id, (int)obj);
        RunCode(create, id);
    }

    [Fact]
    public void An_item_made_in_play_is_told_on_the_next_frame_and_one_taken_as_it_goes()
    {
        var added = new List<Instance>();
        var removed = new List<Instance>();
        GroundItems.OnAdded(_context, item => added.Add(item.Instance));
        GroundItems.OnRemoved(_context, item => removed.Add(item.Instance));

        // (The place loading its own: its loader's user event.)
        RunCode("gml_Object_o_roomEntityLoader_Other_13", LoaderId, () => Make(Loaded, GameObjectId.o_loot, "gml_Object_o_loot_Create_0"));
        // (The room being built, up to its first frame.)
        RunCode("gml_Object_o_controller_Other_5", ControllerId);
        Make(Built, GameObjectId.o_loot, "gml_Object_o_loot_Create_0");
        RunCode("gml_Object_o_controller_Other_4", ControllerId);
        RunFrame();
        Assert.Empty(added);

        Make(Item, GameObjectId.o_loot, "gml_Object_o_loot_Create_0");
        Assert.Empty(added);
        RunFrame();
        Assert.Equal(new[] { Instance.FromId(Item) }, added);

        RunCode("gml_Object_o_loot_Destroy_0", Item);
        Assert.Equal(new[] { Instance.FromId(Item) }, removed);
    }

    [Fact]
    public void A_unit_spawned_in_play_is_told_and_its_death_with_its_killer()
    {
        var spawned = new List<Instance>();
        var died = new List<(Instance Unit, Instance Killer)>();
        Units.OnSpawned(_context, spawned.Add);
        Units.OnDied(_context, (unit, killer) => died.Add((unit, killer)));

        Make(Wolf, GameObjectId.o_enemy, "gml_Object_o_enemy_Create_0");
        RunFrame();
        Assert.Equal(new[] { Instance.FromId(Wolf) }, spawned);

        // (Killed by another unit; then by the player, whom the game keeps as their object now and then.)
        _world.Add(Bandit, (int)GameObjectId.o_enemy);
        _world.Add(PlayerId, (int)GameObjectId.o_player);
        _world.Vars[Wolf] = new() { ["last_attacker"] = Bandit };
        RunCode("gml_Object_o_enemy_Other_16", Wolf);
        _world.Vars[Bandit] = new() { ["last_attacker"] = (int)GameObjectId.o_player };
        RunCode("gml_Object_o_enemy_Other_16", Bandit);

        Assert.Equal(new[] { (Instance.FromId(Wolf), Instance.FromId(Bandit)), (Instance.FromId(Bandit), Instance.FromId(PlayerId)) }, died);
    }
}
