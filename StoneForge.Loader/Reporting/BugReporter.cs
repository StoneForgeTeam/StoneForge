using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Globalization;

namespace StoneForge.Loader;

internal sealed record ReportTarget(string Id, string Name, string Version, string Repo);
internal sealed record ReportResult(bool Success, string MessageKey, int? HttpStatus = null, string? ServiceCode = null,
    string? ServiceError = null, int PayloadBytes = 0);

internal sealed class BugReporter(HttpClient client, ReportLimiter limiter)
{
    internal const string Endpoint = "https://bugdrop.neonwatty.workers.dev/api/feedback";
    internal static readonly BugReporter Default = new(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(30) }, new ReportLimiter(Path.Combine(
            Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!, "report-limits.json")));
    private int _sending;
    internal bool IsSending => Volatile.Read(ref _sending) != 0;

    internal static ReportTarget[] Targets(ModContext context) => context.Mods.All
        .Where(m => m.Github != null && ModRegistry.All.Any(info => info.Id == m.Id && info.Enabled))
        .Select(m => new ReportTarget(m.Id, m.Name, m.Version, m.Github!))
        .Prepend(new ReportTarget(Hooks.LoaderId, "StoneForge", LoaderVersion.Text, "StoneForgeTeam/StoneForge"))
        .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    // Capture all GML information on the game thread. Only plain JSON values reach SendAsync.
    internal static object Payload(ReportTarget target, string title, string description, bool logs)
    {
        return CreatePayload(target, title, description, logs ? ReportLogs.Snapshot() : [],
            Math.Max(0, Game.CallBuiltin("window_get_width").AsInt), Math.Max(0, Game.CallBuiltin("window_get_height").AsInt),
            Localization.Language);
    }
    internal static object CreatePayload(ReportTarget target, string title, string description, ReportLog[] logs,
        int width, int height, string language) => new
    {
        repo = target.Repo, category = "bug", title = title.Trim(), description = description.Trim(),
        attachments = Array.Empty<object>(), consoleLogs = PrepareLogs(logs).Select(LogJson).ToArray(),
        screenshot = (string?)null,
        metadata = new
        {
            appVersion = target.Version, url = "https://github.com/" + target.Repo, timestamp = Timestamp(DateTimeOffset.UtcNow),
            browser = new { name = "StoneForge " + LoaderVersion.Text, version = "" },
            os = new { name = OperatingSystem.IsWindows() ? "Windows" : System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                version = Environment.OSVersion.Version.ToString() },
            viewport = new { width, height }, devicePixelRatio = 1, domNodeCount = 0,
            elementSelector = (string?)null, fullElementSelector = (string?)null, fullPageDisabled = false,
            language, userAgent = "StoneForge C# Bug Reporter"
        }
    };

    internal const int MaxLogBytes = 8192, MaxPayloadBytes = 65536;
    private static string Timestamp(DateTimeOffset time) => time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static object LogJson(ReportLog log) => new { level = log.Level, message = log.Message, timestamp = Timestamp(log.Timestamp) };
    internal static ReportLog[] PrepareLogs(ReportLog[] logs)
    {
        var result = new List<ReportLog>(); int bytes = 2;
        foreach (var log in logs.Reverse().Take(50))
        {
            string message = log.Message[..Math.Min(log.Message.Length, 512)];
            if (message.Length > 0 && char.IsHighSurrogate(message[^1])) message = message[..^1];
            var entry = log with { Message = message };
            int size = JsonSerializer.SerializeToUtf8Bytes(LogJson(entry)).Length + 1;
            if (bytes + size > MaxLogBytes) break;
            result.Add(entry); bytes += size;
        }
        result.Reverse(); return result.ToArray();
    }

    internal async Task<ReportResult> SendAsync(ReportTarget target, string title, string description, object payload)
    {
        if (!ModIdentity.TryGithubRepository(target.Repo, out _) || title.Trim().Length is < 5 or > 120 ||
            description.Trim().Length is < 20 or > 4000) return new(false, "report.invalid");
        if (Interlocked.CompareExchange(ref _sending, 1, 0) != 0) return new(false, "report.sending");
        int payloadBytes = 0;
        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            payloadBytes = json.Length;
            if (payloadBytes > MaxPayloadBytes) return new(false, "report.payload_large", PayloadBytes: payloadBytes);
            string? blocked = limiter.Reserve(target.Repo, title, description);
            if (blocked != null) return new(false, blocked);
            using var content = new ByteArrayContent(json);
            content.Headers.ContentType = new("application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = content };
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation.Token).ConfigureAwait(false);
            // Some services return a JSON error with a 2xx status. Bound the body before parsing.
            using var stream = await response.Content.ReadAsStreamAsync(cancellation.Token).ConfigureAwait(false);
            byte[] buffer = new byte[16385]; int length = 0;
            while (length < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(length), cancellation.Token).ConfigureAwait(false);
                if (read == 0) break;
                length += read;
            }
            int status = (int)response.StatusCode;
            if (length == buffer.Length) return new(false, "report.uncertain", status, PayloadBytes: payloadBytes);
            string? serviceCode = null, serviceError = null;
            bool rejected = false, invalidJson = false;
            if (length > 0)
            {
                try
                {
                    using var body = JsonDocument.Parse(buffer.AsMemory(0, length));
                    if (body.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        string? Field(string name, int max) => body.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                            ? new string(value.GetString()!.Where(c => !char.IsControl(c)).Take(max).ToArray()) : null;
                        serviceCode = Field("code", 64); serviceError = Field("error", 300);
                        rejected = body.RootElement.TryGetProperty("error", out var error) && error.ValueKind is not (JsonValueKind.Null or JsonValueKind.False) ||
                            body.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False;
                    }
                }
                catch (JsonException) { invalidJson = true; }
            }
            // A definite HTTP rejection did not create an issue. Keep the attempt/cooldown,
            // but allow the same draft to be retried after correcting its cause.
            if (status is 400 or 401 or 403 or 404 or 413 or 422 or 429)
                limiter.MarkRejected(target.Repo, title, description);
            string key = serviceCode switch
            {
                "APP_NOT_INSTALLED" => "report.app_not_installed",
                "AUTH_REQUIRED" => "report.auth_required",
                "REPOSITORY_NOT_ALLOWED" => "report.repo_not_allowed",
                _ => status == 413 ? "report.payload_large" : status == 429 ? "report.server_limit" : "report.failed"
            };
            if (!response.IsSuccessStatusCode || rejected) return new(false, key, status, serviceCode, serviceError, payloadBytes);
            if (invalidJson) return new(false, "report.uncertain", status, PayloadBytes: payloadBytes);
            return new(true, "report.sent", status, PayloadBytes: payloadBytes);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException or IOException)
        { return new(false, "report.uncertain", ServiceError: e is OperationCanceledException ? "Request timed out." : e.Message[..Math.Min(e.Message.Length, 300)], PayloadBytes: payloadBytes); }
        finally { Volatile.Write(ref _sending, 0); }
    }
}
