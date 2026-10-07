using StoneForge;

// UI screens on the HUD layer (ModUI.Hud, ModContext.DrawHud): drawn in the HUD pass, under the game's windows -
// not in the Draw GUI pass - and gone with their mod.
public class HudLayerTests : FakeGame
{
    [Theory]
    [InlineData(2, 0, 0, 0, 0)]
    [InlineData(2, 8, 12, 40, 24)]
    [InlineData(1.5, 16, 4, 60, 30)]
    public void Hud_origin_aligns_drawn_elements_with_full_window_hitboxes(
        double unit, double frameLeft, double frameTop, double windowX, double windowY)
    {
        Point origin = Draw.HudScreenOrigin(unit, frameLeft, frameTop, windowX, windowY);
        // The game's mouse mapping at a point 100,64 UI units into the window.
        double gameMouseX = -5000 - frameLeft - windowX / unit + 100;
        double gameMouseY = -5000 - frameTop - windowY / unit + 64;
        Assert.Equal(gameMouseX, origin.X + 100);
        Assert.Equal(gameMouseY, origin.Y + 64);
    }

    [Fact]
    public void Hud_screens_draw_in_the_HUD_pass_and_others_over_everything()
    {
        var context = new ModContext("hud_layer_test");
        try
        {
            UIScreen hud = context.UI.Hud;
            UIScreen inGame = context.UI.InGame;
            Assert.Equal(UILayer.Hud, hud.Layer);
            Assert.Equal(UILayer.Gui, inGame.Layer);
            Assert.Same(hud, context.UI.Hud);
            Assert.Single(Hooks.DrawHudHandlers, h => h.Mod == context.Id);
            Assert.Single(Hooks.DrawGuiHandlers, h => h.Mod == context.Id);
            UIScreen own = context.UI.When(() => true, UILayer.Hud);
            Assert.Equal(UILayer.Hud, own.Layer);
            Assert.Equal(2, Hooks.DrawHudHandlers.Count(h => h.Mod == context.Id));
        }
        finally { Hooks.RemoveMod(context.Id); }
        Assert.DoesNotContain(Hooks.DrawHudHandlers, h => h.Mod == context.Id);
        Assert.DoesNotContain(Hooks.DrawGuiHandlers, h => h.Mod == context.Id);
    }

    [Fact]
    public void DrawHud_handlers_come_and_go()
    {
        var context = new ModContext("hud_handler_test");
        Action handler = () => { };
        context.DrawHud += handler;
        Assert.Contains(Hooks.DrawHudHandlers, h => h.Handler == handler);
        context.DrawHud -= handler;
        Assert.DoesNotContain(Hooks.DrawHudHandlers, h => h.Handler == handler);
    }
}
