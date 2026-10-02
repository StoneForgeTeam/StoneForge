using StoneForge;
using StoneForge.Gml;
using StoneForge.Loader;

// Mod GML: bindings named after the mod folder, the rewrite the game's compiler needs, compile order, and a mod
// compiling against its own bindings.
public class GmlTests
{
    private const string Folder = "ExampleMod";
    private const string Function = "/// @stoneforge return double\n/// @stoneforge param value double\nfunction Twice(value) { return value * 2; }";

    private static GmlProject Parse(string text = Function, string folder = Folder)
        => GmlProject.Parse(folder, new[] { new KeyValuePair<string, string>("GML/Twice.gml", text) });

    private static string Fn(string name, string body)
        => "/// @stoneforge return double\n/// @stoneforge param v double\nfunction " + name + "(v) { " + body + " }";

    private static GmlProject Files(params (string Name, string Body)[] fns)
        => GmlProject.Parse(Folder, fns.Select(f => new KeyValuePair<string, string>("GML/" + f.Name + ".gml", Fn(f.Name, f.Body))));

    [Fact]
    public void Bindings_are_named_after_the_mod_folder_with_stable_internal_names()
    {
        var p = Parse();
        var changed = Parse(Function.Replace("* 2", "* 3"));
        Assert.Equal("ExampleMod.Gml", p.Binding);
        Assert.Contains("namespace ExampleMod {", p.Bindings());
        Assert.Contains("public static class Gml {", p.Bindings());
        Assert.Contains("public static double Twice(double value)", p.Bindings());
        Assert.Equal(p.InternalName("Twice"), changed.InternalName("Twice"));
        Assert.NotEqual(p.Fingerprint, changed.Fingerprint);
        Assert.NotEqual(p.InternalName("Twice"), Parse(folder: "OtherMod").InternalName("Twice"));
        Assert.Contains("function " + p.InternalName("Twice"), p.Rewrite(p.Functions[0]));
    }

    [Theory]
    [InlineData("ExampleMod", "ExampleMod")]
    [InlineData("my-cool mod", "MyCoolMod")]
    [InlineData("2d fx", "_2dFx")]
    public void Folder_names_become_CSharp_namespaces(string folder, string expected)
        => Assert.Equal(expected, GmlProject.NamespaceFor(folder));

    [Theory]
    [InlineData(@"C:\Game\mods\ExampleMod\GML\Sub\Add.gml", "C:/Game/mods/ExampleMod")]
    [InlineData(@"C:\Game\mods\ExampleMod\GML\Add.gml", "C:/Game/mods/ExampleMod")]
    [InlineData(@"C:\Game\mods\ExampleMod\Add.gml", null)]
    public void The_mod_folder_is_the_one_holding_GML(string path, string? expected)
        => Assert.Equal(expected, GmlProject.ModFolderOf(path));

    [Fact]
    public void Rewrite_leaves_comments_strings_and_member_names_intact()
    {
        var p = Parse(Function.Replace("return value * 2;", "/* Twice */ var note = \"Twice\"; return obj.Twice + Twice(value - 1);"));
        var text = p.Rewrite(p.Functions[0]);
        Assert.Contains("/* Twice */", text);
        Assert.Contains("\"Twice\"", text);
        Assert.Contains("obj.Twice", text);
        Assert.Contains(p.InternalName("Twice") + "(argument0 - 1)", text);
    }

    [Fact]
    public void Parameters_compile_as_arguments_not_variables()
    {
        const string two = "/// @stoneforge return double\n/// @stoneforge param left double\n/// @stoneforge param right double\n"
            + "function Pick(left, right) { var s = { left: 1, right: obj.left }; switch (left) { case right: break; } return left > 0 ? left : right; }";
        var p = GmlProject.Parse(Folder, new[] { new KeyValuePair<string, string>("GML/Pick.gml", two) });
        var text = p.Rewrite(p.Functions[0]);
        Assert.Contains("(argument0, argument1)", text);
        Assert.Contains("{ left: 1, right: obj.left }", text); // struct fields and members kept
        Assert.Contains("switch (argument0) { case argument1:", text);
        Assert.Contains("return argument0 > 0 ? argument0 : argument1;", text); // ternary
    }

    [Fact]
    public void Functions_compile_after_the_functions_they_call()
    {
        // (Files are read in name order: A calls B calls C, so C must come first.)
        var p = Files(("A", "return B(v) + 1;"), ("B", "return C(v) * 2;"), ("C", "return v;"), ("Self", "return v <= 0 ? 0 : Self(v - 1);"));
        var order = p.CompileOrder.Select(f => f.Name).ToList();
        Assert.True(order.IndexOf("C") < order.IndexOf("B") && order.IndexOf("B") < order.IndexOf("A"), string.Join(",", order));
        Assert.Contains("Self", order);
    }

    [Fact]
    public void Functions_calling_each_other_in_a_loop_are_rejected()
        => Assert.Throws<ArgumentException>(() => Files(("A", "return B(v);"), ("B", "return C(v);"), ("C", "return A(v);")));

    [Theory]
    [InlineData("function Twice(value) { return value; }")] // no annotations
    [InlineData(Function + " game_end();")] // trailing top-level code
    [InlineData("/// @stoneforge return not_a_type\n/// @stoneforge param value double\nfunction Twice(value) { return value; }")]
    [InlineData(Function + " /*")] // unterminated comment
    public void Malformed_source_and_missing_annotations_are_rejected(string source)
        => Assert.Throws<ArgumentException>(() => Parse(source));

    [Fact]
    public void A_mod_compiles_against_its_own_folder_named_bindings()
    {
        string root = NewMods(out string mod, out string plain);
        var catalog = GmlCatalog.Read(root);
        Assert.Equal("my-gml mod", Assert.Single(catalog.Projects.Values).Name); // only the folder with GML
        var compiled = ModCompiler.Compile(mod);
        Assert.True(compiled.Assembly != null, string.Join("; ", compiled.Errors));
        Assert.NotNull(ModCompiler.Compile(plain).Assembly); // a mod without GML is unaffected
    }

    [Fact]
    public void A_class_clashing_with_the_bindings_is_a_compiler_error()
    {
        NewMods(out string mod, out _);
        File.AppendAllText(Path.Combine(mod, "Test.cs"), " namespace MyGmlMod { public class Gml {} }");
        Assert.Null(ModCompiler.Compile(mod).Assembly);
    }

    // A mods folder: "my-gml mod" (GML, and C# calling it) and "PlainMod" (C# only).
    private static string NewMods(out string mod, out string plain)
    {
        string root = Path.Combine(Path.GetTempPath(), "StoneForgeGmlTests-" + Guid.NewGuid().ToString("N"));
        mod = Path.Combine(root, "my-gml mod");
        plain = Path.Combine(root, "PlainMod");
        Directory.CreateDirectory(Path.Combine(mod, "GML"));
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(mod, "GML", "Twice.gml"), Function);
        File.WriteAllText(Path.Combine(mod, "Test.cs"), "namespace MyGmlMod { public class Test { public double Invoke() => Gml.Twice(21); } }");
        File.WriteAllText(Path.Combine(plain, "Test.cs"), "public class Test { }");
        return root;
    }
}
