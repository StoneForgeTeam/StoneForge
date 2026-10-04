namespace StoneForge;

/// <summary>The game's effects (buffs and debuffs - a stun, a bleed, No Retreat...) on any unit, the player's or another:
/// what's on one, putting one on as the game's attacks and skills do (with its immunities, the target's fortitude),
/// refreshing one, and taking them all off. Mods' own buffs are <see cref="Buffs"/>'.
/// <code>
/// UnitEffects.Create("o_db_stun", enemy, 2, owner: player);
/// foreach (var effect in UnitEffects.On(enemy).Where(e => e.Shown))
///     Game.Log($"{effect.Name}: {effect.Duration} turns");
/// </code></summary>
public static class UnitEffects
{
    private static readonly Dictionary<int, (string Name, bool Shown, bool Harmful)> Kinds = new();
    private static int _invisible = -2, _debuff = -2, _states = -2;

    /// <summary>The effects on a unit (its buffs list), in its order.</summary>
    public static List<GameEffect> On(Instance unit)
    {
        var effects = new List<GameEffect>();
        GmValue buffs = unit.Get("buffs");
        if (buffs.Kind != GmKind.Real || !Game.CallBuiltin("ds_exists", buffs, 2).AsBool)
            return effects;
        int count = Game.CallBuiltin("ds_list_size", buffs).AsInt;
        for (int i = 0; i < count; i++)
        {
            Instance effect = Instance.Of(Game.CallBuiltin("ds_list_find_value", buffs, i));
            if (effect.IsNone || !effect.Exists)
                continue;
            int obj = effect.Get("object_index").AsInt;
            var (name, shown, harmful) = Kind(obj);
            effects.Add(new GameEffect(effect, obj, name, effect.Get("duration").AsReal, shown, harmful));
        }
        return effects;
    }

    /// <summary>Whether a unit has an effect (by its object's name).</summary>
    public static bool Has(Instance unit, string effect) => On(unit).Exists(e => e.Name == effect);

    /// <summary>An effect's icon (its object's sprite), by its object's name; -1 for none.</summary>
    public static int IconOf(string effect)
        => ObjectOf(effect) is >= 0 and int obj ? Game.CallBuiltin("object_get_sprite", obj).AsInt : -1;

    /// <summary>Whether an effect's object shows - not one of the game's invisible workings (o_invisible_buff's), and with
    /// an icon.</summary>
    public static bool IsShown(int obj) => Kind(obj).Shown;

    /// <summary>Puts an effect on a unit as the game does (scr_effect_create): its immunities apply (Stun_Immunity...),
    /// a debuff's turns are shortened by the target's fortitude, and the target's HUD and icons show it. From
    /// <paramref name="owner"/> (the target itself if none); <paramref name="stage"/> for effects that have stages.
    /// The effect made, or none (immune, dead, not a unit).</summary>
    public static Instance Create(string effect, Instance target, double turns, Instance? owner = null, double stage = 1)
        => ObjectOf(effect) is >= 0 and int obj
            ? Instance.Of(Game.CallScript("scr_effect_create", default, obj, turns, target, owner is { IsNone: false } o ? o : target, stage))
            : default;

    /// <summary>Refreshes an effect on a unit as the game does (scr_effect_update): its turns set to
    /// <paramref name="turns"/>, and one made if it has fewer than <paramref name="stacks"/> of it. The one made, or
    /// none.</summary>
    public static Instance Refresh(string effect, Instance target, double turns, int stacks = 1)
        => ObjectOf(effect) is >= 0 and int obj
            ? Instance.Of(Game.CallScript("scr_effect_update", default, obj, target, turns, stacks))
            : default;

    /// <summary>Takes every effect on a unit off (each one whose target it is - its buffs, and the game's states pointing
    /// at it, even off screen), with their Destroy events. Before a unit is taken out without its own Destroy event
    /// (<see cref="Units.Remove"/>): left, they'd point at a unit that's gone, and the game's code reading their target
    /// crashes the game.</summary>
    public static void RemoveAll(Instance unit)
    {
        if (_states == -2)
            _states = Gm.AssetGetIndex("c_abstract_states");
        if (_states < 0)
            return;
        unit = unit.Persist();
        foreach (Instance effect in Instances.All(_states, includeCulled: true))
            if (Instance.Of(effect.Get("target")).Equals(unit))
                effect.Destroy();
    }

    // An effect's name, whether it shows, whether it's harmful: read once per object.
    private static (string Name, bool Shown, bool Harmful) Kind(int obj)
    {
        if (!Kinds.TryGetValue(obj, out var kind))
        {
            if (_invisible == -2)
            {
                _invisible = Gm.AssetGetIndex("o_invisible_buff");
                _debuff = Gm.AssetGetIndex("o_debuff");
            }
            bool shown = !(_invisible >= 0 && Gm.ObjectIsAncestor(obj, _invisible)) && Game.CallBuiltin("object_get_sprite", obj).AsInt >= 0;
            Kinds[obj] = kind = (Gm.ObjectGetName(obj), shown, _debuff >= 0 && Gm.ObjectIsAncestor(obj, _debuff));
        }
        return kind;
    }

    private static int ObjectOf(string effect) => Gm.AssetGetIndex(effect);
}
