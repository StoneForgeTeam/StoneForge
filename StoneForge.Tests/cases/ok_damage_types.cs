using System;
using System.Collections.Generic;
using StoneForge;
using StoneForge.GameDamageTypes;

namespace TestMod;

// A mod's own kinds of damage, over the game's (and pure), dealt with Combat.Damage.
public class Lightning : Shock
{
    public Lightning() { Name = "Lightning"; }
    protected override double Modify(DamageHit hit) => hit.Amount * 1.5;
}

public class Void : Pure
{
    public Void() { Name = "Void"; }
    protected override double Modify(DamageHit hit) => hit.Amount * (1 - Math.Clamp(hit.Target.Instance.Get("Void_Resistance").AsReal, 0, 100) / 100);
    protected override void OnDealt(DamageHit hit) { }
}

public class M : IStoneMod
{
    public void Unload() { }

    public void Load(ModContext context)
    {
        GameInstance? target = null;
        if (target != null)
        {
            Combat.Damage(target, DamageType.Fire, 5);
            Combat.Damage(target, new Lightning(), 5, target, new DamageOptions { ArmorPiercing = 10, Name = "Storm" });
            Combat.Damage(target, new Dictionary<DamageType, double> { [new Void()] = 4, [DamageType.Slashing] = 2 });
        }
    }
}
