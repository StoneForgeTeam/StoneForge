using StoneForge;

public sealed class DialogConditionTests : FakeGame
{
    private readonly ModContext _mod = new("condition_test");
    private static int _calls;
    private static DialogConditionResult _state;
    private static DialogOptionContext? _last;
    private static class Valid
    {
        [DialogCondition("state")]
        private static DialogConditionResult Check(DialogOptionContext context) { _calls++; _last = context; return _state; }
        [DialogCondition("simple")]
        public static DialogConditionResult Simple() => DialogConditionResult.Enabled;
    }
    private sealed class InstanceMethod { [DialogCondition("bad")] public DialogConditionResult Bad() => DialogConditionResult.Enabled; }
    private static class WrongReturn { [DialogCondition("bad")] public static bool Bad() => true; }
    private static class WrongArgument { [DialogCondition("bad")] public static DialogConditionResult Bad(string text) => DialogConditionResult.Enabled; }
    private static class Generic { [DialogCondition("bad")] public static DialogConditionResult Bad<T>() => DialogConditionResult.Enabled; }
    private static class Duplicate
    {
        [DialogCondition("same")] public static DialogConditionResult First() => DialogConditionResult.Enabled;
        [DialogCondition("same")] public static DialogConditionResult Second() => DialogConditionResult.Visible;
    }
    [Fact]
    public void Conditions_are_namespaced_use_their_mod_context_and_release_on_unload()
    {
        _calls = 0; _last = null;
        DialogConditions.Register(_mod, new[] { typeof(Valid) });
        Assert.Equal(0, _calls); Assert.Equal(2, DialogConditions.ForMod(_mod.Id).Count);
        foreach (var state in Enum.GetValues<DialogConditionResult>())
        {
            _state = state;
            Assert.Equal(state, DialogConditions.Evaluate(_mod.Id, "condition_test:state", default, default));
        }
        Assert.Equal(3, _calls); Assert.Same(_mod, _last!.Mod); Assert.Equal("condition_test:state", _last.Id);
        Assert.False(DialogConditions.Contains("another_mod", "condition_test:state"));
        Assert.Equal(DialogConditionResult.Visible, DialogConditions.Evaluate("another_mod", "condition_test:state", default, default));
        Hooks.RemoveMod(_mod.Id); Assert.Empty(DialogConditions.ForMod(_mod.Id));
        Assert.Equal(DialogConditionResult.Visible, DialogConditions.Evaluate(_mod.Id, "condition_test:state", default, default));
        DialogConditions.Register(_mod, new[] { typeof(Valid) }); Assert.Equal(2, DialogConditions.ForMod(_mod.Id).Count);
    }
    [Theory]
    [InlineData(typeof(InstanceMethod))]
    [InlineData(typeof(WrongReturn))]
    [InlineData(typeof(WrongArgument))]
    [InlineData(typeof(Generic))]
    [InlineData(typeof(Duplicate))]
    public void Invalid_signatures_and_duplicates_fail_registration_atomically(Type invalid)
    {
        Assert.Throws<ArgumentException>(() => DialogConditions.Register(_mod, new[] { typeof(Valid), invalid }));
        Assert.Empty(DialogConditions.ForMod(_mod.Id));
    }
    private static class Failing
    {
        [DialogCondition("throwing")] public static DialogConditionResult Throwing() => throw new Exception("bad condition");
        [DialogCondition("invalid")] public static DialogConditionResult Invalid() => (DialogConditionResult)100;
    }
    [Fact]
    public void Exceptions_and_invalid_results_leave_the_response_visible_but_disabled()
    {
        DialogConditions.Register(_mod, new[] { typeof(Failing) });
        Assert.Equal(DialogConditionResult.Visible, DialogConditions.Evaluate(_mod.Id, "condition_test:throwing", default, default));
        Assert.Equal(DialogConditionResult.Visible, DialogConditions.Evaluate(_mod.Id, "condition_test:invalid", default, default));
    }
    public override void Dispose() { Hooks.RemoveMod(_mod.Id); base.Dispose(); }
}
