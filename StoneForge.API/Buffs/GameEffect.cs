namespace StoneForge;

/// <summary>One of the game's effects on a unit (<see cref="UnitEffects.On"/>): its instance, its object (o_db_stun,
/// o_b_no_retreat...), how many turns it has left, whether it shows (an icon, not one of the game's invisible workings)
/// and whether it's harmful (a debuff).</summary>
public readonly record struct GameEffect(Instance Instance, int Object, string Name, double Duration, bool Shown, bool Harmful);
