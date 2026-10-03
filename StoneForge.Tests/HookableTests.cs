using StoneForge;

// A mod's script hook on a script the game data doesn't make hookable is refused at once (it would never be called),
// saying how to declare it; StoneForge's own, and any script with no list to go by, aren't checked.
[CollectionDefinition(nameof(HookableTests), DisableParallelization = true)]
public class HookableTestsCollection { }

// (Hooks.Hookable is shared: run alone, so no other test's hooks see this one's list.)
[Collection(nameof(HookableTests))]
public class HookableTests : IDisposable
{
    public HookableTests() => Hooks.Hookable = new HashSet<string> { "scr_atr" };

    public void Dispose()
    {
        Hooks.Hookable = null;
        Hooks.RemoveMod("testmod");
        Hooks.RemoveMod(Hooks.LoaderId);
    }

    [Fact]
    public void A_hookable_script_can_be_hooked()
        => Hooks.AddScript("testmod", "scr_atr", _ => false);

    [Fact]
    public void One_that_isnt_says_how_to_declare_it()
    {
        var e = Assert.Throws<ArgumentException>(() => Hooks.AddScript("testmod", "scr_everyHourQuestTriggers", _ => false));
        Assert.Contains("[assembly: HookScript(nameof(Scripts.scr_everyHourQuestTriggers))]", e.Message);
    }

    [Fact]
    public void StoneForges_own_and_unknown_lists_arent_checked()
    {
        Hooks.AddScript(Hooks.LoaderId, "scr_stonemod_draw_gui", _ => false);
        Hooks.Hookable = null;
        Hooks.AddScript("testmod", "scr_anything", _ => false);
    }
}
