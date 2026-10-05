namespace StoneForge;

public static partial class Combat
{
    /// <summary>One unit attacks another with its weapon as the game resolves it (scr_attack): hit, dodge, block, crit,
    /// its damage, counterattacks - and the attacker's turn taken. With <paramref name="forced"/>, as a forced attack: its
    /// turn is left alone (an attack its turn didn't choose).</summary>
    public static void Attack(Instance attacker, Instance target, bool forced = false)
    {
        GmValue wasForced = attacker.Get("force_attack");
        if (forced)
            attacker["force_attack"] = true;
        try { Game.CallScript("scr_attack", attacker, target); }
        finally
        {
            if (forced && attacker.Exists)
                attacker["force_attack"] = wasForced.IsUndefined ? false : wasForced;
        }
    }

    /// <summary>Takes health off a unit as the game's plain damage does (scr_simple_damage): the flash and number, its
    /// morale, its reaction to being hit - with no damage types or resistances (see <see cref="Damage(GameInstance,
    /// DamageType, double, GameInstance?, DamageOptions?)"/> for those). From <paramref name="source"/>, the attacker it
    /// turns on.</summary>
    public static void Hit(Instance target, double amount, Instance? source = null)
        => Game.CallScript("scr_simple_damage", source ?? default, target, amount, 200);

    /// <summary>How much of a unit's damage list one attacker has (scr_enemy_target_damage_priority_get) - more than 0:
    /// they took part in fighting it, for its kill.</summary>
    public static double DamageShare(Instance unit, Instance attacker)
        => Game.CallScript("scr_enemy_target_damage_priority_get", unit, attacker, 0).AsReal;

    /// <summary>Adds an attacker to a unit's damage list (scr_enemy_target_priority_add) - counted in its kill.</summary>
    public static void AddDamageShare(Instance unit, Instance attacker, double amount = 1)
        => Game.CallScript("scr_enemy_target_priority_add", unit, attacker, amount);

    /// <summary>Runs as an attack has been resolved - anyone's, a melee blow or a shot: hit, crit, block, dodge or
    /// fumble, its damage already dealt (the game's attack outcome scripts, which StoneForge hooks itself). The attacker
    /// and target are kept by id: the attack can be kept.</summary>
    public static void OnAttack(ModContext context, Action<Attack> handler)
    {
        foreach (var (script, result) in Outcomes)
            context.OnScript(script, after: call =>
            {
                if (call.Args.Length == 0 || call.Self.IsNone)
                    return;
                Instance attacker = call.Self.Persist(), target = Instance.Of(call.Args[0]);
                if (target.IsNone)
                    return;
                // (A hit's seventh argument: whether it's a crit.)
                AttackResult outcome = result == AttackResult.Hit && call.Args.Length > 6 && call.Args[6].AsBool ? AttackResult.Crit : result;
                Instance player = Player.Instance;
                handler(new Attack(attacker, target, outcome, call.Result.AsReal, attacker.Get("shoot_attack").AsBool,
                    !player.IsNone && attacker.Equals(player), !player.IsNone && target.Equals(player)));
            });
    }

    /// <summary>Runs as an attack strikes - a hit or a crit (<see cref="OnAttack"/>, the others left out).</summary>
    public static void OnHit(ModContext context, Action<Attack> handler)
        => OnAttack(context, attack =>
        {
            if (attack.IsHit)
                handler(attack);
        });

    private static readonly (string Script, AttackResult Result)[] Outcomes =
    {
        ("scr_attack_result_hit", AttackResult.Hit), ("scr_attack_result_block", AttackResult.Block),
        ("scr_attack_result_dodge", AttackResult.Dodge), ("scr_attack_result_fumble", AttackResult.Fumble),
    };
}
