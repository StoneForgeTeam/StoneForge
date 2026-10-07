namespace StoneForge;

/// <summary>The screen held black, as the game's own fades are (its black overlay), with a line of text in the middle
/// if wanted - while something happens behind it (another game's world on its way, a long load). It fades in, and stays
/// until <see cref="Hide"/> or the next room change, whose fade takes it over (the game's overlays fade out as a room
/// starts).
/// <code>
/// Blackout.Show("Waiting for the host...");
/// ...
/// Blackout.Hide();
/// </code></summary>
public static class Blackout
{
    private static Instance _overlay;

    /// <summary>Whether it's up (fading in, or held).</summary>
    public static bool IsShown => !_overlay.IsNone && _overlay.Exists;

    /// <summary>Fades the screen to black and holds it there, with <paramref name="text"/> in the middle (none: as it
    /// was). Already up: just the text.</summary>
    public static void Show(string? text = null)
    {
        if (!IsShown)
            _overlay = Instance.Of(Game.CallScript("scr_guiCreateSimple", default, Game.Global["guiBaseContainerVisible"],
                (int)GameObjectId.o_black_overlay));
        if (text != null)
            Text = text;
    }

    /// <summary>The text in the middle of it ("" for none).</summary>
    public static string Text
    {
        get => IsShown && _overlay.Get("drawText").AsBool ? _overlay.Get("text").AsString : "";
        set
        {
            if (!IsShown || (_overlay.Get("drawText").AsBool && _overlay.Get("text").AsString == value))
                return;
            _overlay["drawText"] = value.Length > 0;
            _overlay["text"] = value;
            // (Held at full: the overlay animates its text only as it fades out.)
            _overlay["textAlpha"] = 1;
        }
    }

    /// <summary>Fades it back out (the overlay goes once it's clear).</summary>
    public static void Hide()
    {
        if (IsShown)
            _overlay["maxAlpha"] = 0;
        _overlay = default;
    }
}
