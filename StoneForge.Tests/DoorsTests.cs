using StoneForge;

// The room's doors (Doors): found, read open or shut through the game's scr_door_is_closed, and opened or closed by
// their own user event 3 - a locked one unlocked when opened, unless asked not to; and their opening or closing, as an
// event (laid out with FakeGame's room and scripts).
public class DoorsTests : FakeGame
{
    private const int DoorObject = 400, HouseDoor = 401, DoorId = 100_100;
    private readonly FakeScripts _scripts = new();
    private readonly FakeWorld _world = new();
    private bool _closed = true;

    public DoorsTests()
    {
        Doors.ResetForTests();
        GameScripts = _scripts;
        World = _world;
        _world.ExistsByObject = true;
        _world.Parents[HouseDoor] = DoorObject;
        _world.Assets["o_door_parent"] = DoorObject;
        _world.Add(DoorId, HouseDoor);
        _world.Vars[DoorId] = new() { ["object_index"] = HouseDoor, ["is_lock"] = false };
        _scripts.Add("scr_door_is_closed", _ => _closed);
    }

    private Instance Door => Instance.FromId(DoorId);
    private readonly ModContext _context = new("doors_test");

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }


    [Fact]
    public void A_door_is_found_and_read()
    {
        Assert.Equal(new[] { Door }, Doors.All());
        Assert.True(Doors.IsDoor(Door));
        Assert.False(Doors.IsOpen(Door));
        _closed = false;
        Assert.True(Doors.IsOpen(Door));
    }

    [Fact]
    public void Opening_and_closing_go_through_its_own_event()
    {
        Doors.SetOpen(Door, true);
        Assert.Equal((DoorId, 3), Assert.Single(_world.UserEvents));
        // (Already open: nothing.)
        _closed = false;
        Doors.SetOpen(Door, true);
        Assert.Single(_world.UserEvents);
        Doors.SetOpen(Door, false);
        Assert.Equal(2, _world.UserEvents.Count);
    }

    [Fact]
    public void A_locked_door_is_unlocked_by_opening_it_unless_told_not_to()
    {
        _world.Vars[DoorId]["is_lock"] = true;
        Assert.True(Doors.IsLocked(Door));
        Doors.SetOpen(Door, true, unlock: false);
        Assert.Empty(_world.UserEvents);
        Doors.SetOpen(Door, true);
        Assert.False(Doors.IsLocked(Door));
        Assert.Single(_world.UserEvents);
    }

    [Fact]
    public void A_door_opening_or_closing_is_an_event()
    {
        _world.LendsIds = true;
        var changes = new List<(Instance Door, bool Open)>();
        Doors.OnChanged(_context, (door, open) => changes.Add((door, open)));

        RunCode("gml_Object_o_door_parent_Other_13", DoorId, () => _closed = false);
        RunCode("gml_Object_o_door_parent_Other_13", DoorId, () => _closed = true);
        // (A crypt's door: its own event, which only opens.)
        RunCode("gml_Object_o_cryptdoor_parent_Other_13", DoorId, () => _closed = false);
        // (The event run, the door as it was - one that can't close, say: nothing.)
        RunCode("gml_Object_o_door_parent_Other_13", DoorId, () => { });

        Assert.Equal(new[] { (Door, true), (Door, false), (Door, true) }, changes);
    }
}
