using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.InteropServices;
using ModShardLauncher;
using ModShardLauncher.Controls;
using ModShardLauncher.Mods;
using UndertaleModLib.Models;
using System.Collections.Generic;
using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace StoneForge.MslHost;

internal static class EnhancedSupport
{
    internal static void ResolveDependencies()
    {
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, name.Name + ".dll");
            return File.Exists(path) ? context.LoadFromAssemblyPath(path) : null;
        };
        AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, name) =>
        {
            string path = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? name : name + ".dll");
            return File.Exists(path) ? NativeLibrary.Load(path) : IntPtr.Zero;
        };
    }

    internal static void Initialize(Main main, ModInfos mods, string output)
    {
        var assembly = typeof(Main).Assembly;
        var sourceType = assembly.GetType("ModShardLauncher.Controls.ModSourceInfos", throwOnError: true)!;
        var sourcePage = RuntimeHelpers.GetUninitializedObject(sourceType);
        var sources = sourceType.GetProperty("ModSources")!;
        sources.SetValue(sourcePage, Activator.CreateInstance(sources.PropertyType));
        typeof(Main).GetField("ModSourcePage")!.SetValue(main, sourcePage);
        typeof(Main).GetField("ModPage")!.SetValue(main, mods);
        sourceType.GetField("Instance")!.SetValue(null, sourcePage);
        // Enhanced's audio importer writes next to dataPath. Only the owned staging directory is exposed.
        typeof(DataLoader).GetField("dataPath", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(null, output);
    }

    internal static void CheckConflicts(ModInfos mods)
    {
        var checker = typeof(ModLoader).GetNestedType("DependencyChecker", BindingFlags.Public)!;
        foreach (var mod in mods.Mods)
        {
            var warnings = (System.Collections.Generic.List<string>)checker.GetMethod("CheckDependencies")!
                .Invoke(null, new object[] { mod, mods.Mods })!;
            foreach (string warning in warnings) Serilog.Log.Warning("MSL Enhanced dependency: {Warning}", warning);
            if (warnings.Any(w => w.Contains("REQUIRED", StringComparison.Ordinal) || w.Contains("LOAD ORDER ERROR", StringComparison.Ordinal)))
                throw new InvalidDataException("MSL Enhanced dependencies for " + mod.Name + ":\n" + string.Join("\n", warnings));
        }
        // The supplied fork's PatchMods shows a modal dialog for these conflicts. Fail before entering it.
        var analyzer = typeof(Main).Assembly.GetType("ModShardLauncher.ConflictAnalyzer", true)!;
        var conflicts = (IList)analyzer.GetMethod("AnalyzeConflicts")!.Invoke(null, new object[] { mods.Mods })!;
        if (conflicts.Count == 0) return;
        string report = (string)analyzer.GetMethod("GenerateConflictReport")!.Invoke(null, new object[] { conflicts, mods.Mods })!;
        throw new InvalidDataException("MSL Enhanced resource conflicts must be resolved before patching:\n" + report);
    }

    // Mirrors the pinned fork's VM patch sequence, replacing the two AppData writers with staged imports.
    // Mod files and resource lists remain intact while mod code runs.
    internal static void PatchMods(ModInfos mods, string output, EnhancedFailures failures)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        foreach (string name in new[] { "Credits", "Disclaimers", "Menus" })
        {
            var field = typeof(ModLoader).GetField(name, flags)!;
            field.SetValue(null, Activator.CreateInstance(field.FieldType));
        }
        foreach (var file in mods.Mods)
        {
            if (!File.Exists(file.Path)) throw new FileNotFoundException("MSL Enhanced package disappeared.", file.Path);
            var settings = typeof(Main).GetField("Settings", BindingFlags.Static | BindingFlags.Public)!.GetValue(null)!;
            ((IList)settings.GetType().GetField("EnableMods")!.GetValue(settings)!).Add(file.Name);
            file.PatchStatus = PatchStatus.Patching;
            TextureLoader.LoadTextures(file);
            InvokeLoader("NewTextureLoader", "LoadTextures", file);
            EnhancedResources.PrepareAudioGroups(file, output);
            InvokeLoader("AudioLoader", "LoadAudio", file);
            EnhancedResources.StageFonts(file, output);
            EnhancedResources.LoadShaders(file, output);
            if (failures.Count > 0) throw new InvalidDataException("MSL Enhanced resource imports failed for " + file.Name + ". See the preceding log.");
            ResetDecompileContext();
            file.instance.PatchMod();
            foreach (var type in file.Assembly.GetTypes().Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(Weapon))))
                ModLoader.LoadWeapon(type);
            file.PatchStatus = PatchStatus.Success;
        }
        ResetDecompileContext();
        var credits = (List<(string, string[])>)typeof(ModLoader).GetField("Credits", flags)!.GetValue(null)!;
        InvokeMsl("AddDisclaimerRoom", credits.Select(c => c.Item1).ToArray(), credits.SelectMany(c => c.Item2).Distinct().ToArray());
        InvokeMsl("ChainDisclaimerRooms", typeof(ModLoader).GetField("Disclaimers", flags)!.GetValue(null)!);
        InvokeMsl("CreateMenu", typeof(ModLoader).GetField("Menus", flags)!.GetValue(null)!);
        EnhancedResources.ApplyPendingTextures();
    }

    private static void ResetDecompileContext() => typeof(Msl).GetMethod("ResetDecompileContext", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    private static void InvokeMsl(string name, params object[] args) => typeof(Msl).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!.Invoke(null, args);
    private static void InvokeLoader(string type, string method, ModFile mod) => typeof(Main).Assembly.GetType("ModShardLauncher." + type, true)!
        .GetMethod(method)!.Invoke(null, new object[] { mod });
}

internal sealed class EnhancedFailures : ILogEventSink
{
    private int _count;
    internal int Count => Volatile.Read(ref _count);
    public void Emit(LogEvent entry)
    {
        // Enhanced resource importers catch exceptions and log errors; don't accept a partially imported mod.
        if (entry.Level >= LogEventLevel.Error) Interlocked.Increment(ref _count);
    }
}
