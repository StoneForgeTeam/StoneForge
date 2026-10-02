namespace StoneForge;

/// <summary>One of the game's GML scripts (generated as <c>Scripts.scr_atr</c>...). Hooking it needs the
/// mod to declare it: <c>[assembly: HookScript(nameof(Scripts.scr_atr))]</c>.</summary>
public sealed class Script
{
    /// <summary>Its name in the game.</summary>
    public string Name { get; }
    /// <summary>How many arguments it takes (as declared in the game).</summary>
    public int ArgumentCount { get; }

    public Script(string name, int argumentCount)
    {
        Name = name;
        ArgumentCount = argumentCount;
    }

    /// <summary>Runs when the game calls it, before its code. Set <see cref="ScriptCall.Result"/> and return
    /// true to replace the call.</summary>
    public void Before(ModContext context, Func<ScriptCall, bool> handler) => context.OnScript(Name, handler);

    /// <summary>Replaces the call with your own: whatever <paramref name="replacement"/> returns is what the
    /// game gets. <see cref="CallOriginal(ScriptCall)"/> inside it runs the game's own version (to adjust its result).</summary>
    public void Replace(ModContext context, Func<ScriptCall, GmValue> replacement)
        => context.OnScript(Name, call =>
        {
            call.Result = replacement(call);
            return true;
        });

    /// <summary>Calls it (its hooks run, as for any call).</summary>
    public GmValue Call(GameInstance? self = null, params GmValue[] args) => Game.CallScript(Name, self?.Instance ?? default, args);

    /// <summary>Calls the game's own version, skipping every mod's hooks on it - for use inside a hook.</summary>
    public GmValue CallOriginal(GameInstance? self = null, params GmValue[] args) => CallOriginal(self?.Instance ?? default, args);

    /// <summary>As <see cref="CallOriginal(GameInstance?, GmValue[])"/>, with the call's own self and arguments.</summary>
    public GmValue CallOriginal(ScriptCall call) => CallOriginal(call.Self, call.Args);

    private GmValue CallOriginal(Instance self, GmValue[] args)
    {
        // (The hook block in the script checks global.__smh_<name>: off for the call, back on after.)
        string flag = "__smh_" + Name;
        bool hooked = Game.Global[flag].AsBool;
        Game.Global[flag] = false;
        try { return Game.CallScript(Name, self, args); }
        finally { if (hooked) Game.Global[flag] = true; }
    }

    public override string ToString() => Name;
}
