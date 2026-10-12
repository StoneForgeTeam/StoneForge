using StoneForge;

// A number setting (ModSettings.Number) stays within its range. (Never saved here: the loader's frame does that.)
public class NumberSettingTests : FakeGame
{
    [Fact]
    public void A_number_stays_within_its_range()
    {
        var settings = new ModSettings("number_setting_test");
        try
        {
            var port = settings.Number("port", "Port", 7777, min: 1, max: ushort.MaxValue);
            Assert.Equal(7777, port.Value);
            port.Value = 70000;
            Assert.Equal(65535, port.Value);
            port.Value = 0;
            Assert.Equal(1, port.Value);
            port.Value = 25565;
            Assert.Equal(25565, port.Value);
            Assert.Equal(7777, settings.Number("other", "Other", 99999, min: 0, max: 7777).Value);
        }
        finally { ModSettings.ByMod.Remove("number_setting_test"); }
    }
}
