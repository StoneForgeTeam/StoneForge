using StoneForge;

// UI screens on the HUD layer (ModUI.Hud, ModContext.DrawHud): drawn in the HUD pass, under the game's windows -
// not in the Draw GUI pass - and gone with their mod.
public class HudLayerTests : FakeGame
{
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
