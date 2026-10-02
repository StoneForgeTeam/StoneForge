using System.IO;
using System.Security.Cryptography;
using System.Text;
using UndertaleModLib;

namespace StoneForge.Patcher;

/// <summary>data.win as the game reads it: the game's own (kept as dotnet\data_base.win) with the loader's
/// additions (<see cref="LoaderPatches"/>), the scripts mods hook (<see cref="ScriptHooks"/>) and mods'
/// consumables' objects (<see cref="ConsumableObjects"/>). Rebuilt only when the game's data (a game update, a
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
        var gml = GmlCatalog.Read(game.Mods);
        string key = Key(newBase ? game.Data : game.BaseData, hooks, consumables, skills) + gml.Fingerprint;
        if (!newBase && key == builtFrom && File.Exists(Path.Combine(game.Dotnet, "stoneforge-gml.txt")))
        {
            PatcherConsole.Log($"Game data up to date ({hooks.Count} hooked script(s)).");
            return;
        }

        PatcherConsole.Show();
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
            key = Key(game.BaseData, hooks, consumables, skills) + gml.Fingerprint;
        }
        if (gameData == null)
        {
            gameData = Read(game.BaseData);
            if (IsOurs(gameData))
                throw new InvalidOperationException("dotnet\\data_base.win has StoneForge's changes, so it can't be the base. Restore the game's own data.win (Steam: Verify integrity of game files), delete dotnet\\data_base.win and start again.");
        }

        var editor = new GameDataEditor(gameData);
        LoaderPatches.Apply(editor);
        var added = ConsumableObjects.Add(editor, consumables);
        var addedSkills = SkillObjects.Add(editor, skills);
        int made = ScriptHooks.HookAll(editor, hooks);
        ModGmlPatches.Apply(editor, gml);

        string temp = game.Data + ".tmp";
        using (var output = File.Create(temp))
            UndertaleIO.Write(output, gameData, _ => { });
        File.Move(temp, game.Data, true);
        File.WriteAllLines(Path.Combine(game.Dotnet, "stoneforge-gml.txt"),
            new[] { Stamp(game.Data) }.Concat(gml.Projects.Values.Select(p => p.Name + "|" + p.Fingerprint)));
        File.WriteAllLines(game.DataKey, new[] { Stamp(game.Data), key });
        ModClassDeclaration.Remember(game.KnownConsumables, added);
        ModClassDeclaration.Remember(game.KnownSkills, addedSkills);
        PatcherConsole.Log($"Done ({made} of {hooks.Count} script(s) hooked, {added.Count} mod consumable(s), {addedSkills.Count} mod skill(s)).");
    }

    /// <summary>Uninstall: the game's own data.win back (if ours is in place), our files gone.</summary>
    public static void Restore(GameFolder game)
    {
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
        foreach (string known in new[] { game.KnownConsumables, game.KnownSkills })
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
    private static string Key(string baseData, List<string> hooks, List<ModClassDeclaration> consumables, List<ModClassDeclaration> skills)
        => Hash($"{Stamp(baseData)}|{string.Join(",", hooks)}|{string.Join(",", consumables)}|{string.Join(",", skills)}|{Hash(typeof(GameDataBuilder).Assembly.ManifestModule.ModuleVersionId + LoaderGml.Contents())}");

    // A file as it stands: its size and time.
    private static string Stamp(string file)
    {
        var info = new FileInfo(file);
        return info.Exists ? $"{info.Length}|{info.LastWriteTimeUtc.Ticks}" : "";
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
