using StoneForge;
using StoneForge.Gml;
using StoneForge.Patcher;
using UndertaleModLib;

/// <summary>Unpatched game data patched once for all the integration tests - the loader's patches, a mod
/// consumable and skill, GmlFixture's GML as a mod "GmlFixture", the loader's script hooks - then saved to a temporary
/// file and read back. The input is never changed. <see cref="Input"/> is null when there's no data to test with.</summary>
public sealed class PatchedGameData : IDisposable
{
    private const string Steam = @"C:\Program Files (x86)\Steam\steamapps\common\Stoneshard\dotnet\data_base.win";

    public string? Input { get; }
    public bool InputWasUnpatched { get; }
    /// <summary>The data as patched, before saving.</summary>
    public UndertaleData Data { get; } = null!;
    /// <summary>The data read back from the saved file, and an editor on it.</summary>
    public UndertaleData Restored { get; } = null!;
    internal GameDataEditor Read { get; } = null!;
    public string[] OriginalObjects { get; } = Array.Empty<string>();
    public string[] OriginalCode { get; } = Array.Empty<string>();
    public string BaseSkill { get; } = "";
    public int Consumables { get; }
    public int Skills { get; }
    public int Objects { get; }
    public int Hooks { get; }
    /// <summary>Functions defined inside another script's file, hooked as a mod would (Gwynel's house cutscene steps, the
    /// vineyard thief's wine check).</summary>
    public static readonly string[] InnerHooks =
    {
        "scr_rewards_find_guinnel_1", "scr_rewards_find_guinnel_door_2", "scr_npc_lines_vineyard_thief_check_wine",
    };
    public List<string> InnerHooked { get; } = new();
    /// <summary>The functions each of their files declared before (its child entries).</summary>
    public Dictionary<string, string[]> InnerFiles { get; } = new();
    public GmlProject Gml { get; } = null!;
    private readonly string _output = Path.Combine(Path.GetTempPath(), "StoneForgePatcherTest-" + Guid.NewGuid().ToString("N") + ".win");

    public PatchedGameData()
    {
        Input = FindInput();
        if (Input == null)
            return;
        using (var input = File.OpenRead(Input))
            Data = UndertaleIO.Read(input, (_, _) => { }, _ => { });
        var editor = new GameDataEditor(Data);
        OriginalObjects = Data.GameObjects.Select(o => o.Name.Content).ToArray();
        OriginalCode = Data.Code.Select(c => c.Name.Content).ToArray();
        InputWasUnpatched = Data.GameObjects.ByName("o_stonemod_gui") == null;
        if (!InputWasUnpatched)
            return;
        LoaderPatches.Apply(editor);
        Consumables = ConsumableObjects.Add(editor, new() { new("sf_test_tonic", "Consumable", "wine") }).Count;
        BaseSkill = Data.GameObjects.First(o => o.Name.Content.StartsWith("o_skill_") &&
            !o.Name.Content.EndsWith("_ico") && Data.GameObjects.ByName(o.Name.Content + "_ico") != null).Name.Content[8..];
        Skills = SkillObjects.Add(editor, new() { new("sf_test_skill", "ModSkill", BaseSkill), new("sf_test_passive", "ModPassive", null) }).Count;
        Objects = ModGameObjects.Add(editor, new() { new("sf_test_ghost", "GameObject", "o_enemy"), new("sf_test_plain", "GameObject", null) }).Count;
        // A mod's own GML: GmlFixture's (Twice calls Add, a file read after it).
        string mods = Path.Combine(Path.GetTempPath(), "StoneForgePatcherGml-" + Guid.NewGuid().ToString("N"));
        CopyFolder(Path.Combine(AppContext.BaseDirectory, "GmlFixture"), Path.Combine(mods, "GmlFixture"));
        try
        {
            var gml = GmlCatalog.Read(mods);
            Gml = gml.Projects.Values.Single();
            ModGmlPatches.Apply(editor, gml);
        }
        finally { Directory.Delete(mods, true); }
        Hooks = ScriptHooks.HookAll(editor, ScriptHooks.LoaderHooks);
        foreach (string file in InnerHooks.Select(editor.ScriptFile).Distinct())
            InnerFiles[file] = Data.Code.ByName(file).ChildEntries.Select(c => c.Name.Content).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        ScriptHooks.HookAll(editor, InnerHooks, InnerHooked);
        using (var output = File.Create(_output))
            UndertaleIO.Write(output, Data, _ => { });
        using (var roundtrip = File.OpenRead(_output))
            Restored = UndertaleIO.Read(roundtrip, (_, _) => { }, _ => { });
        Read = new GameDataEditor(Restored);
    }

    // STONEFORGE_TEST_DATA, or StoneForge's preserved copy of the unpatched data in the Steam install.
    private static string? FindInput()
    {
        string? path = Environment.GetEnvironmentVariable("STONEFORGE_TEST_DATA");
        if (!string.IsNullOrEmpty(path))
            return File.Exists(path) ? Path.GetFullPath(path) : throw new FileNotFoundException("STONEFORGE_TEST_DATA: no such file", path);
        return File.Exists(Steam) ? Steam : null;
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (string file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(to, Path.GetRelativePath(from, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose()
    {
        Restored?.Dispose();
        Data?.Dispose();
        if (File.Exists(_output))
            File.Delete(_output);
    }
}
