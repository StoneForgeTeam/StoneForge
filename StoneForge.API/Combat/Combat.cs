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
public static class Combat
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
            dealt += (int)Game.CallScript("scr_stonemod_damage", default, target.Instance, from, typed,
                options.ArmorPiercing, options.Log, name, labels).AsReal;
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

    // A kind's name as the log's breakdown writes the game's ("shock"): lower case, nothing that would split the list.
    private static string LogName(DamageType type) => new string(type.Name.ToLowerInvariant().Where(c => c is not ('|' or '=' or '\n')).ToArray());
}
