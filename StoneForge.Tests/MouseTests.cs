using StoneForge;

// The mouse in the world (Mouse): its room position and cell, the unit on that cell, and telling a click on the world
// from one on the game's UI or a mod's.
public class MouseTests : FakeGame
{
    public MouseTests()
    {
        Mouse.ResetForTests();
        Globals["guiMouseX"] = 0;
        Globals["guiMouseY"] = 0;
    }

    [Fact]
    public void World_position_and_its_cell()
    {
        Globals["mouse_x"] = 60.5;
        Globals["mouse_y"] = 27;
        Assert.Equal(60.5, Mouse.WorldX);
        Assert.Equal((2, 1), Mouse.Cell);
        Globals["mouse_x"] = -1;
        Assert.Equal(-1, Mouse.Cell.X);
    }

    [Fact]
    public void The_unit_on_the_cell_comes_from_the_position_grid()
    {
        Input = new FakeInput { Positions = new int[5, 5] };
        for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
                Input.Positions[x, y] = -4;
        Input.Positions[2, 1] = 4242;
        Input.Units.Add(4242);
        Globals["mouse_x"] = 60;
        Globals["mouse_y"] = 30;
        Assert.Equal(Instance.FromId(4242), Mouse.Unit);
        // (An empty cell, off the grid, and before the room's left edge: none.)
        Globals["mouse_x"] = 90;
        Assert.True(Mouse.Unit.IsNone);
        Globals["mouse_x"] = 500;
        Assert.True(Mouse.Unit.IsNone);
        Globals["mouse_x"] = -10;
        Assert.True(Mouse.Unit.IsNone);
        // (No game: no controller.)
        Input.Positions = null;
        Globals["mouse_x"] = 60;
        Assert.True(Mouse.Unit.IsNone);
    }

    [Fact]
    public void A_click_on_the_world_needs_the_focus_and_no_UI_under_it()
    {
        Input = new FakeInput { Pressed = true };
        Assert.True(Mouse.ClickedWorld());
        // (Our own input blocker, and a hidden element of the game's, aren't UI under it.)
        Input.GuiUnderMouse.Add((9001, FakeInput.BlockerObject, true, -20000));
        Input.GuiUnderMouse.Add((9002, 1, false, -12100));
        Assert.False(Mouse.OverGameUI);
        Assert.True(Mouse.ClickedWorld());
        Input.GuiUnderMouse.Add((9003, 1, true, -12100));
        Assert.True(Mouse.OverGameUI);
        Assert.False(Mouse.ClickedWorld());
        Input.GuiUnderMouse.Clear();
        Input.Focused = false;
        Assert.False(Mouse.ClickedWorld());
        Input.Focused = true;
        Input.Pressed = false;
        Assert.False(Mouse.ClickedWorld());
    }

    [Fact]
    public void The_HUD_only_counts_the_game_UI_drawn_over_it()
    {
        Input = new FakeInput();
        Input.GuiUnderMouse.Add((9004, 1, true, -12000));
        Assert.True(Mouse.OverGameUI);
        Assert.False(Mouse.GameUIUnder(nearerThan: -12050));
        Input.GuiUnderMouse.Add((9005, 1, true, -12100));
        Assert.True(Mouse.GameUIUnder(nearerThan: -12050));
    }
}
