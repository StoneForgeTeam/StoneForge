namespace StoneForge.Loader;

/// <summary>"StoneForge 0.1.0 - 1 mod" in the main menu's bottom-left corner (the game's own version is at the
/// top right) - on the loader's UI.MainMenu screen.</summary>
internal sealed class VersionLabel : UILabel
{
    public VersionLabel() : base("", 5, 4, Draw.Muted)
    {
        Anchor = UIAnchor.BottomLeft;
    }

    protected override void OnUpdate(double deltaTime)
    {
        int mods = ModManager.LoadedCount;
        Text = $"StoneForge {LoaderVersion.Text} - {mods} mod{(mods == 1 ? "" : "s")}";
    }
}
