namespace StoneForge;

/// <summary>The quality an item is given with (<see cref="Items.Give(string, ItemQuality, double?)"/>): the game's rarities.</summary>
public enum ItemQuality
{
    /// <summary>Rolled, as loot is.</summary>
    Rolled = -4,
    Common = 1,
    Enchanted = 2,
    Magical = 3,
    Cursed = 5,
}
