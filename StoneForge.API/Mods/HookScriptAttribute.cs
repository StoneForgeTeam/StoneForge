namespace StoneForge;

/// <summary>Declares a GML script this mod hooks with <see cref="ModContext.OnScript"/>:
/// <c>[assembly: HookScript("scr_atr")]</c>, once per script. StoneForge's patcher reads these before starting the
/// game and makes those scripts hookable (in its own copy of the game data, data_stonemod.win).</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HookScriptAttribute : Attribute
{
    public HookScriptAttribute(string scriptName) => ScriptName = scriptName;
    public string ScriptName { get; }
}
