using System.Text.Json;

namespace StoneForge.Loader;

internal static class SmlRuntime
{
    internal static void Discover()
    {
        string dotnet = Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!;
        try
        {
            var packages = SmlCatalog.Read(ModManager.ModsDir, Path.Combine(dotnet, "mods.json"));
            SmlPrepared? prepared = null;
            string state = Path.Combine(dotnet, SmlCatalog.StateFile);
            if (File.Exists(state))
            {
                try { prepared = JsonSerializer.Deserialize<SmlPrepared>(File.ReadAllText(state)); }
                catch (Exception e) { Game.Log("MSL preparation state unreadable: " + e.Message); }
            }
            var data = new FileInfo(Path.Combine(dotnet, "..", "data.win"));
            bool matches = data.Exists && prepared?.DataStamp == $"{data.Length}|{data.LastWriteTimeUtc.Ticks}";
            foreach (var package in packages)
            {
                bool applied = matches && prepared!.Applied.ContainsKey(package.Id);
                string? error = applied && prepared!.Applied[package.Id] != package.Hash
                    ? "The package changed, but this run still contains its previous patches. Restart to apply it; check dotnet/msl-patch.log if preparation failed."
                    : Game.IsNative ? "MSL packages require the VM modbranch; they cannot run on the native build." : null;
                SmlMetadata? detail = null;
                if (prepared?.Metadata?.TryGetValue(package.Id, out var cached) == true && cached.Hash == package.Hash)
                    detail = cached;
                ModRegistry.All.Add(new ModInfo(package.Id,
                    (string.IsNullOrWhiteSpace(detail?.Name) ? package.Name : detail.Name) + (detail?.Runtime == "MSLE" ? " [MSLE]" : " [MSL]"),
                    detail?.Description ?? "", detail?.Author ?? "", detail?.Version ?? "",
                    applied, package.Path, Error: error, IsSml: true));
                Game.Log(package.Name + ": " + SmlCatalog.Warning);
            }
        }
        catch (Exception e) { Game.Log("MSL discovery failed: " + e.Message); }
    }
}
