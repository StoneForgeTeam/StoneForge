using StoneForge;

public sealed class DialogOptionTests : FakeGame
{
    private readonly ModContext _mod = new("action_test");
    private static int _calls;
    private static DialogOptionContext? _last;
    [Fact]
    public void Built_ins_are_shared_and_survive_mod_unload_without_exposing_other_mod_actions()
    {
        Assert.Equal(12, DialogOptions.WithBuiltIns(_mod.Id).Count(e => e.BuiltIn));
        Assert.True(DialogOptions.Contains(_mod.Id, "stoneforge:exit_dialogue"));
        Assert.True(DialogOptions.Contains("another_mod", "stoneforge:exit_dialogue"));
        Assert.False(DialogOptions.Contains(_mod.Id, "stoneforge:arbitrary_script"));
        Assert.False(DialogOptions.Invoke(_mod.Id, "stoneforge:exit_dialogue", default, default));
        DialogOptions.RemoveMod(_mod.Id); Assert.True(DialogOptions.Contains(_mod.Id, "stoneforge:exit_dialogue"));
    }
    private static class Valid
    {
        [DialogOption("simple")]
        private static void Simple() => _calls++;
        [DialogOption("context")]
        public static void Context(DialogOptionContext context) => _last = context;
    }
    private sealed class InvalidInstance { [DialogOption("bad")] public void Bad() { } }
    private static class InvalidReturn { [DialogOption("bad")] public static int Bad() => 1; }
    private static class InvalidParameter { [DialogOption("bad")] public static void Bad(string value) { } }
    private static class Duplicate
    {
        [DialogOption("same")] public static void First() { }
        [DialogOption("same")] public static void Second() { }
    }
    public override void Dispose() { Hooks.RemoveMod(_mod.Id); base.Dispose(); }
    [Fact]
    public void Registration_namespaces_actions_without_running_them_and_unload_allows_rebinding()
    {
        _calls = 0; _last = null;
        DialogOptions.Register(_mod, new[] { typeof(Valid) });
        Assert.Equal(0, _calls);
        Assert.Equal("action_test:simple", DialogOptions.ForMod(_mod.Id).Single(e => e.Id == "action_test:simple").Label);
        Assert.False(DialogOptions.Invoke("another_mod", "action_test:simple", default, default));
        Assert.True(DialogOptions.Invoke(_mod.Id, "action_test:simple", default, default));
        Assert.Equal(1, _calls);
        Assert.True(DialogOptions.Invoke(_mod.Id, "action_test:context", default, default));
        Assert.Same(_mod, _last!.Mod); Assert.Equal("action_test:context", _last.Id);
        Hooks.RemoveMod(_mod.Id);
        Assert.Empty(DialogOptions.ForMod(_mod.Id));
        Assert.False(DialogOptions.Invoke(_mod.Id, "action_test:simple", default, default));
        DialogOptions.Register(_mod, new[] { typeof(Valid) });
        Assert.Equal(2, DialogOptions.ForMod(_mod.Id).Count);
    }
    [Theory]
    [InlineData(typeof(InvalidInstance))]
    [InlineData(typeof(InvalidReturn))]
    [InlineData(typeof(InvalidParameter))]
    [InlineData(typeof(Duplicate))]
    public void Invalid_methods_fail_registration_atomically(Type invalid)
    {
        Assert.Throws<ArgumentException>(() => DialogOptions.Register(_mod, new[] { typeof(Valid), invalid }));
        Assert.Empty(DialogOptions.ForMod(_mod.Id));
    }
}
