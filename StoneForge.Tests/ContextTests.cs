using StoneForge;

// context.Items / Buffs / Skills: registration keeps its owning context and namespaces content by the mod's ID, so
// two mods can use the same key; within a mod a key is taken once, through either API, until the mod unloads.
public class ContextTests : IDisposable
{
    private readonly bool _running = Game.Running;
    private readonly ModContext _first = new("context_test_a");
    private readonly ModContext _second = new("context_test_b");

    public ContextTests() => Game.Running = false;

    public void Dispose()
    {
        Clear(_first.Id);
        Clear(_second.Id);
        Game.Running = _running;
    }

    [Fact]
    public void Content_is_owned_by_its_context_and_named_by_its_mod()
    {
        var item = new Blade();
        var food = new Food();
        var buff = new Focus();
        var skill = new Bolt();
        _first.Items.Add(item);
        _first.Items.Add(food);
        _first.Buffs.Add(buff);
        _first.Skills.Add(skill);
        Assert.All(new[] { item.Owner, food.Owner, buff.Owner, skill.Owner }, owner => Assert.Same(_first, owner));
        Assert.Equal("context_test_a:sf_blade", item.Id);
        Assert.Equal("context_test_a:sf_food", food.Id);
        Assert.Equal("context_test_a:sf_focus", buff.Id);
        Assert.Equal("context_test_a:sf_bolt", skill.Id);
        Assert.Equal("context_test_a__sf_food", food.GameKey);
        Assert.Equal("context_test_a__sf_bolt", skill.GameKey);
    }

    [Fact]
    public void Two_mods_can_use_the_same_keys()
    {
        _first.Items.Add(new Blade());
        _first.Items.Add(new Food());
        _first.Buffs.Add(new Focus());
        _first.Skills.Add(new Bolt());
        var blade = new Blade();
        _second.Items.Add(blade);
        _second.Items.Add(new Food());
        _second.Buffs.Add(new Focus());
        _second.Skills.Add(new Bolt());
        Assert.Equal("context_test_b:sf_blade", blade.Id);
    }

    [Fact]
    public void A_mod_takes_a_key_once_through_either_API_until_it_unloads()
    {
        var item = new Blade();
        var food = new Food();
        var buff = new Focus();
        var skill = new Bolt();
        _first.Items.Add(item);
        _first.Items.Add(food);
        _first.Buffs.Add(buff);
        _first.Skills.Add(skill);
        // Both entry points share duplicate checks and the same unload path.
        Assert.Throws<ArgumentException>(() => Items.Add(_first, new Blade()));
        Assert.Throws<ArgumentException>(() => Items.Add(_first, new Food()));
        Assert.Throws<ArgumentException>(() => Buffs.Add(_first, new Focus()));
        Assert.Throws<ArgumentException>(() => Skills.Add(_first, new Bolt()));
        Assert.Throws<ArgumentException>(() => _first.Items.Add(new Blade()));
        Clear(_first.Id);
        Items.Add(_first, item);
        Items.Add(_first, food);
        Buffs.Add(_first, buff);
        Skills.Add(_first, skill);
        Assert.All(new[] { item.Owner, food.Owner, buff.Owner, skill.Owner }, owner => Assert.Same(_first, owner));
    }

    private static void Clear(string id)
    {
        Items.RemoveMod(id); Consumables.RemoveMod(id); Buffs.RemoveMod(id); Skills.RemoveMod(id);
        Hooks.RemoveMod(id);
    }

    private sealed class Blade : Weapon { public Blade() : base("sf_blade", "sword") {} public ModContext Owner => Context; }
    private sealed class Food : Consumable { public Food() : base("sf_food", "wine") {} public ModContext Owner => Context; }
    private sealed class Focus : ModBuff { public Focus() : base("sf_focus") {} public ModContext Owner => Context; }
    private sealed class Bolt : ModSkill { public Bolt() : base("sf_bolt", "chain_lightning") {} public ModContext Owner => Context; }
}
