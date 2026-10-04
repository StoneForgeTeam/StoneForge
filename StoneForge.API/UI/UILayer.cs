namespace StoneForge;

/// <summary>Where a UI screen is drawn (<see cref="ModUI.When(Func{bool}, UILayer)"/>).</summary>
public enum UILayer
{
    /// <summary>Over everything: the game's world, its HUD and its windows (<see cref="ModContext.DrawGui"/>).</summary>
    Gui,
    /// <summary>With the game's HUD (<see cref="ModContext.DrawHud"/>): over the world, under the game's windows and
    /// its bottom panel. Its elements have the mouse only where none of the game's GUI drawn over them is under it - a
    /// click on a game window over them is the window's. (Mod windows - <see cref="UIWindow"/> - still open over
    /// everything.)</summary>
    Hud,
}
