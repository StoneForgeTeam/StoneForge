namespace StoneForge.Patcher;

/// <summary>Script hooks for C# mods: the scripts mods declare ([assembly: HookScript("...")] in their source -
/// <see cref="ModSources.DeclaredHooks"/>) and the loader's own get a small block at the top of their body.
/// For script &lt;name&gt;:
///     if (variable_global_exists("__smh_&lt;name&gt;"))
///     if (global.__smh_&lt;name&gt;)
///     {
///         (its arguments into an array)
///         var __smr = string_concat("__stonemod_script__", "&lt;name&gt;", &lt;the array&gt;)
///         if is_array(__smr)
///             return __smr[0];
///     }
/// string_concat is a built-in Stoneshard never calls; the native bridge intercepts it and hands marked calls to
/// C#. A script nobody subscribes to (its flag unset) only pays the one check.</summary>
internal static class ScriptHooks
{
    /// <summary>Scripts the loader itself hooks, whatever mods there are: an attack's outcome (scr_attack runs one
    /// of these as the attacker; each applies its damage and returns it) - for items' OnAttack / OnAttacked; an item's
    /// worn pictures being picked (scr_itemCharSpritesInit) - for mod items' female and per-character ones.</summary>
    public static readonly string[] LoaderHooks =
    {
        "scr_attack_result_hit", "scr_attack_result_block", "scr_attack_result_dodge", "scr_attack_result_fumble",
        "scr_itemCharSpritesInit",
        "scr_cast_spell", "scr_cast_aoe_spell", "scr_skill_reparse_locked",
        // (SaveSlots.OnInfoSaving.)
        "scr_slotMapSave",
        // (ContextMenus: a menu as it opens.)
        "scr_create_context_menu",
        // (Turns.OnTurn: the world's turn.)
        "scr_global_turn",
        // (SaveData.OnLoaded / OnSaving: a save read, or about to be written.)
        "scr_slotLoad", "scr_slotSaveUpdate",
        // (Quests: started, a step on, done, failed.)
        "scr_quest_start", "scr_quest_set_progress", "scr_quest_set_complete", "scr_quest_set_failed",
        // (LootTables: a table's slots beyond the game's nine, rolled after its own roll.)
        "scr_loot_from_tables",
    };

    /// <summary>Each script made hookable; how many could be.</summary>
    public static int HookAll(GameDataEditor editor, IEnumerable<string> names, List<string>? hooked = null)
    {
        int made = 0;
        foreach (string name in names)
        {
            try
            {
                ScriptEditor.InsertAtBodyStart(editor, name, Stub(name));
                made++;
                hooked?.Add(name);
                PatcherConsole.Log($"  {name}: hookable");
            }
            catch (Exception e)
            {
                PatcherConsole.Log($"  {name}: can't be hooked - {e.Message.Split('\n')[0]}");
            }
        }
        return made;
    }

    private static string Stub(string name) =>
        // Modded data can compile boolean operators without short-circuiting. Never read an unset hook flag.
        "    if (variable_global_exists(\"__smh_" + name + "\"))\n" +
        "    if (global.__smh_" + name + ")\n" +
        "    {\n" +
        "        var __sma = array_create(argument_count)\n" +
        "        for (var __smi = 0; __smi < argument_count; __smi++)\n" +
        "            __sma[__smi] = argument[__smi]\n" +
        "        var __smr = string_concat(\"__stonemod_script__\", \"" + name + "\", __sma)\n" +
        "        if is_array(__smr)\n" +
        "            return __smr[0];\n" +
        "    }";
}
