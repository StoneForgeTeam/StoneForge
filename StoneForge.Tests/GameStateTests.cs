using StoneForge;

// Game.IsBusy and Game.IsCutscene (laid out with FakeGame's scene: the objects present, and scr_is_cutscene's answer -
// which fails unless it runs as an instance).
public class GameStateTests : FakeGame
{
    private readonly FakeScene _scene = new();
    // (The player's handed over by pointer, as inside a game callback.)
    private readonly CallbackLifetime _lease = new();

    public GameStateTests()
    {
        Scene = _scene;
        _scene.Present.Add(GameObjectId.o_player);
    }

    public override void Dispose()
    {
        _lease.Dispose();
        base.Dispose();
    }

    [Fact]
    public void A_quiet_game_isnt_busy()
    {
        Assert.False(Game.IsBusy);
        Assert.False(Game.IsCutscene);
    }

    [Theory]
    [InlineData(GameObjectId.o_smoothRoomChanger)]
    [InlineData(GameObjectId.o_black_overlay)]
    [InlineData(GameObjectId.o_dialogue)]
    public void A_room_change_fade_or_dialogue_is_busy(GameObjectId obj)
    {
        _scene.Present.Add(obj);
        Assert.True(Game.IsBusy);
    }

    [Fact]
    public void A_cutscene_is_busy_and_is_checked_as_the_player()
    {
        _scene.Cutscene = true;
        Assert.True(Game.IsCutscene);
        Assert.True(Game.IsBusy);
        Assert.Equal((IntPtr)FakeScene.PlayerPointer, _scene.CheckedAs);
    }

    [Fact]
    public void With_no_player_theres_no_cutscene_and_no_check()
    {
        _scene.Present.Clear();
        _scene.Cutscene = true;
        Assert.False(Game.IsCutscene);
        Assert.False(Game.IsBusy);
        Assert.Equal(0, _scene.CutsceneChecks);
        // (A room change on the main menu is busy all the same.)
        _scene.Present.Add(GameObjectId.o_smoothRoomChanger);
        Assert.True(Game.IsBusy);
    }
}
