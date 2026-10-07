using System.Text.RegularExpressions;
using StoneForge.Patcher;
using UndertaleModLib;
using UndertaleModLib.Models;

// The patcher on real game data (PatchedGameData: patched once, saved and read back). Skipped without the data.
public class PatcherIntegrationTests : IClassFixture<PatchedGameData>
{
    private readonly PatchedGameData _game;

    public PatcherIntegrationTests(PatchedGameData game) => _game = game;

    private void RequireData()
    {
        Skip.If(_game.Input == null, @"No unpatched VM game data: set STONEFORGE_TEST_DATA, or install StoneForge in Steam's Stoneshard on the VM branch (dotnet\data_base.win).");
        Assert.True(_game.InputWasUnpatched, _game.Input + " already has StoneForge's patches; the tests need unpatched data.");
    }

    [SkippableFact]
    public void Mod_consumable_skill_and_loader_hooks_compile()
    {
        RequireData();
        Assert.Equal(1, _game.Consumables);
        Assert.Equal(2, _game.Skills);
        Assert.Equal(2, _game.Objects);
        Assert.Equal(ScriptHooks.LoaderHooks.Length, _game.Hooks);
    }

    [SkippableFact]
    public void Original_object_IDs_and_code_order_are_preserved()
    {
        RequireData();
        Assert.Equal(_game.OriginalObjects, _game.Data.GameObjects.Take(_game.OriginalObjects.Length).Select(o => o.Name.Content));
        Assert.Equal(_game.OriginalCode, _game.Data.Code.Take(_game.OriginalCode.Length).Select(c => c.Name.Content));
    }

    [SkippableFact]
    public void Inheritance_and_event_links_survive_serialization()
    {
        RequireData();
        Assert.Equal("o_inv_wine", _game.Read.GetObject("o_inv_sf_test_tonic").ParentId.Name.Content);
        Assert.Equal("o_skill_" + _game.BaseSkill, _game.Read.GetObject("o_skill_sf_test_skill").ParentId.Name.Content);
        Assert.Equal("o_skill_passive", _game.Read.GetObject("o_pass_skill_sf_test_passive").ParentId.Name.Content);
        var ghost = _game.Read.GetObject("o_sf_test_ghost");
        Assert.Equal("o_enemy", ghost.ParentId.Name.Content);
        // (Every event StoneForge runs C# for: Create, Destroy, Clean Up, 3 Steps, 4 Draws, 12 alarms, 16 user events, 4 mouse.)
        Assert.Equal(42, ghost.Events.SelectMany(e => e).Count());
        Assert.Contains("event_inherited", _game.Read.ReadGml("gml_Object_o_sf_test_ghost_Create_0"));
        var plain = _game.Read.GetObject("o_sf_test_plain");
        Assert.Null(plain.ParentId);
        Assert.Contains("draw_self", _game.Read.ReadGml("gml_Object_o_sf_test_plain_Draw_0"));
        Assert.Contains(_game.Read.GetObject("o_stonemod_gui").Events.SelectMany(e => e).SelectMany(e => e.Actions), a => a.CodeId != null);
    }

    [SkippableFact]
    public void Loader_functions_survive_serialization()
    {
        RequireData();
        var restored = _game.Restored;
        var functions = restored.Code.Where(c => c.ParentEntry != null && c.Name.Content.StartsWith("gml_Script_scr_stonemod_")).ToArray();
        Assert.NotEmpty(functions);
        foreach (var function in functions)
        {
            string name = function.Name.Content;
            Assert.True(function.ParentEntry.ChildEntries.Contains(function), name + ": parent link");
            Assert.True(restored.Scripts.Any(s => s.Code == function), name + ": script link");
            Assert.True(restored.Functions.ByName(name) != null, name + ": function metadata");
            Assert.True(restored.CodeLocals.ByName(function.ParentEntry.Name.Content) != null, name + ": locals metadata");
            Assert.Single(restored.GlobalInitScripts, s => s.Code == function.ParentEntry);
            Assert.Contains("function ", _game.Read.ReadGml(function.ParentEntry.Name.Content));
        }
    }

