using System.Security.Cryptography;
using System.Text.Json;

namespace StoneForge.Patcher;

internal sealed record SmlRuntimeSelection(bool Enhanced, string? Directory, Dictionary<string, string> Files, string[]? PackageOrder = null)
{
    internal const string ConfigFile = "msl-runtime.json";
    private sealed record Settings(string Mode = "auto", string? EnhancedDirectory = null, string[]? PackageOrder = null);

    internal static SmlRuntimeSelection Select(GameFolder game, IEnumerable<SmlPackage> packages)
    {
        var enabled = packages.Where(p => p.Enabled).ToArray();
        if (enabled.Length == 0) return new(false, null, new());
        string config = Path.Combine(game.Dotnet, ConfigFile);
        var settings = File.Exists(config) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(config),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("Invalid " + ConfigFile) : new Settings();
        string mode = settings.Mode?.ToLowerInvariant() ?? "";
        if (mode is not ("auto" or "standard" or "enhanced")) throw new InvalidDataException("msl-runtime.json Mode must be auto, standard or enhanced.");
        if (settings.PackageOrder is { } order && (order.Any(n => string.IsNullOrWhiteSpace(n) || Path.GetFileName(n) != n || !n.EndsWith(".sml", StringComparison.OrdinalIgnoreCase)) ||
            order.Distinct(StringComparer.OrdinalIgnoreCase).Count() != order.Length))
            throw new InvalidDataException("msl-runtime.json PackageOrder must contain unique .sml filenames.");
        string baseline = Path.Combine(SmlPatches.HostDirectory, "ModShardLauncher.dll");
        bool required = enabled.Any(p => SmlPackageInspection.RequiresEnhanced(SmlPackageInspection.Read(p.Path), baseline));
        if (required && mode == "standard") throw new InvalidOperationException("An enabled package requires MSL Enhanced, but msl-runtime.json selects standard MSL.");
        if (!required && mode != "enhanced") return new(false, null, new(), settings.PackageOrder);
        string directory = Path.GetFullPath(settings.EnhancedDirectory ?? "msle", game.Dotnet);
        if (!System.IO.Directory.Exists(directory)) throw new DirectoryNotFoundException("MSL Enhanced is required. Set EnhancedDirectory in dotnet/msl-runtime.json to your MSLE installation, or copy it to dotnet/msle.");
        // Pin the supplied fork: same version numbers do not guarantee the adapter's API or headless behavior.
        var expected = new Dictionary<string, string>
        {
            ["ModShardLauncher.dll"] = "26168221007AD5F0ADEF8B9474D8D8263FA477FD50F3622AB543560FDBCA1D4E",
            ["UndertaleModLib.dll"] = "EBFC4AC77ABABE4BAB27FCB717DA6DBC23D246F8E0FD7A08CC63AF13D53604A8",
            ["UndertaleModTool.dll"] = "3E90401CCFBE4257F193ECA65315F29A5765B953F66A31991721A773F6BBF745"
        };
        foreach (var pair in expected)
            if (!File.Exists(Path.Combine(directory, pair.Key)) || Hash(Path.Combine(directory, pair.Key)) != pair.Value)
                throw new InvalidDataException("Unsupported MSL Enhanced build or missing dependency: " + pair.Key + ". Use the tested September 17, 2026 build.");
        var files = System.IO.Directory.EnumerateFiles(directory, "*.dll")
            .Where(p => Path.GetFileName(p).StartsWith("ModShard", StringComparison.Ordinal) ||
                Path.GetFileName(p).StartsWith("Undertale", StringComparison.Ordinal) ||
                Path.GetFileName(p).StartsWith("Magick.NET", StringComparison.Ordinal) ||
                Path.GetFileName(p) is "ICSharpCode.SharpZipLib.dll" or "XamlAnimatedGif.dll")
            .Concat(new[] { "win", "win-x64" }.Select(rid => Path.Combine(directory, "runtimes", rid))
                .Where(System.IO.Directory.Exists).SelectMany(p => System.IO.Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)))
            .OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => Path.GetRelativePath(directory, p), Hash);
        return new(true, directory, files, settings.PackageOrder);
    }

    internal string Key => (Enhanced ? "enhanced|" : "standard|") + string.Join("|", Files.Select(p => p.Key + ":" + p.Value))
        + "|order:" + string.Join(",", PackageOrder ?? Array.Empty<string>());
    internal List<SmlPackage> OrderPackages(IEnumerable<SmlPackage> packages)
    {
        var positions = (PackageOrder ?? Array.Empty<string>()).Select((name, index) => (name, index))
            .ToDictionary(p => p.name, p => p.index, StringComparer.OrdinalIgnoreCase);
        return packages.OrderBy(p => positions.GetValueOrDefault(Path.GetFileName(p.Path), int.MaxValue))
            .ThenBy(p => p.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }
    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    internal string Stage(string work)
    {
        if (!Enhanced) return SmlPatches.HostDirectory;
        string runtime = Path.Combine(work, "runtime");
        foreach (string source in System.IO.Directory.EnumerateFiles(SmlPatches.HostDirectory, "*", SearchOption.AllDirectories))
            Copy(source, Path.Combine(runtime, Path.GetRelativePath(SmlPatches.HostDirectory, source)));
        foreach (var pair in Files)
        {
            string target = Path.Combine(runtime, pair.Key);
            Copy(Path.Combine(Directory!, pair.Key), target);
            if (Hash(target) != pair.Value) throw new IOException("MSL Enhanced dependency changed during preparation: " + pair.Key);
        }
        return runtime;
    }

    private static void Copy(string source, string target)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
    }
}

