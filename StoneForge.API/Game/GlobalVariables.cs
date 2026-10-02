namespace StoneForge;

/// <summary>global.name variables.</summary>
public sealed class GlobalVariables
{
    internal GlobalVariables() { }
    public GmValue this[string name]
    {
        get { Game.CheckRunning("global." + name); return Game.GetVar(IntPtr.Zero, name); }
        set { Game.CheckRunning("global." + name); Game.SetVar(IntPtr.Zero, name, value); }
    }
}
