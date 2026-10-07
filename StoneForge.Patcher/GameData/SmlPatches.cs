using System.Diagnostics;
using System.Text.Json;

namespace StoneForge.Patcher;

internal static class SmlPatches
{
    internal static string HostDirectory => Path.Combine(AppContext.BaseDirectory, "msl");

    internal static string HostKey()
    {
        // Version the helper and its dependencies as well as the package input.
        if (!Directory.Exists(HostDirectory)) return "missing-msl-host";
        return string.Join("|", Directory.EnumerateFiles(HostDirectory, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal).Select(p =>
            {
                using var stream = File.OpenRead(p);
                return Path.GetRelativePath(HostDirectory, p) + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
            }));
    }

    internal static UndertaleModLib.UndertaleData Apply(GameFolder game, List<SmlPackage> packages,
        out Dictionary<string, SmlMetadata> metadata)
    {
        string host = Path.Combine(HostDirectory, "StoneForge.MslHost.exe");
        if (!File.Exists(host)) throw new FileNotFoundException("MSL compatibility helper missing. Reinstall the complete StoneForge release.", host);
        Preflight.RequireMslRuntime();
        string work = Path.Combine(game.Dotnet, "msl-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var copies = new List<string>();
            foreach (var package in packages.Where(p => p.Enabled))
            {
                // Snapshot bytes so the fingerprint describes exactly what the helper executes.
                string copy = Path.Combine(work, Path.GetFileName(package.Path));
                File.Copy(package.Path, copy);
                using var stream = File.OpenRead(copy);
                if (Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)) != package.Hash)
                    throw new IOException("MSL package changed during preparation: " + package.Name);
                copies.Add(copy);
                PatcherConsole.Log(package.Name + ": " + SmlCatalog.Warning);
            }
            string output = Path.Combine(work, "patched.win");
            string request = Path.Combine(work, "request.json");
            string details = Path.Combine(work, "metadata.json");
            File.WriteAllText(request, JsonSerializer.Serialize(new { Input = game.BaseData, Output = output, Packages = copies, Metadata = details }));
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = work, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(request);
            PatcherConsole.Log($"MSL: Preparing {copies.Count} package(s). Detailed log: dotnet/msl-patch.log");
            var elapsed = Stopwatch.StartNew();
            using var process = Process.Start(start) ?? throw new IOException("Could not start MSL helper.");
            // Drain both pipes concurrently, keeping live console output and the complete diagnostic log.
            using var log = new StreamWriter(Path.Combine(game.Dotnet, "msl-patch.log"), false) { AutoFlush = true };
            object gate = new();
            long lastActivity = 0;
            Task Pump(StreamReader reader) => Task.Run(async () =>
            {
                while (await reader.ReadLineAsync() is { } line)
                    lock (gate)
                    {
                        log.WriteLine(line);
                        PatcherConsole.Log(line);
                        lastActivity = elapsed.ElapsedMilliseconds;
                    }
            });
            var stdout = Pump(process.StandardOutput);
            var stderr = Pump(process.StandardError);
            while (!process.WaitForExit(1000))
            {
                if (elapsed.ElapsedMilliseconds >= 300_000)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    Task.WaitAll(stdout, stderr);
                    throw new TimeoutException("MSL patching exceeded five minutes. See dotnet/msl-patch.log.");
                }
                lock (gate)
                    if (elapsed.ElapsedMilliseconds - lastActivity >= 10_000)
                    {
                        PatcherConsole.Log($"MSL: Still working ({elapsed.Elapsed.TotalSeconds:0}s elapsed)...");
                        lastActivity = elapsed.ElapsedMilliseconds;
                    }
            }
            Task.WaitAll(stdout, stderr);
            if (process.ExitCode != 0) throw new InvalidOperationException("MSL patching failed. Game data was not replaced. See dotnet/msl-patch.log.");
            metadata = JsonSerializer.Deserialize<Dictionary<string, SmlMetadata>>(File.ReadAllText(details)) ?? new();
            PatcherConsole.Log("MSL: Checking patched game data...");
            using var input = File.OpenRead(output);
            return UndertaleModLib.UndertaleIO.Read(input);
        }
        finally { Directory.Delete(work, recursive: true); }
    }
}
