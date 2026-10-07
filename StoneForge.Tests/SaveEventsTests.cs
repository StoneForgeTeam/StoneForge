using StoneForge;

// A save read and one about to be written, as events (SaveData.OnLoaded, OnSaving): around the game's scr_slotLoad and
// scr_slotSaveUpdate, each with its folder and save (laid out with FakeGame's lists, globals and scripts).
public class SaveEventsTests : FakeGame
{
    private readonly FakeDs _ds = new();
    private readonly FakeScripts _scripts = new();
    private readonly ModContext _context = new("save_events_test");
    private readonly List<string> _ran = new();

    public SaveEventsTests()
    {
        Ds = _ds;
        GameScripts = _scripts;
        KeepGlobalWrites = true;
        // (As the game's: a load makes the save data current; a save writes it.)
        _scripts.Add("scr_slotLoad", _ =>
        {
            _ran.Add("loaded");
            Globals["saveDataMap"] = DsMap.Create().Id;
            return GmValue.Undefined;
        });
        _scripts.Add("scr_slotSaveUpdate", _ =>
        {
            _ran.Add("written");
            return GmValue.Undefined;
        });
    }

    public override void Dispose()
    {
        Hooks.RemoveMod(_context.Id);
        base.Dispose();
    }

    [Fact]
    public void A_save_is_told_once_its_read_and_before_its_written()
    {
        SaveData.OnLoaded(_context, save => _ran.Add($"OnLoaded {save.Slot.Name}/{save.Name} data={SaveData.Available}"));
        SaveData.OnSaving(_context, save => _ran.Add($"OnSaving {save.Slot.Name}/{save.Name}"));

        Game.CallScript("scr_slotLoad", default, "character_1", "autosave_3");
        Game.CallScript("scr_slotSaveUpdate", default, "character_1", "save_4");

        Assert.Equal(new[] { "loaded", "OnLoaded character_1/autosave_3 data=True", "OnSaving character_1/save_4", "written" }, _ran);
    }
}
