using StoneForge;

// Which of the skills menu's sections a mod's Group joins: the game's, by English name or as the game shows it.
public class SkillGroupTests
{
    private static readonly string[] English = { "WEAPONRY", "UTILITY", "SORCERY" };
    private static readonly string[] Russian = { "ОРУЖИЕ", "ПРОЧЕЕ", "МАГИЯ" };

    [Theory]
    [InlineData(SkillGroup.Weaponry, 0)]
    [InlineData(SkillGroup.Utility, 1)]
    [InlineData(SkillGroup.Sorcery, 2)]
    [InlineData("sorcery", 2)]
    [InlineData(SkillGroup.Mods, -1)]
    [InlineData("Example", -1)]
    public void Game_sections_by_English_name(string group, int section)
        => Assert.Equal(section, SkillGroup.GameIndex(group, English));

    [Fact]
    public void Game_sections_by_the_name_the_game_shows_in_its_language()
    {
        Assert.Equal(2, SkillGroup.GameIndex("Магия", Russian));
        Assert.Equal(2, SkillGroup.GameIndex(SkillGroup.Sorcery, Russian));
    }
}
