using System.Diagnostics;
using System.Text.Json;

namespace StoneForge.Patcher;

internal static class SmlPatches
{
    internal static string HostDirectory => Path.Combine(AppContext.BaseDirectory, "msl");

    /// <summary>How long the MSL helper may go with neither output nor CPU use before it's taken as stuck and
    /// ended. There's no overall limit: a large package can take an hour while it's still working.</summary>
    internal static readonly TimeSpan StallLimit = TimeSpan.FromMinutes(5);

    // CPU use over a second that counts as working (a stuck process uses next to none).
    private static readonly TimeSpan CpuProgress = TimeSpan.FromMilliseconds(100);

    // The helper's CPU time so far (all its threads); zero once it has gone.
    private static TimeSpan CpuTime(Process process)
    {
        try
        {
            process.Refresh();
            return process.TotalProcessorTime;
        }
        catch (InvalidOperationException) { return TimeSpan.Zero; }
    }

    private static string Hms(TimeSpan time) => time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");

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
        SmlRuntimeSelection runtime, SmlAudioFiles audio, out Dictionary<string, SmlMetadata> metadata)
    {
        string host = Path.Combine(HostDirectory, "StoneForge.MslHost.exe");
        if (!File.Exists(host)) throw new FileNotFoundException("MSL compatibility helper missing. Reinstall the complete StoneForge release.", host);
        Preflight.RequireMslRuntime();
        string work = Path.Combine(game.Dotnet, "msl-work", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            metadata = new();
            // (Each pass on the last one's output: standard MSL's packages, then MSL Enhanced's.)
            var passes = runtime.Passes(packages);
            string input = game.BaseData;
            using var log = new StreamWriter(Path.Combine(game.Dotnet, "msl-patch.log"), false) { AutoFlush = true };
            for (int i = 0; i < passes.Count; i++)
            {
                if (passes[i].Packages.Count == 0)
                    continue;
                string passWork = Path.Combine(work, "pass" + i);
                Directory.CreateDirectory(passWork);
                input = RunPass(game, passes[i], input, passWork, audio, log, metadata);
            }
            PatcherConsole.Log("MSL: Checking patched game data...");
            using var stream = File.OpenRead(input);
            return UndertaleModLib.UndertaleIO.Read(stream);
        }
        finally { Directory.Delete(work, recursive: true); }
    }

    // One run of the MSL helper: its packages, on its runtime, applied to input - the patched data's path.
    private static string RunPass(GameFolder game, SmlPass pass, string input, string work, SmlAudioFiles audio,
        StreamWriter log, Dictionary<string, SmlMetadata> metadata)
    {
        var runtime = pass.Runtime;
        string host = Path.Combine(runtime.Stage(work), "StoneForge.MslHost.exe");
        if (runtime.Enhanced) audio.StageTo(work);
        var copies = new List<string>();
        foreach (var package in pass.Packages)
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
        File.WriteAllText(request, JsonSerializer.Serialize(new { Input = input, Output = output, Packages = copies, Metadata = details, Enhanced = runtime.Enhanced }));
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = work, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(request);
        PatcherConsole.Log($"MSL: Using {(runtime.Enhanced ? "MSL Enhanced" : "standard MSL")}. Preparing {copies.Count} package(s). Detailed log: dotnet/msl-patch.log");
        PatcherConsole.Log("MSL: Large packages can take a long time (up to an hour); the game starts once it's done.");
        log.WriteLine($"==== {(runtime.Enhanced ? "MSL Enhanced" : "standard MSL")}: {string.Join(", ", pass.Packages.Select(p => p.Name))}");
        var elapsed = Stopwatch.StartNew();
        // (The helper dies with us, however we end - the console window closed, killed: it never runs on orphaned.)
        using var job = new KillOnExitJob();
        using var process = Process.Start(start) ?? throw new IOException("Could not start MSL helper.");
        if (!job.Add(process))
            PatcherConsole.Log("MSL: (couldn't tie the helper to the patcher - if this window is closed, end StoneForge.MslHost.exe yourself)");
        // Drain both pipes concurrently, keeping live console output and the complete diagnostic log.
        object gate = new();
        // Progress: the last time the helper wrote a line or used CPU. No time limit while it makes progress -
        // patching can take an hour - only a stall (neither, for StallLimit) ends it.
        long lastProgress = 0, lastHeartbeat = 0;
        var lastCpu = TimeSpan.Zero;
        Task Pump(StreamReader reader) => Task.Run(async () =>
        {
            while (await reader.ReadLineAsync() is { } line)
                lock (gate)
                {
                    log.WriteLine(line);
                    PatcherConsole.Log(line);
                    lastProgress = elapsed.ElapsedMilliseconds;
                }
        });
        var stdout = Pump(process.StandardOutput);
        var stderr = Pump(process.StandardError);
        while (!process.WaitForExit(1000))
        {
            var cpu = CpuTime(process);
            lock (gate)
            {
                long now = elapsed.ElapsedMilliseconds;
                // (More than a stalled process's idle trickle: it's working, if quietly.)
                if (cpu - lastCpu >= CpuProgress)
                {
                    lastCpu = cpu;
                    lastProgress = now;
                }
                if (now - lastProgress >= StallLimit.TotalMilliseconds)
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit();
                    Task.WaitAll(stdout, stderr);
                    throw new TimeoutException($"MSL stopped making progress (no output and no CPU use for {StallLimit.TotalMinutes:0} minutes, after {Hms(elapsed.Elapsed)}). Game data was not replaced. See dotnet/msl-patch.log.");
                }
                if (now - lastHeartbeat >= 10_000 && now - lastProgress >= 10_000)
                {
                    PatcherConsole.Log($"MSL: Still working ({Hms(elapsed.Elapsed)} elapsed, {cpu.TotalSeconds:0}s CPU)...");
                    lastHeartbeat = now;
                }
            }
        }
        Task.WaitAll(stdout, stderr);
        if (process.ExitCode != 0) throw new InvalidOperationException("MSL patching failed. Game data was not replaced. See dotnet/msl-patch.log.");
        if (runtime.Enhanced) audio.Collect(work);
        foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, SmlMetadata>>(File.ReadAllText(details)) ?? new())
            metadata[pair.Key] = pair.Value;
        return output;
    }
}
