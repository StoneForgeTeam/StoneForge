namespace StoneForge.Tests;

// Alarms (Instance.Alarm): only GameMaker's twelve are asked for - the engine's accessor isn't given an index it
// doesn't have. Reading and setting them goes through the game, so that's tested in game.
public class AlarmTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(12)]
    public void Only_alarms_0_to_11_exist(int index)
    {
        // (Straight off the instance, as mods write it: player.Alarm[2] = -1.)
        var instance = Instance.FromId(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => instance.Alarm[index]);
        Assert.Throws<ArgumentOutOfRangeException>(() => instance.Alarm[index] = 1);
    }
}
