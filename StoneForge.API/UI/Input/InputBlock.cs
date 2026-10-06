namespace StoneForge;

// Keeps the game's own input off mods' UI:
//  - the mouse: an invisible o_stonemod_blocker (a c_GUI child, which the patcher adds) is put over the mod UI
//    the mouse is on. The game's GUI only answers the nearest c_GUI under the mouse (can_press_gui), and its
//    world - moving, attacking, highlighting - ignores clicks with any c_GUI under it (position_meeting with
//    c_GUI), so neither sees a click on mod UI;
//  - the keyboard: while a mod's text box has the focus, global.stonemod_typing is set, and the game's hotkey
//    checks (scr_check_keyboard_*_array - the patcher guards them) see no keys.
// Each UIScreen reports what it covers during the Draw GUI pass; Flush, after them, moves the blocker.
internal static class InputBlock
{
    // What mod UI covers under the mouse this frame (GUI coordinates), from every screen.
    private static readonly List<(double X1, double Y1, double X2, double Y2)> Covered = new();
    private static int _blocker = -1;
    private static bool _blocking, _typing;

    /// <summary>Whether the mouse was on any mod's UI in the last Draw GUI pass (Mouse.OverModUI).</summary>
    internal static bool MouseOnModUI { get; private set; }

    internal static void Report(UIElement root) => Covered.Add((root.ScreenX, root.ScreenY, root.ScreenX + root.Width, root.ScreenY + root.Height));

    // After every screen has drawn: the blocker over the covered area the mouse is in (or away), and the
    // typing flag.
    internal static void Flush()
    {
        try
        {
            // (A text box typed in, or a window open: the game's hotkeys held off.)
            bool typing = UITextBox.AnyFocused || UIWindow.AnyOpen;
            if (typing != _typing)
            {
                _typing = typing;
                // (The native build's guard is the bridge's; the VM build's reads the global.)
                if (Game.IsNative)
                    Game.SetTyping(typing);
                else
                    Game.Global["stonemod_typing"] = typing;
            }
            if (Covered.Count == 0)
            {
                MouseOnModUI = false;
                if (_blocking)
                    MoveAway();
                return;
            }
            double mx = Mouse.X, my = Mouse.Y;
            var inside = Covered.Where(c => mx >= c.X1 && mx < c.X2 && my >= c.Y1 && my < c.Y2).ToList();
            MouseOnModUI = inside.Count > 0;
            if (inside.Count > 0)
                Place(inside[0], mx, my);
            else if (_blocking)
                MoveAway();
        }
        catch (Exception e) { Game.Log("input block: " + e.Message); }
        finally { Covered.Clear(); }
    }

    // The game's GUI space: the window's pixels over window_ratio * cameraScale, shifted (x and y from
    // -5000). Ours maps the window to display_get_gui_width/height. Anchored at the mouse - where both know
    // it is - so the shifts cancel out.
    private static void Place((double X1, double Y1, double X2, double Y2) area, double mx, double my)
    {
        if (Blocker() is not Instance blocker)
            return;
        double gameX = Game.Global["guiMouseX"].AsReal, gameY = Game.Global["guiMouseY"].AsReal;
        double unit = Game.Global["window_ratio"].AsReal * Game.Global["cameraScale"].AsReal;
        double guiWidth = Draw.Width, guiHeight = Draw.Height;
        if (unit <= 0 || guiWidth <= 0 || guiHeight <= 0)
            return;
        double kx = Game.CallBuiltinTrusted("window_get_width", default, default).AsReal / guiWidth / unit;
        double ky = Game.CallBuiltinTrusted("window_get_height", default, default).AsReal / guiHeight / unit;
        double x1 = gameX + (area.X1 - mx) * kx, y1 = gameY + (area.Y1 - my) * ky;
        blocker.Set("x", x1);
        blocker.Set("y", y1);
        blocker.Set("image_xscale", Math.Max(1, (area.X2 - area.X1) * kx));
        blocker.Set("image_yscale", Math.Max(1, (area.Y2 - area.Y1) * ky));
        _blocking = true;
    }

    private static void MoveAway()
    {
        if (Blocker() is Instance blocker)
        {
            blocker.Set("x", -100000);
            blocker.Set("y", -100000);
        }
        _blocking = false;
    }

    // The blocker: made the first time it's needed (persistent), made again should anything remove it.
    private static Instance? Blocker()
    {
        if (_blocker >= 0 && Game.CallBuiltinTrusted("instance_exists", default, default, _blocker).AsBool)
            return Instance.FromId(_blocker);
        int obj = Game.CallBuiltinTrusted("asset_get_index", default, default, "o_stonemod_blocker").AsInt;
        if (obj < 0)
            return null;
        // (Nearer than any of the game's GUI: it must win can_press_gui. Never drawn, so its depth is free.)
        Instance made = Game.CallBuiltinTrusted("instance_create_depth", default, default, -100000, -100000, -20000, obj);
        _blocker = made.Id;
        return made.IsNone ? null : made;
    }
}