    [SkippableFact]
    public void Mod_GML_compiles_once_each_with_arguments_and_resolved_calls()
    {
        RequireData();
        var restored = _game.Restored;
        var gml = _game.Gml;
        foreach (var function in gml.Functions)
        {
            string name = gml.InternalName(function.Name);
            var parent = restored.Code.ByName("gml_GlobalScript_" + name);
            Assert.True(parent != null && parent.ChildEntries.Count == 1 && parent.ChildEntries[0].Name.Content == "gml_Script_" + name, name + ": one entry, its own");
            var child = parent!.ChildEntries[0];
            Assert.Single(restored.GlobalInitScripts, s => s.Code == parent);
            Assert.True(restored.Scripts.Any(s => s.Name.Content == "gml_Script_" + name && s.Code == child), name + ": script points at it");
            Assert.True(child.LocalsCount == (restored.CodeLocals.ByName("gml_GlobalScript_" + name)?.Locals.Count ?? 1) - 1, name + ": locals " + child.LocalsCount);
            string text = _game.Read.ReadGml("gml_GlobalScript_" + name);
            // (Arguments decompile as argument0... or arg0..., by decompiler.)
            Assert.True(function.Parameters.Count == 0 || Regex.IsMatch(text, @"\barg(ument)?0\b"), name + ": parameters as arguments");
            foreach (var parameter in function.Parameters)
                Assert.False(Regex.IsMatch(text, @"(?<![\w.])" + parameter.Key + @"(?!\w)"), name + ": no variable " + parameter.Key);
        }
        // (A call to a function the compiler hadn't seen yet would be to a bare name - no such script.)
        Assert.DoesNotContain(restored.Functions, f => f.Name.Content.StartsWith("sf_gml_"));
        Assert.Matches("return " + gml.InternalName("Add") + @"\((arg|argument)0, (arg|argument)0\)", _game.Read.ReadGml("gml_GlobalScript_" + gml.InternalName("Twice")));
    }

    [SkippableFact]
    public void Script_hooks_survive_serialization()
    {
        RequireData();
        foreach (string hook in ScriptHooks.LoaderHooks)
            Assert.True(_game.Read.ReadGml("gml_GlobalScript_" + hook).Contains("__stonemod_script__"), hook);
    }

    [SkippableFact]
    public void Functions_inside_another_scripts_file_are_hookable()
    {
        RequireData();
        Assert.Equal(PatchedGameData.InnerHooks, _game.InnerHooked);
        foreach (string hook in PatchedGameData.InnerHooks)
        {
            string file = _game.Read.ScriptFile(hook);
            Assert.NotEqual("gml_GlobalScript_" + hook, file);
            // (The block in its own function: after its declaration, before the next one's.)
            var lines = _game.Read.ReadGml(file).Replace("\r\n", "\n").Split('\n');
            int start = Array.FindIndex(lines, l => l.TrimStart().StartsWith("function " + hook + "("));
            int end = Array.FindIndex(lines, start + 1, l => l.TrimStart().StartsWith("function "));
            var body = lines[(start + 1)..(end < 0 ? lines.Length : end)];
            Assert.Contains(body, l => l.Contains("\"__stonemod_script__\", \"" + hook + "\""));
        }
        // (And every other function in those files is still there.)
        foreach (var (file, functions) in _game.InnerFiles)
            Assert.Equal(functions, _game.Restored.Code.ByName(file).ChildEntries.Select(c => c.Name.Content).OrderBy(n => n, StringComparer.Ordinal));
    }

    [SkippableFact]
    public void Editor_reports_errors_and_contexts_are_separate()
    {
        RequireData();
        var read = _game.Read;
        Assert.Throws<InvalidOperationException>(() => read.GetObject("__sf_missing__"));
        Assert.Throws<InvalidOperationException>(() => read.AddFunction("function scr_stonemod_item_define() {}", "scr_stonemod_item_define"));
        var probe = read.AddObject("o_sf_test_errors");
        if (!probe.Events[(int)EventType.Create].Any())
            read.AddNewEvent(probe, "var sf_probe_value = 1;", EventType.Create, 0);
        Assert.Throws<InvalidOperationException>(() => read.AddNewEvent(probe, "", EventType.Create, 0));
        Assert.Throws<InvalidOperationException>(() => read.ReplaceGml("gml_Object_o_sf_test_errors_Create_0", "var = ;"));
        Assert.Null(_game.Data.GameObjects.ByName("o_sf_test_errors"));
    }
}
