using StoneForge;
using StoneForge.Loader;

// The mod sandbox: each case in cases\ is one mod's source - ok_* must compile and pass the source policy, every
// other must be refused. And ModFiles' path rules.
public class SandboxTests
{
    private static string CasesDir => Path.Combine(AppContext.BaseDirectory, "cases");

    public static IEnumerable<object[]> Cases()
        => Directory.GetFiles(CasesDir, "*.cs").OrderBy(f => f).Select(f => new object[] { Path.GetFileNameWithoutExtension(f) });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Case_compiles_only_if_allowed(string name)
    {
        string temp = Path.Combine(Path.GetTempPath(), "smtest_" + name);
        if (Directory.Exists(temp))
            Directory.Delete(temp, true);
        Directory.CreateDirectory(temp);
        File.Copy(Path.Combine(CasesDir, name + ".cs"), Path.Combine(temp, name + ".cs"));
        var result = ModCompiler.Compile(temp);
        bool shouldPass = name.StartsWith("ok_");
        Assert.True((result.Assembly != null) == shouldPass,
            shouldPass ? "refused: " + string.Join("; ", result.Errors.Take(4)) : "compiled, but should have been refused");
    }

    // Reads in the game / data folders, writes in the mod's own / data folder only.
    [Theory]
    [InlineData("settings.json", true, true)]
    [InlineData("sub/data.txt", true, true)]
    [InlineData("{data}mymod.ini", true, true)]
    [InlineData("{game}data.win", false, true)]
    [InlineData("{game}data.win", true, false)]
    [InlineData("{game}dotnet\\StoneForge.dll", true, false)]
    [InlineData("{game}mods\\OtherMod\\x.cs", true, false)]
    [InlineData(@"..\OtherMod\x.cs", true, false)]
    [InlineData(@"..\..\data.win", true, false)]
    [InlineData(@"..\..\..\..\..\Windows\win.ini", false, false)]
    [InlineData(@"C:\Windows\win.ini", false, false)]
    [InlineData(@"\\server\share\x", false, false)]
    [InlineData(@"\\?\C:\Windows\win.ini", false, false)]
    [InlineData(@"\\.\PhysicalDrive0", false, false)]
    [InlineData("notes.txt:hidden", true, false)]
    [InlineData("{game}_evil\\x.txt", false, false)]
    public void Mod_file_paths(string path, bool write, bool allowed)
    {
        path = path.Replace("{game}_evil", ModFiles.GameFolder + "_evil")
            .Replace("{game}", ModFiles.GameFolder + Path.DirectorySeparatorChar)
            .Replace("{data}", ModFiles.DataFolder + Path.DirectorySeparatorChar);
        var files = new ModFiles(Path.Combine(ModFiles.GameFolder, "mods", "TestMod"));
        bool ok;
        try { files.Resolve(path, write); ok = true; }
        catch (UnauthorizedAccessException) { ok = false; }
        Assert.True(ok == allowed, $"{(write ? "write" : "read")} {path}: {(ok ? "allowed" : "refused")}");
    }
}
