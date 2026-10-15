using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace StoneForge.Patcher;

/// <summary>run --update: before the game starts, StoneForge's latest release on GitHub installed over this one, if it's
/// newer. Only within the same minor version (0.9.x to a later 0.9.x): a new minor version can change the API mods are
/// built against, so it's only announced. Asked at most once an hour. Whatever goes wrong - offline, GitHub slow, a
/// failed install - the game starts with what's installed.</summary>
internal static class Updater
{
    private const string LatestRelease = "https://api.github.com/repos/StoneForgeTeam/StoneForge/releases/latest";
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);
    private static readonly TimeSpan AskTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);
    // What a file this patcher had in use was renamed to, for the new one to be copied in: deleted at the next start.
    private const string Aside = ".old";

    public static void Run(GameFolder game)
    {
        try
        {
            Update(game);
        }
        catch (Exception e)
        {
            PatcherConsole.Show();
            PatcherConsole.Log("Couldn't update StoneForge: " + e.Message + "\nThe game starts with the StoneForge installed.");
        }
    }

    /// <summary>What an update renamed out of the way (files this patcher had in use) - gone, now nothing has them.</summary>
    public static void RemoveLeftovers(GameFolder game)
    {
        if (!Directory.Exists(game.PatcherDir))
            return;
        foreach (string file in Directory.GetFiles(game.PatcherDir, "*" + Aside, SearchOption.AllDirectories))
            try { File.Delete(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static void Update(GameFolder game)
    {
        string checkedAt = Path.Combine(game.Dotnet, "stoneforge-update-check.txt");
        if (File.Exists(checkedAt) && DateTime.UtcNow - File.GetLastWriteTimeUtc(checkedAt) < CheckEvery)
            return;
        File.WriteAllText(checkedAt, DateTime.UtcNow.ToString("o"));

        Version installed = Installed();
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("StoneForge-Patcher/" + installed);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var release = JsonDocument.Parse(Ask(http, LatestRelease, AskTimeout));
        string tag = release.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest) || latest <= installed)
            return;

        PatcherConsole.Show();
        string page = release.RootElement.TryGetProperty("html_url", out var url) ? url.GetString() ?? "" : "";
        if (latest.Major != installed.Major || latest.Minor != installed.Minor)
        {
            PatcherConsole.Log($"StoneForge {Short(latest)} is out (you have {Short(installed)}). It may need your mods updated too,");
            PatcherConsole.Log($"so it isn't installed automatically - get it from {page}");
            return;
        }

        string asset = $"StoneForge-{tag.TrimStart('v', 'V')}.zip";
        string? download = null;
        foreach (var file in release.RootElement.GetProperty("assets").EnumerateArray())
            if (string.Equals(file.GetProperty("name").GetString(), asset, StringComparison.OrdinalIgnoreCase))
                download = file.GetProperty("browser_download_url").GetString();
        if (download == null)
            throw new InvalidOperationException($"release {tag} has no {asset}");

        PatcherConsole.Log($"Updating StoneForge {Short(installed)} to {Short(latest)}...");
        string work = Path.Combine(Path.GetTempPath(), "StoneForge-update", Short(latest));
        if (Directory.Exists(work))
            Directory.Delete(work, recursive: true);
        Directory.CreateDirectory(work);
        string zip = Path.Combine(work, asset);
        using (var response = http.Send(new HttpRequestMessage(HttpMethod.Get, download), Cancel(DownloadTimeout)))
        {
            response.EnsureSuccessStatusCode();
            using var to = File.Create(zip);
            response.Content.ReadAsStream().CopyTo(to);
        }
        string extracted = Path.Combine(work, "release");
        ZipFile.ExtractToDirectory(zip, extracted);
        string patcher = Path.Combine(extracted, "files", "dotnet", "patcher", "StoneForge.Patcher.exe");
        if (!File.Exists(patcher))
            throw new InvalidOperationException($"{asset} has no installer in it");

        // The new release's own patcher installs it (this one's files are being replaced): what this one has in use,
        // renamed out of the way first - put back if the install fails before copying the new ones in.
        var moved = MoveInUseAside(game);
        int exit = Install(patcher, game);
        if (exit != 0)
        {
            PutBack(moved);
            throw new InvalidOperationException($"the installer stopped (exit code {exit})");
        }
        PatcherConsole.Log($"StoneForge {Short(latest)} installed.");
        try { Directory.Delete(work, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    private static Version Installed()
    {
        string version = typeof(Updater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";
        return Version.TryParse(version.Split('-')[0], out Version? parsed) ? parsed : new Version(0, 0);
    }

    private static string Short(Version version) => version.ToString(3);

    private static string Ask(HttpClient http, string address, TimeSpan timeout)
    {
        using var response = http.Send(new HttpRequestMessage(HttpMethod.Get, address), Cancel(timeout));
        response.EnsureSuccessStatusCode();
        using var reader = new StreamReader(response.Content.ReadAsStream());
        return reader.ReadToEnd();
    }

    private static CancellationToken Cancel(TimeSpan after) => new CancellationTokenSource(after).Token;

    // Windows lets a running exe and its loaded DLLs be renamed, not overwritten: each the installer couldn't replace,
    // renamed.
    private static List<(string From, string To)> MoveInUseAside(GameFolder game)
    {
        var moved = new List<(string, string)>();
        if (!Directory.Exists(game.PatcherDir))
            return moved;
        foreach (string file in Directory.GetFiles(game.PatcherDir, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(Aside, StringComparison.OrdinalIgnoreCase) || !InUse(file))
                continue;
            string aside = file + Aside;
            if (File.Exists(aside))
                File.Delete(aside);
            File.Move(file, aside);
            moved.Add((file, aside));
        }
        return moved;
    }

    private static bool InUse(string file)
    {
        try
        {
            using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return true; }
    }

    private static void PutBack(List<(string From, string To)> moved)
    {
        foreach (var (from, to) in moved)
            if (!File.Exists(from))
                File.Move(to, from);
    }

    // The installer in this console, nobody asked to press a key (its input closed).
    private static int Install(string patcher, GameFolder game)
    {
        var start = new ProcessStartInfo(patcher) { UseShellExecute = false, RedirectStandardInput = true, WorkingDirectory = Path.GetDirectoryName(patcher)! };
        start.ArgumentList.Add("install");
        start.ArgumentList.Add(game.Dir);
        using var process = Process.Start(start)!;
        process.StandardInput.Close();
        process.WaitForExit();
        return process.ExitCode;
    }
}
