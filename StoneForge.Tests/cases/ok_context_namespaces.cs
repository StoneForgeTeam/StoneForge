using StoneForge;
namespace OrganizedMod.Items { public sealed class Blade : Weapon { public Blade() : base("context_blade", "sword") {} } }
namespace OrganizedMod.Buffs { public sealed class Focus : ModBuff { public Focus() : base("context_focus") {} } }
namespace OrganizedMod.Skills { public sealed class Bolt : ModSkill { public Bolt() : base("context_bolt", "chain_lightning") {} } }
namespace OrganizedMod
{
    public sealed class Mod : IStoneMod
    {
        public void Load(ModContext context)
        {
            context.Items.Add(new Items.Blade());
            context.Buffs.Add(new Buffs.Focus());
            context.Skills.Add(new Skills.Bolt());
        }
        public void Unload() {}
        public void Operations(ModContext context, ModItem item, Consumable food, ModBuff buff, GameInstance target)
        {
            context.Items.Add(food);
            context.Items.Give(item);
            context.Items.Give(food, 2);
            context.Items.Give("wine");
            context.Items.Exists("wine");
            context.Items.Stat("sword", "Damage");
            context.Items.Stat("sword", default(WeaponColumn));
            context.Items.Stat("shirt", default(ArmorColumn));
            context.Buffs.Apply(buff, target, 3);
            context.Buffs.ApplyGame("o_db_daze", target, 3);
            context.Buffs.Has(target, buff);
        }
    }
}
