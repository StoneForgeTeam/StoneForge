namespace StoneForge;

/// <summary>The mouse: where it is on the screen (GUI coordinates, as <see cref="Draw"/>) and in the world (the room's
/// coordinates, its cell and the unit on it), its buttons and wheel, and what it's over - the game's UI, a mod's - so a
/// mod can tell a click on the world from one on a window:
/// <code>
/// if (Mouse.ClickedWorld())
///     Game.Log($"Clicked cell {Mouse.Cell}, {(Mouse.Unit.IsNone ? "empty" : "a unit")}");
/// </code></summary>
public static class Mouse
{
    public const int Left = 1, Right = 2, Middle = 3;

    // ---- on the screen ----

    /// <summary>Where it is on the screen, in GUI coordinates (as <see cref="Draw"/> and mods' UI).</summary>
    public static double X => Game.CallBuiltin("device_mouse_x_to_gui", 0).AsReal / Draw.Scale;
    public static double Y => Game.CallBuiltin("device_mouse_y_to_gui", 0).AsReal / Draw.Scale;
    /// <summary>Both at once: where it is on the screen, in GUI coordinates.</summary>
    public static Point Position => new(X, Y);
    /// <summary>Pressed this frame.</summary>
    public static bool Pressed(int button = Left) => Game.CallBuiltin("mouse_check_button_pressed", button).AsBool;
    /// <summary>Released this frame.</summary>
    public static bool Released(int button = Left) => Game.CallBuiltin("mouse_check_button_released", button).AsBool;
    /// <summary>Held down.</summary>
    public static bool Down(int button = Left) => Game.CallBuiltin("mouse_check_button", button).AsBool;
    /// <summary>Whether the mouse is over the rectangle (GUI coordinates).</summary>
    public static bool Over(double x, double y, double width, double height) { double mx = X, my = Y; return mx >= x && mx < x + width && my >= y && my < y + height; }
    /// <summary>The wheel this frame: 1 up, -1 down, 0 none.</summary>
    public static int Wheel => Game.CallBuiltin("mouse_wheel_up").AsBool ? 1 : Game.CallBuiltin("mouse_wheel_down").AsBool ? -1 : 0;

    // ---- in the world ----

    /// <summary>Where it is in the world: the room's coordinates (the game's mouse_x / mouse_y - its camera taken into
    /// account), as units' x / y are.</summary>
    public static double WorldX => Game.Global["mouse_x"].AsReal;
    public static double WorldY => Game.Global["mouse_y"].AsReal;
    /// <summary>Both at once: where it is in the world, in the room's coordinates.</summary>
    public static Point World => new(WorldX, WorldY);

    /// <summary>The world cell it's over (the game's 26-pixel grid, as units stand on it: x div 26, y div 26).</summary>
    public static Cell Cell => StoneForge.Cell.At(WorldX, WorldY);

    /// <summary>The unit standing on the cell it's over - an enemy, an NPC, the player, another mod's unit - as the game
    /// finds who stands where (its position grid: targeting, the cursor); none for an empty cell, off the room, or with
    /// no game.</summary>
    public static Instance Unit => Units.At(Cell);

    // ---- what it's over ----

    /// <summary>Whether the game's window has the focus (a click while it hasn't went to another window first).</summary>
    public static bool HasFocus => Game.CallBuiltinTrusted("window_has_focus", default, default).AsBool;

    /// <summary>Whether it's over the game's own UI - a window, the bottom panel, a button: any of its shown GUI
    /// elements, as the game finds what's clicked (its c_GUI under the mouse).</summary>
    public static bool OverGameUI => GameUIUnder(nearerThan: null);

    /// <summary>Whether it's over a mod's UI (StoneForge's - any mod's screen or window), as of the last frame drawn.</summary>
    public static bool OverModUI => InputBlock.MouseOnModUI;

    /// <summary>Whether it's over any UI, the game's or a mod's: a click there isn't on the world.</summary>
    public static bool OverUI => OverModUI || OverGameUI;

    /// <summary>Whether <paramref name="button"/> was pressed this frame on the world: in the game's window while it has
    /// the focus, and not on any UI (the game's or a mod's) - a click meant for the world, as the game takes one to move
    /// or attack.</summary>
    public static bool ClickedWorld(int button = Left) => Pressed(button) && HasFocus && !OverUI;

    private static int _cGui = -2, _blocker = -2;

    // (Tests: the game's objects looked up again.)
    internal static void ResetForTests()
    {
        _cGui = _blocker = -2;
        Units.ResetForTests();
    }

    // Whether any of the game's shown GUI elements is under the mouse (c_GUI, in the game's own GUI space:
    // global.guiMouseX / Y) - all of them, or only those drawn nearer than a depth. Not StoneForge's input blocker, which
    // sits over mods' UI for the game's sake.
    internal static bool GameUIUnder(double? nearerThan)
    {
        if (_cGui == -2)
        {
            _cGui = Gm.AssetGetIndex("c_GUI");
            _blocker = Gm.AssetGetIndex("o_stonemod_blocker");
        }
        if (_cGui < 0)
            return false;
        GmValue list = Game.CallBuiltin("ds_list_create");
        try
        {
            int count = Game.CallBuiltin("instance_position_list", Game.Global["guiMouseX"], Game.Global["guiMouseY"], _cGui, list, false).AsInt;
            for (int i = 0; i < count; i++)
            {
                GmValue gui = Game.CallBuiltin("ds_list_find_value", list, i);
                if (Game.CallBuiltin("variable_instance_get", gui, "object_index").AsInt == _blocker
                    || !Game.CallBuiltin("variable_instance_get", gui, "visible").AsBool)
                    continue;
                if (nearerThan is not { } depth || Game.CallBuiltin("variable_instance_get", gui, "depth").AsReal < depth)
                    return true;
            }
            return false;
        }
        finally { Game.CallBuiltin("ds_list_destroy", list); }
    }
}
