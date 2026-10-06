using System.Globalization;

namespace StoneForge;

/// <summary>Dealing damage as the game does: through its damage calculation, so the target's protection, armour
/// piercing and resistances apply, the damage number shows over it, the combat log says so and, with a source,
/// the hit is the source's (who attacked whom, crimes, kills).
/// <code>
/// int done = Combat.Damage(target, DamageType.Shock, 12, source: cast.Caster);
/// Combat.Damage(target, new Dictionary&lt;DamageType, double&gt; { [DamageType.Fire] = 8, [DamageType.Slashing] = 4 }, attacker);
/// Combat.Damage(target, new Lightning(), 10, caster);   // a kind of your own (see DamageType)
/// </code></summary>
public static partial class Combat
{
    /// <summary>Damage of one kind to <paramref name="target"/> (a unit), from <paramref name="source"/> (null:
    /// nobody's - a trap's, say). Returns the damage done, after the target's protection and resistances (0 if it
    /// did none, or there's no such target).</summary>
    public static int Damage(GameInstance target, DamageType type, double amount, GameInstance? source = null, DamageOptions? options = null)
        => Damage(target, new Dictionary<DamageType, double> { [type] = amount }, source, options);

    /// <summary>Damage of several kinds at once - one hit, as a weapon's mixed damage is.</summary>
    public static int Damage(GameInstance target, IReadOnlyDictionary<DamageType, double> amounts, GameInstance? source = null, DamageOptions? options = null)
    {
        Game.CheckRunning("Combat.Damage");
        ArgumentNullException.ThrowIfNull(target);
        options ??= new DamageOptions();
        if (options.ArmorPiercing is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options), "ArmorPiercing is 0-100");
        if (!target.Instance.Exists)
            return 0;
        // Each kind's amount, as it modifies it (a kind of a mod's own: its bonuses, its own resistance).
        var hits = new List<DamageHit>();
        foreach (var (type, amount) in amounts)
        {
            ArgumentNullException.ThrowIfNull(type);
            if (amount <= 0)
                continue;
            var hit = new DamageHit(target, source, type, amount);
            hit.Amount = Math.Max(0, type.Modify(hit));
            if (hit.Amount > 0)
                hits.Add(hit);
        }
        if (hits.Count == 0)
            return 0;
        // (Nobody's: noone, as the game passes it. Nobody's and unnamed: named after its damage in the log.)
        GmValue from = source != null && !source.Instance.IsNone ? source.Instance : -4;
        string name = options.Name ?? (from.Kind == GmKind.Instance ? "" : hits[0].Type.Name);
        int dealt = 0;
        // The game's kinds in one hit through its calculation; pure damage past it.
        var kinds = hits.Where(h => h.Type.GameName != null).GroupBy(h => h.Type.GameName!).ToList();
        string typed = string.Join("|", kinds.Select(g => g.Key + "_Damage=" + g.Sum(h => h.Amount).ToString(CultureInfo.InvariantCulture)));
        // A mod's own kind named in the log's breakdown ("9 overcharge") - when it's all of that game kind in the hit.
        string labels = string.Join("|", kinds
            .Where(g => g.Select(h => h.Type.Name).Distinct().Count() == 1 && g.First().Type.Name != g.Key)
            .Select(g => g.Key + "_Damage=" + LogName(g.First().Type)));
        if (typed.Length > 0)
            dealt += DealTyped(target.Instance, from, typed, options.ArmorPiercing, options.Log, name, labels);
        double pure = hits.Where(h => h.Type.GameName == null).Sum(h => h.Amount);
        if (pure > 0 && target.Instance.Exists)
            dealt += (int)Game.CallScript("scr_pure_damage", default, from, target.Instance, pure, options.Log,
                name.Length > 0 ? name : "N/A").AsReal;
        foreach (var hit in hits)
        {
            hit.Dealt = dealt;
            hit.Type.OnDealt(hit);
        }
        return dealt;
    }

    // Damage dealt as the game deals it outside a weapon's swing (its traps, spells' splashes, backfires): an
    // o_damage_dealer at the target, its damage by type ("Shock_Damage=12|Fire_Damage=3"), through its user event 0
    // (scr_damage_with_calc) - armour, protection, resistances, the number over the target and the combat log the game's
    // own. Its owner (who did it: noone, nobody) makes it theirs - who hit whom, crimes, kills. labels: names for kinds in
    // the log's breakdown ("Shock_Damage=overcharge"), global.actionsLogDamages' own swapped for this hit only. The
    // damage done. (What was the patcher's GML scr_stonemod_damage: C# on both builds.)
    private static int DealTyped(Instance target, GmValue from, string typed, double piercing, bool log, string name, string labels)
    {
        if (!target.Exists)
            return 0;
        var swapped = new List<(string Key, GmValue Was)>();
        DsMap? logNames = Game.Global["actionsLogDamages"].AsDsMap;
        if (logNames is { } names)
            foreach (var (key, value) in Pairs(labels))
            {
                swapped.Add((key, names[key]));
                names[key] = value;
            }
        try
        {
            int dealer = Gm.AssetGetIndex("o_damage_dealer");
            Instance made = Instance.Of(Game.CallBuiltinTrusted("instance_create_depth", default, default, target.Get("x"), target.Get("y"), target.Get("depth"), dealer));
            if (made.IsNone)
                return 0;
            made.Set("target", target.Pointer == IntPtr.Zero ? target.Id : target.Get("id"));
            if (from.Kind == GmKind.Instance && Game.CallBuiltinTrusted("instance_exists", default, default, from).AsBool)
            {
                made.Set("owner", from);
                made.Set("name", Game.CallScript("scr_actionsLogGetName", default, from));
            }
            if (name.Length > 0)
                made.Set("name", name);
            made.Set("default_log", log);
            made.Set("Armor_Piercing", piercing);
            foreach (var (key, value) in Pairs(typed))
                made.Set(key, double.Parse(value, CultureInfo.InvariantCulture));
            Game.CallBuiltinAs("event_user", made, made, 0);
            return made.Exists ? (int)made.Get("damage").AsReal : 0;
        }
        finally
        {
            // (The game's names back.)
            if (logNames is { } back)
                foreach (var (key, was) in swapped)
                {
                    if (was.IsUndefined)
                        back.Remove(key);
                    else
                        back[key] = was;
                }
        }
    }

    // "a=1|b=2" as its pairs.
    private static IEnumerable<(string Key, string Value)> Pairs(string text)
    {
        foreach (string pair in text.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = pair.IndexOf('=');
            if (equals > 0)
                yield return (pair[..equals], pair[(equals + 1)..]);
        }
    }

    // A kind's name as the log's breakdown writes the game's ("shock"): lower case, nothing that would split the list.
    private static string LogName(DamageType type) => new string(type.Name.ToLowerInvariant().Where(c => c is not ('|' or '=' or '\n')).ToArray());
}
