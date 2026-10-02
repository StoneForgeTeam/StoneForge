namespace StoneForge;

/// <summary>A C# mod: a public class with a parameterless constructor implementing this, in C# source in its
/// own folder (mods\&lt;mod&gt;\*.cs) beside its mod.json (its id, name, version... - see <see cref="ModManifest"/>), its
/// pictures - and an optional icon.png - in the folder's Assets\. One mod per folder. The loader compiles and checks
/// the source when the game starts - only what's safe for a mod is allowed (see ModSecurity). <see cref="Load"/>
/// runs once, when the game has started. For something every frame, the mod (or anything of its) implements
/// <see cref="ITickable"/>.</summary>
public interface IStoneMod
{
    void Load(ModContext context);

    /// <summary>The mod is being switched off while the game runs (the Mods window): undo here anything it
    /// changed in the game that it didn't register through its <see cref="ModContext"/> (globals it set,
    /// instances it made, files...) - leave it empty if there's nothing. What it registered through its context -
    /// events, hooks, its ITickables, DrawGui, its UI and windows, main menu buttons, its items' behaviour - is
    /// taken back for it after this, and its settings saved; its items are removed from the game (a save from
    /// before still has them for when it's back on). Not called when the game quits.</summary>
    void Unload();
}
