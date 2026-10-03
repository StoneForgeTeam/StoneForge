using StoneForge;

// Off-screen instances: the game's culling controller deactivates them into its list, where GameMaker's own with and
// instance_exists don't see them. Instances.All(includeCulled), IsCulled / IsGone and Destroy find them there, and a
// culled one is destroyed only after it's out of the list (laid out with FakeGame's room model).
public class CullingTests : FakeGame
{
    private const int Loot = 3, Weapon = 30, Other = 40;
    private readonly FakeWorld _world = new();

    public CullingTests()
    {
        World = _world;
        // (A weapon on the ground is loot: a child of o_loot.)
        _world.Parents[Weapon] = Loot;
        _world.Add(1, Loot);
        _world.Add(2, Weapon);
        _world.Add(3, Other);
        _world.Add(4, Loot, culled: true);
        _world.Add(5, Weapon, culled: true);
        _world.Add(6, Other, culled: true);
    }

    private static int[] Ids(IEnumerable<Instance> instances) => instances.Select(i => i.ToString()).Select(s => int.Parse(s.Split(' ')[^1])).OrderBy(i => i).ToArray();

    [Fact]
    public void All_is_the_active_ones_unless_culled_ones_are_asked_for()
    {
        Assert.Equal(new[] { 1, 2 }, Ids(Instances.All(Loot)));
        Assert.Equal(new[] { 1, 2, 4, 5 }, Ids(Instances.All(Loot, includeCulled: true)));
        // (Children of the object, culled or not; never another object's.)
        Assert.Equal(new[] { 2, 5 }, Ids(Instances.All(Weapon, includeCulled: true)));
    }

    [Fact]
    public void A_culled_instance_is_not_gone()
    {
        var culled = Instance.FromId(4);
        Assert.False(culled.Exists);
        Assert.True(culled.IsCulled);
        Assert.False(culled.IsGone);
        var active = Instance.FromId(1);
        Assert.True(active.Exists);
        Assert.False(active.IsCulled);
        Assert.False(active.IsGone);
        var destroyed = Instance.FromId(77);
        Assert.False(destroyed.IsCulled);
        Assert.True(destroyed.IsGone);
    }

    [Fact]
    public void Destroying_a_culled_instance_takes_it_out_of_the_list_first()
    {
        Instance.FromId(5).Destroy();
        Assert.Contains(5, _world.Destroyed);
        Assert.DoesNotContain(5, _world.Culled);
        // (The controller's cached count follows its list, or it would walk past the end.)
        Assert.Equal(_world.Culled.Count, _world.CachedSize);
        Assert.True(Instance.FromId(5).IsGone);
    }

    [Fact]
    public void Destroying_an_active_one_leaves_the_list_alone_and_a_gone_one_does_nothing()
    {
        Instance.FromId(1).Destroy();
        Assert.Equal(new[] { 1 }, _world.Destroyed);
        Assert.Equal(new[] { 4, 5, 6 }, _world.Culled);
        Instance.FromId(77).Destroy();
        Assert.Equal(new[] { 1 }, _world.Destroyed);
    }

    [Fact]
    public void A_culled_instance_has_its_built_ins_but_its_own_variables_cant_be_set()
    {
        Assert.Equal(Loot, Instance.FromId(4).Get("object_index").AsInt);
        Assert.Throws<InvalidOperationException>(() => Instance.FromId(4).Set("mp_seen", 1));
    }

    [Fact]
    public void One_that_cant_be_found_reads_as_undefined_and_is_left_out_never_throwing()
    {
        _world.Unresolvable.Add(4);
        Assert.True(Instance.FromId(4).Get("object_index").IsUndefined);
        Assert.Equal(new[] { 1, 2, 5 }, Ids(Instances.All(Loot, includeCulled: true)));
    }

    [Fact]
    public void A_room_full_of_culled_decorations_is_read_once_not_once_per_instance()
    {
        // (After a room change the list holds everything off screen - decorations by the hundred.)
        for (int id = 100; id < 400; id++)
            _world.Add(id, Other, culled: true);
        Calls.Clear();
        var loot = Instances.All(Loot, includeCulled: true);
        foreach (var item in loot)
            _ = item.IsCulled;
        Assert.Equal(4, loot.Count);
        // (Was a read of the whole list per instance - some 300 x 300 calls.)
        Assert.True(Calls.Count < 1500, $"{Calls.Count} calls into the game");
    }
}
