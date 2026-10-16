using StoneForge;

public class TitleFontTests
{
    [Theory]
    [InlineData("居合·拔刀剑 [MSL]", "f_dmg")]
    [InlineData("Katana 拔刀剑", "f_dmg")]
    [InlineData("カタナ", "f_dmg")]
    [InlineData("한국어", "f_dmg")]
    [InlineData("\U00020000", "f_dmg")]
    [InlineData("Stealth Mode [MSL]", "f_digits")]
    [InlineData("Русский", "f_digits")]
    [InlineData("", "f_digits")]
    public void Titles_use_a_consistent_font_for_the_entire_label(string text, string font)
        => Assert.Equal(font, Draw.TitleFont(text));
}
