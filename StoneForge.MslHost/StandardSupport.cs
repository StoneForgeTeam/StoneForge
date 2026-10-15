using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using ModShardLauncher;
using ModShardLauncher.Controls;
using ModShardLauncher.Mods;
using Serilog;
using UndertaleModLib;

namespace StoneForge.MslHost;

// Standard MSL's patch sequence (ModLoader.PatchMods, MSL 35fec305), run here so a later pass - on data MSL Enhanced has
// patched first - keeps the one-time additions already there (its disclaimer room and chain, mods menu, msl_always_true)
// instead of stopping on them. The mods themselves patch as MSL patches them.
internal static class StandardSupport
{
    internal static void PatchMods(ModInfos mods, bool continuation)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        foreach (string name in new[] { "Credits", "Disclaimers", "Menus" })
        {
            var field = typeof(ModLoader).GetField(name, flags)!;
            field.SetValue(null, Activator.CreateInstance(field.FieldType));
        }
        var settings = typeof(Main).GetField("Settings", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
        var enabled = (IList)settings.GetType().GetField("EnableMods")!.GetValue(settings)!;
        foreach (var mod in mods.Mods.Where(m => m.isEnabled))
        {
            enabled.Add(mod.Name);
            mod.PatchStatus = PatchStatus.Patching;
            if (mod.Version != Main.Instance.mslVersion)
                Log.Warning("Mod {{{0}}} was built with msl {{{1}}} which is different from the current msl {{{2}}}", mod.Name, mod.Version, Main.Instance.mslVersion);
            TextureLoader.LoadTextures(mod);
            mod.instance.PatchMod();
            foreach (var type in mod.Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Weapon))))
                ModLoader.LoadWeapon(type);
            mod.PatchStatus = PatchStatus.Success;
        }
        var credits = (System.Collections.Generic.List<(string, string[])>)typeof(ModLoader).GetField("Credits", flags)!.GetValue(null)!;
        EnhancedSupport.Once(continuation, "disclaimer room", () => EnhancedSupport.InvokeMsl("AddDisclaimerRoom",
            credits.Select(c => c.Item1).ToArray(), credits.SelectMany(c => c.Item2).Distinct().ToArray()));
        EnhancedSupport.Once(continuation, "disclaimer chain", () => EnhancedSupport.InvokeMsl("ChainDisclaimerRooms", typeof(ModLoader).GetField("Disclaimers", flags)!.GetValue(null)!));
        EnhancedSupport.Once(continuation, "mods menu", () => EnhancedSupport.InvokeMsl("CreateMenu", typeof(ModLoader).GetField("Menus", flags)!.GetValue(null)!));
        if (continuation && DataLoader.data.Code.ByName("gml_GlobalScript_msl_always_true") != null)
            Console.WriteLine("MSL: msl_always_true was already added by the earlier pass.");
        else
            EnhancedSupport.InvokeMsl("AddFunction", "function msl_always_true() {\n    return true;\n}", "msl_always_true");
    }
}
