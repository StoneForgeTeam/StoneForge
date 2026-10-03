namespace StoneForge;

/// <summary>The skills menu's sections (<see cref="ModSkill.Group"/>). The game's three - <see cref="Weaponry"/>,
/// <see cref="Utility"/>, <see cref="Sorcery"/> - take a mod's tabs after their own; any other name is a section of
/// the mods' own, after the game's (default: <see cref="Mods"/>).
/// <code>Group = SkillGroup.Sorcery;   // its tab beside Pyromancy, Electromancy...</code></summary>
public static class SkillGroup
{
    /// <summary>The game's Weaponry section (swords, axes, bows...).</summary>
    public const string Weaponry = "Weaponry";
    /// <summary>The game's Utility section (athletics, combat, survival...).</summary>
    public const string Utility = "Utility";
    /// <summary>The game's Sorcery section (the schools of magic).</summary>
    public const string Sorcery = "Sorcery";
    /// <summary>The mods' own section, after the game's (the default).</summary>
    public const string Mods = "Mods";

    // The game's sections in the menu's order.
    private static readonly string[] Game = { Weaponry, Utility, Sorcery };

    // Which of the game's sections a group is (-1: none): by its English name, or by the name the game shows it by
    // (headers: the game's, in order - in the game's language).
    internal static int GameIndex(string group, IReadOnlyList<string> headers)
    {
        for (int i = 0; i < Game.Length && i < headers.Count; i++)
            if (string.Equals(group, Game[i], StringComparison.OrdinalIgnoreCase)
                || string.Equals(group, headers[i], StringComparison.CurrentCultureIgnoreCase))
                return i;
        return -1;
    }
}
