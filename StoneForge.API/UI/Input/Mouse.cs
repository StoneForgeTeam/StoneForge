namespace StoneForge;

/// <summary>The mouse, in GUI coordinates (as <see cref="Draw"/>).</summary>
public static class Mouse
{
    public const int Left = 1, Right = 2, Middle = 3;
    public static double X => Game.CallBuiltin("device_mouse_x_to_gui", 0).AsReal / Draw.Scale;
    public static double Y => Game.CallBuiltin("device_mouse_y_to_gui", 0).AsReal / Draw.Scale;
    /// <summary>Pressed this frame.</summary>
    public static bool Pressed(int button = Left) => Game.CallBuiltin("mouse_check_button_pressed", button).AsBool;
    /// <summary>Released this frame.</summary>
    public static bool Released(int button = Left) => Game.CallBuiltin("mouse_check_button_released", button).AsBool;
    /// <summary>Held down.</summary>
    public static bool Down(int button = Left) => Game.CallBuiltin("mouse_check_button", button).AsBool;
    /// <summary>Whether the mouse is over the rectangle.</summary>
    public static bool Over(double x, double y, double width, double height) { double mx = X, my = Y; return mx >= x && mx < x + width && my >= y && my < y + height; }
    /// <summary>The wheel this frame: 1 up, -1 down, 0 none.</summary>
    public static int Wheel => Game.CallBuiltin("mouse_wheel_up").AsBool ? 1 : Game.CallBuiltin("mouse_wheel_down").AsBool ? -1 : 0;
}
