namespace StoneForge;

/// <summary>The character's attributes, as skills ask for them (<see cref="ModSkill.RequireAttributes"/>).</summary>
public enum CharacterAttribute
{
    Strength,
    Agility,
    Perception,
    Vitality,
    Willpower,
}

internal static class CharacterAttributes
{
    // Its name in the game (the player's variable, the key of global.attribute).
    public static string GameName(this CharacterAttribute attribute) => attribute switch
    {
        CharacterAttribute.Strength => "STR",
        CharacterAttribute.Agility => "AGL",
        CharacterAttribute.Perception => "PRC",
        CharacterAttribute.Vitality => "Vitality",
        _ => "WIL",
    };
}
