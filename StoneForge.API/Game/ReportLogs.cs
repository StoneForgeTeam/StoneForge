namespace StoneForge;

internal sealed record ReportLog(string Level, string Message, DateTimeOffset Timestamp);

// Recent managed console output, bounded independently of bridge.log's size. No game calls on worker threads.
internal static class ReportLogs
{
    private static readonly Queue<ReportLog> Entries = new();
    private static readonly object Gate = new();
    internal static void Add(string message)
    {
        string level = message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("failed", StringComparison.OrdinalIgnoreCase) || message.Contains("threw", StringComparison.OrdinalIgnoreCase)
            ? "error" : message.Contains("warn", StringComparison.OrdinalIgnoreCase) ? "warn" : "info";
        lock (Gate)
        {
            Entries.Enqueue(new(level, message[..Math.Min(message.Length, 512)], DateTimeOffset.UtcNow));
            while (Entries.Count > 100) Entries.Dequeue();
        }
    }
    internal static ReportLog[] Snapshot() { lock (Gate) return Entries.ToArray(); }
}
