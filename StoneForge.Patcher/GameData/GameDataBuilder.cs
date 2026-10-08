using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UndertaleModLib;

namespace StoneForge.Patcher;

/// <summary>data.win as the game reads it: the game's own (kept as dotnet\data_base.win) with the loader's
/// additions (<see cref="LoaderPatches"/>), the scripts mods hook (<see cref="ScriptHooks"/>) and mods'
/// consumables' objects (<see cref="ConsumableObjects"/>) - on the native (YYC) build, the loader's objects only
/// (<see cref="NativeLoaderPatches"/>). Rebuilt only when the game's data (a game update, a
/// re-patch with another tool), the hooked scripts, mods' consumables or the patcher (its code, its GML) change. The editing uses UndertaleModLib directly.</summary>
internal static class GameDataBuilder
{
    public static void Prepare(GameFolder game)
    {
        if (File.Exists(game.OldDataCopy))
            File.Delete(game.OldDataCopy);

        string[] state = File.Exists(game.DataKey) ? File.ReadAllLines(game.DataKey) : Array.Empty<string>();
        string written = state.Length > 0 ? state[0] : "";
        string builtFrom = state.Length > 1 ? state[1] : "";
        // data.win isn't what we wrote (the first time, a game update, a re-patch with another tool): it's the new base.
        bool newBase = !File.Exists(game.BaseData) || Stamp(game.Data) != written;
        var hooks = ModSources.DeclaredHooks(game.Mods);
        var declared = ModClassDeclaration.Declared(game.Mods);
        var consumables = ModClassDeclaration.WithKnown(declared, game.KnownConsumables);
        var skills = ModClassDeclaration.WithKnown(declared, game.KnownSkills);
        var objects = ModClassDeclaration.WithKnown(declared, game.KnownObjects);
        var gml = GmlCatalog.Read(game.Mods);
        var sml = SmlCatalog.Read(game.Mods, Path.Combine(game.Dotnet, "mods.json"));
        var metadata = new Dictionary<string, SmlMetadata>();
        string smlState = Path.Combine(game.Dotnet, SmlCatalog.StateFile);
        if (File.Exists(smlState))
        {
            try
            {
                var previous = JsonSerializer.Deserialize<SmlPrepared>(File.ReadAllText(smlState));
                foreach (var package in sml)
                    if (previous?.Metadata?.TryGetValue(package.Id, out var detail) == true && detail.Hash == package.Hash)
                        metadata[package.Id] = detail;
            }
            catch (Exception e) { PatcherConsole.Log("MSL metadata cache unreadable: " + e.Message); }
        }
        var smlRuntime = SmlRuntimeSelection.Select(game, sml);
        sml = smlRuntime.OrderPackages(sml);
        string smlKey = SmlCatalog.Fingerprint(sml) + (sml.Any(p => p.Enabled) ? SmlPatches.HostKey() + smlRuntime.Key : "")
            + SmlAudioFiles.Fingerprint(game, smlRuntime.Enhanced);
        string key = Key(newBase ? game.Data : game.BaseData, hooks, consumables, skills, objects) + gml.Fingerprint + smlKey;
        if (!newBase && key == builtFrom && File.Exists(Path.Combine(game.Dotnet, "stoneforge-gml.txt")) && File.Exists(game.HookedScripts) && File.Exists(Path.Combine(game.Dotnet, SmlCatalog.StateFile)))
        {
            // (The native build hooks no scripts here - its list is empty: they're detoured as mods load.)
            PatcherConsole.Log(File.ReadAllLines(game.HookedScripts).Length == 0 && hooks.Count > 0
                ? "Game data up to date." : $"Game data up to date ({hooks.Count} hooked script(s)).");
            return;
        }

        PatcherConsole.Show();
        using var audio = new SmlAudioFiles(game, smlRuntime.Enhanced);
        PatcherConsole.Log("Updating Stoneshard's game data for mods - once, until the mods or the game change...");
        UndertaleData? gameData = null;
        if (newBase)
        {
            // Check it before it replaces the base: a data.win that only got a new time (copied, restored from a
            // backup) still has our changes, and then the base we kept stays.
            gameData = Read(game.Data);
            if (IsOurs(gameData))
            {
                if (!File.Exists(game.BaseData))
                    throw new InvalidOperationException("data.win already has StoneForge's changes and the game's own copy (dotnet\\data_base.win) is missing. Restore it (Steam: Verify integrity of game files) and start again.");
                PatcherConsole.Log("  data.win is our own build with a new date: rebuilding from dotnet\\data_base.win");
                gameData = null;
            }
            else
            {
                PatcherConsole.Log(File.Exists(game.BaseData) ? "  data.win has changed (game update or re-patch): using it as the new base" : "  keeping the game's own data.win (dotnet\\data_base.win)");
                File.Copy(game.Data, game.BaseData, overwrite: true);
            }
            key = Key(game.BaseData, hooks, consumables, skills, objects) + gml.Fingerprint + smlKey;
        }
        if (gameData == null)
        {
            gameData = Read(game.BaseData);
            if (IsOurs(gameData))
                throw new InvalidOperationException("dotnet\\data_base.win has StoneForge's changes, so it can't be the base. Restore the game's own data.win (Steam: Verify integrity of game files), delete dotnet\\data_base.win and start again.");
        }

        if (sml.Any(p => p.Enabled))
        {
            if (gameData.IsYYC()) throw new InvalidOperationException("MSL mods require the VM modbranch. Disable them or switch to modbranch.");
            // Never layer MSL over its previous output: always start at the preserved input.
            if (gameData.GameObjects.ByName("o_msl_log") != null || gameData.GameObjects.ByName("o_msl_mod_disclaimer") != null)
                throw new InvalidOperationException("The preserved game data already contains MSL patches. Restore clean modbranch data before enabling .sml packages.");
            gameData.Dispose();
            gameData = SmlPatches.Apply(game, sml, smlRuntime, audio, out var loadedMetadata);
            foreach (var pair in loadedMetadata) metadata[pair.Key] = pair.Value;
        }
        PatcherConsole.Log("Applying StoneForge's game-data changes...");
        var editor = new GameDataEditor(gameData);
        var added = new List<ModClassDeclaration>();
        var addedSkills = new List<ModClassDeclaration>();
        var addedObjects = new List<ModClassDeclaration>();
        var hooked = new List<string>();
        int made = 0;
        // (The native build - no GML in its data.win: the loader's objects only. Scripts are hooked by detours there.)
        if (gameData.IsYYC())
        {
            NativeLoaderPatches.Apply(editor);
            // (Mods' consumables are code-less children of the game's on both builds.)
            added = ConsumableObjects.Add(editor, consumables);
            addedSkills = SkillObjects.Add(editor, skills, native: true);
            addedObjects = ModGameObjects.AddNative(editor, objects);
        }
        else
        {
            LoaderPatches.Apply(editor);
            added = ConsumableObjects.Add(editor, consumables);
            addedSkills = SkillObjects.Add(editor, skills);
            addedObjects = ModGameObjects.Add(editor, objects);
            made = ScriptHooks.HookAll(editor, hooks, hooked);
            ModGmlPatches.Apply(editor, gml);
        }

        string temp = game.Data + ".tmp";
        PatcherConsole.Log("Saving game data...");
        using (var output = File.Create(temp))
            UndertaleIO.Write(output, gameData, _ => { });
        audio.Commit(temp);
        File.Delete(temp);
        // Audio outputs and their preserved originals now contribute to the stable next-start key.
        smlKey = SmlCatalog.Fingerprint(sml) + (sml.Any(p => p.Enabled) ? SmlPatches.HostKey() + smlRuntime.Key : "")
            + SmlAudioFiles.Fingerprint(game, smlRuntime.Enhanced);
        key = Key(game.BaseData, hooks, consumables, skills, objects) + gml.Fingerprint + smlKey;
        File.WriteAllLines(Path.Combine(game.Dotnet, "stoneforge-gml.txt"),
            new[] { Stamp(game.Data) }.Concat(gml.Projects.Values.Select(p => p.Name + "|" + p.Fingerprint)));
        File.WriteAllLines(game.DataKey, new[] { Stamp(game.Data), key });
        File.WriteAllText(Path.Combine(game.Dotnet, SmlCatalog.StateFile), JsonSerializer.Serialize(
            new SmlPrepared(Stamp(game.Data), sml.Where(p => p.Enabled).ToDictionary(p => p.Id, p => p.Hash), metadata)));
        ModClassDeclaration.Remember(game.KnownConsumables, added);
        ModClassDeclaration.Remember(game.KnownSkills, addedSkills);
        ModClassDeclaration.Remember(game.KnownObjects, addedObjects);
        File.WriteAllLines(game.HookedScripts, hooked);
        PatcherConsole.Log(gameData.IsYYC()
            ? $"Done - the native build: {added.Count} mod consumable(s), {addedSkills.Count} mod skill(s), {addedObjects.Count} mod object(s); scripts are hooked as mods load."
            : $"Done ({made} of {hooks.Count} script(s) hooked, {added.Count} mod consumable(s), {addedSkills.Count} mod skill(s), {addedObjects.Count} mod object(s)).");
    }

