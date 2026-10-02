namespace StoneForge.Loader;

/// <summary>The main menu's Mods button (a <see cref="MainMenu"/> button), which opens the Mods window
/// (<see cref="ModsWindow"/>, on the loader's main menu screen).</summary>
internal static class ModsMenu
{
    internal static void Install(ModContext context)
    {
        var window = context.UI.MainMenu.Add(new ModsWindow());
        MainMenu.AddButton(context, "Mods", window.Open);
    }
}
