using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StoneForge.Loader;

// Local friction, not a replacement for authentication/rate limiting at BugDrop.
// Reserve before HTTP: failed/ambiguous requests count too, so retries cannot flood the service.
internal sealed class ReportLimiter(string path, Func<DateTimeOffset>? clock = null)
{
    internal sealed record Attempt(string Hash, DateTimeOffset At);
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);
    private static string Hash(string repo, string title, string description)
    {
        string Normalize(string text) => Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            Normalize(repo) + "\n" + Normalize(title) + "\n" + Normalize(description))));
    }
    internal void MarkRejected(string repo, string title, string description)
    {
        lock (_gate)
        {
            try
            {
                using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var attempts = JsonSerializer.Deserialize<List<Attempt>>(File.ReadAllText(path)) ?? throw new InvalidDataException();
                string hash = Hash(repo, title, description);
                for (int i = 0; i < attempts.Count; i++)
                    if (attempts[i].Hash == hash) attempts[i] = attempts[i] with { Hash = "" };
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(attempts));
                File.Move(path + ".tmp", path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { /* Keep the stricter duplicate block if the saved state cannot be updated. */ }
        }
    }
    internal string? Reserve(string repo, string title, string description)
    {
        lock (_gate)
        {
            try
            {
                var now = _clock();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                // Another Stoneshard process must not reserve against the same old snapshot.
                using var fileLock = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                if (File.Exists(path) && new FileInfo(path).Length > 16384) return "report.limit_storage";
                var attempts = File.Exists(path) ? JsonSerializer.Deserialize<List<Attempt>>(File.ReadAllText(path))
                    ?? throw new InvalidDataException() : new List<Attempt>();
                attempts.RemoveAll(a => a.At < now.AddDays(-1));
                if (attempts.Any(a => a.At > now.AddMinutes(1))) return "report.limit_clock";
                string hash = Hash(repo, title, description);
                if (attempts.Any(a => a.Hash == hash)) return "report.duplicate";
                if (attempts.Any(a => a.At > now.AddMinutes(-10))) return "report.cooldown";
                if (attempts.Count(a => a.At > now.AddHours(-1)) >= 3 || attempts.Count >= 10) return "report.rate_limit";
                attempts.Add(new(hash, now));
                string temporary = path + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(attempts));
                File.Move(temporary, path, overwrite: true);
                return null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            { return "report.limit_storage"; }
        }
    }
}