    /// <summary>Uninstall: the game's own data.win back (if ours is in place), our files gone.</summary>
    public static void Restore(GameFolder game)
    {
        using var audio = new SmlAudioFiles(game);
        audio.Commit();
        string[] state = File.Exists(game.DataKey) ? File.ReadAllLines(game.DataKey) : Array.Empty<string>();
        if (File.Exists(game.BaseData) && state.Length > 0 && Stamp(game.Data) == state[0])
        {
            File.Copy(game.BaseData, game.Data, overwrite: true);
            PatcherConsole.Log("Restored the game's own data.win.");
        }
        else
            PatcherConsole.Log("data.win isn't ours - left as it is.");
        if (File.Exists(game.BaseData))
            File.Delete(game.BaseData);
        if (File.Exists(game.DataKey))
            File.Delete(game.DataKey);
        string gmlState = Path.Combine(game.Dotnet, "stoneforge-gml.txt");
        if (File.Exists(gmlState)) File.Delete(gmlState);
        string smlState = Path.Combine(game.Dotnet, SmlCatalog.StateFile);
        if (File.Exists(smlState)) File.Delete(smlState);
        foreach (string known in new[] { game.KnownConsumables, game.KnownSkills, game.KnownObjects, game.HookedScripts })
            if (File.Exists(known))
                File.Delete(known);
    }

    private static UndertaleData Read(string file)
    {
        using var input = File.OpenRead(file);
        return UndertaleIO.Read(input, (_, _) => { }, _ => { });
    }

    // Game data the loader has already been added to (its Draw GUI object is there - in every build).
    private static bool IsOurs(UndertaleData gameData) => gameData.GameObjects.ByName("o_stonemod_gui") != null;

    // What a build is made from: the base data, the hooked scripts, mods' consumables and skills (as declared), and this
    // patcher (its code and its GML).
    private static string Key(string baseData, List<string> hooks, List<ModClassDeclaration> consumables, List<ModClassDeclaration> skills, List<ModClassDeclaration> objects)
        => Hash($"{Stamp(baseData)}|{string.Join(",", hooks)}|{string.Join(",", consumables)}|{string.Join(",", skills)}|{string.Join(",", objects)}|{Hash(typeof(GameDataBuilder).Assembly.ManifestModule.ModuleVersionId + LoaderGml.Contents())}");

    // A file as it stands: its size and time.
    private static string Stamp(string file)
    {
        var info = new FileInfo(file);
        return info.Exists ? $"{info.Length}|{info.LastWriteTimeUtc.Ticks}" : "";
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
