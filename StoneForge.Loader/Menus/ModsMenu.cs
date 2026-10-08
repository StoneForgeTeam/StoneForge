namespace StoneForge.Loader;

/// <summary>The main menu's Mods button (a <see cref="MainMenu"/> button), which opens the Mods window
/// (<see cref="ModsWindow"/>, on the loader's main menu screen).</summary>
internal static class ModsMenu
{
    internal static void Install(ModContext context)
    {
        var window = context.UI.MainMenu.Add(new ModsWindow());
        string title = Localization.Get("mods.title");
        MainMenu.AddButton(context, title, window.Open);
        int revision = Localization.Revision;
        context.Frame += () =>
        {
            if (revision == Localization.Revision) return;
            revision = Localization.Revision;
            string translated = Localization.Get("mods.title");
            MainMenu.RefreshButtonText(context.Id, title, translated);
            title = translated;
        };
    }
}
