using ModShardLauncher;
using ModShardLauncher.Controls;
using ModShardLauncher.Mods;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UndertaleModLib;

if (args.Length != 1) { Console.Error.WriteLine("Expected an MSL patch request JSON file."); return 1; }
try
{
    Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();
    using var request = JsonDocument.Parse(File.ReadAllText(args[0]));
    var root = request.RootElement;
    string input = root.GetProperty("Input").GetString()!;
    string output = root.GetProperty("Output").GetString()!;
    if (Path.GetFullPath(input).Equals(Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("MSL output must be separate from the preserved base.");
    Console.WriteLine("MSL: Reading preserved game data...");
    using (var stream = File.OpenRead(input)) DataLoader.data = UndertaleIO.Read(stream);
    if (DataLoader.data.IsYYC()) throw new NotSupportedException("MSL mods require the VM modbranch.");
    // (MSL's patching reads its launcher's window and mod list: stand-ins, never shown - their constructors, which build
    // the WPF UI, aren't run. The window only gives MSL's version, for its "built with another MSL" warning.)
    var main = (Main)RuntimeHelpers.GetUninitializedObject(typeof(Main));
    main.mslVersion = "v" + FileVersionInfo.GetVersionInfo(typeof(Main).Assembly.Location).FileVersion;
    Main.Instance = main;
    var modList = (ModInfos)RuntimeHelpers.GetUninitializedObject(typeof(ModInfos));
    modList.Mods = new();
    ModInfos.Instance = modList;
    ModLoader.Initalize();
    LootUtils.ResetLootTables();
    var metadata = new System.Collections.Generic.Dictionary<string, object>();
    // MSL's default author and description are "unknown" in Chinese; omit that placeholder.
    const string Unknown = "\u672a\u77e5";
    string ReadDetail(Func<string> read)
    {
        try
        {
            string value = read()?.Trim() ?? "";
            return value == Unknown ? "" : value;
        }
        catch (Exception error) { Console.WriteLine("MSL metadata: " + error.Message); return ""; }
    }
    foreach (var entry in root.GetProperty("Packages").EnumerateArray())
    {
        string path = entry.GetString()!;
        Console.WriteLine("MSL: " + Path.GetFileName(path));
        var file = FileReader.Read(path) ?? throw new InvalidDataException("Not an MSL package: " + path);
        var types = file.Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Mod))).ToArray();
        if (types.Length != 1) throw new InvalidDataException("Expected one MSL Mod class: " + path);
        var mod = (Mod)Activator.CreateInstance(types[0])!;
        mod.ModFiles = file;
        file.instance = mod;
        file.isEnabled = true;
        mod.LoadAssembly();
        modList.Mods.Add(file);
        using var bytes = File.OpenRead(path);
        metadata["sml:" + Path.GetFileName(path).ToLowerInvariant()] = new {
            Hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)),
            Name = ReadDetail(() => mod.Name), Author = ReadDetail(() => mod.Author),
            Version = ReadDetail(() => mod.Version), Description = ReadDetail(() => mod.Description)
        };
    }
    LogUtils.InjectLog();
    Console.WriteLine("MSL: Applying mod patches...");
    ModLoader.PatchMods();
    Console.WriteLine("MSL: Finalizing loot and script ordering...");
    LootUtils.InjectLootScripts();
    StoneForge.MslHost.CodeOrdering.Normalize(DataLoader.data.Code);
    Console.WriteLine("MSL: Saving patched game data...");
    using (var stream = File.Create(output)) UndertaleIO.Write(stream, DataLoader.data);
    File.WriteAllText(root.GetProperty("Metadata").GetString()!, JsonSerializer.Serialize(metadata));
    Console.WriteLine("MSL: Finished.");
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error);
    return 2;
}
