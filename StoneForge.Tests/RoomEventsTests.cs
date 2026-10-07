using StoneForge;

// Going into a room and leaving it, as events (Rooms.OnEntered, OnLeaving): the game's controller's room start and
// end, the entering told on the room's first frame (laid out with FakeGame's room and globals).
public class RoomEventsTests : FakeGame
{
    private const int ControllerId = 100_001;
    private readonly FakeWorld _world = new();
    private readonly ModContext _context = new("room_events_test");

    public RoomEventsTests()
    {
        World = _world;
        _world.LendsIds = true;
        _world.Add(ControllerId, (int)GameObjectId.o_controller);
        Globals["room"] = 7;
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    [Fact]
    public void Leaving_is_told_at_once_and_entering_on_the_rooms_first_frame()
    {
        var entered = new List<int>();
        var left = new List<int>();
        Rooms.OnEntered(_context, entered.Add);
        Rooms.OnLeaving(_context, left.Add);

        RunCode("gml_Object_o_controller_Other_5", ControllerId);
        Assert.Equal(new[] { 7 }, left);

        Globals["room"] = 9;
        RunCode("gml_Object_o_controller_Other_4", ControllerId);
        Assert.Empty(entered);
        RunFrame();
        Assert.Equal(new[] { 9 }, entered);
        // (Once.)
        RunFrame();
        Assert.Single(entered);
    }
}
