using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StoneForge;

// Discovery never loads assemblies or evaluates mod metadata: even a constructor is unrestricted code.
internal sealed record SmlPackage(string Id, string Name, string Path, string Hash, bool Enabled = true);
internal static class SmlCatalog
{
    internal const string Warning = "This MSL mod runs unrestricted C# while patching and can access files, the network and other programs. Its game changes require a restart. MSL mods in the mods folder are enabled: only put ones you trust there.";
    internal const string StateFile = "stoneforge-sml.json";
    internal static string Id(string path) => "sml:" + System.IO.Path.GetFileName(path).ToLowerInvariant();

    internal static List<SmlPackage> Read(string mods, string configPath)
    {
        if (!Directory.Exists(mods)) return new();
        var paths = Directory.EnumerateFiles(mods).Where(p => System.IO.Path.GetExtension(p).Equals(".sml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return new();
        // (A package put in the mods folder is enabled - putting it there is the player's say-so - unless it's switched off
        // in the Mods window: mods.json's Disabled list.)
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(configPath))
        {
            // (Malformed settings fail: a package the player switched off must not run because the file broke.)
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            if (document.RootElement.TryGetProperty("Disabled", out var values) && values.ValueKind != JsonValueKind.Null)
                foreach (var value in values.EnumerateArray()) disabled.Add(value.GetString()!);
        }
        return paths.Select(p =>
            {
                string id = Id(p);
                using var stream = File.OpenRead(p);
                return new SmlPackage(id, System.IO.Path.GetFileNameWithoutExtension(p), System.IO.Path.GetFullPath(p),
                    Convert.ToHexString(SHA256.HashData(stream)), !disabled.Contains(id));
            }).ToList();
    }

    internal static string Fingerprint(IEnumerable<SmlPackage> packages) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("\n", packages.Where(p => p.Enabled).Select(p => p.Id + "|" + p.Hash)))));
}

internal sealed record SmlMetadata(string Hash, string Name, string Author, string Version, string Description);
internal sealed record SmlPrepared(string DataStamp, Dictionary<string, string> Applied,
    Dictionary<string, SmlMetadata>? Metadata = null);
