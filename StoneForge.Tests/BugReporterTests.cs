using System.Net;
using System.Net.Http;
using System.Text.Json;
using StoneForge;
using StoneForge.Loader;

public sealed class BugReporterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "stoneforge-reporter-" + Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private ReportLimiter Limiter() => new(Path.Combine(_folder, "limits.json"), () => _now);
    private static readonly ReportTarget Target = new("examplemod", "Example", "1.2.3", "Owner/Repo");
    private const string Title = "Missing button";
    private const string Description = "Open the menu and its button is missing.";
    private static object Payload() => BugReporter.CreatePayload(Target, Title, Description, [], 1920, 1080, "en-US");
    public void Dispose() { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }

    [Theory]
    [InlineData("Owner/Repo", "Owner/Repo")]
    [InlineData(" https://github.com/Owner/Repo/ ", "Owner/Repo")]
    public void Manifest_normalizes_github_and_exposes_it(string input, string expected)
    {
        var manifest = new ModManifest(ModIdentity.ParseManifest(JsonSerializer.Serialize(new { id = "m", name = "M", version = "1", github = input })));
        Assert.Equal(expected, manifest.Github);
    }
    [Theory]
    [InlineData("https://example.com/Owner/Repo")]
    [InlineData("https://github.com/Owner/Repo/issues")]
    [InlineData("https://user@github.com/Owner/Repo")]
    [InlineData("https://github.com/Owner/Repo?q=x")]
    [InlineData("https://github.com/Owner/Repo#x")]
    [InlineData("https://github.com:444/Owner/Repo")]
    [InlineData("Owner/..")]
    [InlineData("Owner/Repo/extra")]
    [InlineData("http://github.com/Owner/Repo")]
    [InlineData("")]
    public void Invalid_repository_is_rejected(string input)
    {
        Assert.Throws<InvalidDataException>(() => ModIdentity.ParseManifest(JsonSerializer.Serialize(new { id = "m", name = "M", version = "1", github = input })));
    }
    [Fact]
    public void Payload_has_native_metadata_and_no_images_or_attachments()
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(Payload())); var root = json.RootElement;
        Assert.Equal(Target.Repo, root.GetProperty("repo").GetString());
        Assert.Empty(root.GetProperty("attachments").EnumerateArray());
        Assert.Empty(root.GetProperty("consoleLogs").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("screenshot").ValueKind);
        var metadata = root.GetProperty("metadata");
        Assert.Equal(1920, metadata.GetProperty("viewport").GetProperty("width").GetInt32());
        Assert.Equal(new[] { "appVersion", "url", "timestamp", "browser", "os", "viewport", "devicePixelRatio", "domNodeCount",
            "elementSelector", "fullElementSelector", "fullPageDisabled", "language", "userAgent" }, metadata.EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "repo", "category", "title", "description", "attachments", "consoleLogs", "screenshot", "metadata" }, root.EnumerateObject().Select(p => p.Name));
        Assert.EndsWith("Z", metadata.GetProperty("timestamp").GetString());
        Assert.Equal("StoneForge C# Bug Reporter", metadata.GetProperty("userAgent").GetString());
        Assert.Equal(new[] { "name", "version" }, metadata.GetProperty("browser").EnumerateObject().Select(p => p.Name));
        Assert.Equal("StoneForge " + LoaderVersion.Text, metadata.GetProperty("browser").GetProperty("name").GetString());
        Assert.Equal("", metadata.GetProperty("browser").GetProperty("version").GetString());
        Assert.Equal(new[] { "name", "version" }, metadata.GetProperty("os").EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "width", "height" }, metadata.GetProperty("viewport").EnumerateObject().Select(p => p.Name));
        Assert.Equal("1.2.3", metadata.GetProperty("appVersion").GetString());
        Assert.Equal("en-US", metadata.GetProperty("language").GetString());
        Assert.False(string.IsNullOrEmpty(metadata.GetProperty("os").GetProperty("version").GetString()));
    }
    [Fact]
    public void Cooldown_duplicate_and_hour_limit_survive_new_instances_and_normalize_whitespace()
    {
        Assert.Null(Limiter().Reserve(Target.Repo, Title, Description));
        Assert.Equal("report.duplicate", Limiter().Reserve("owner/repo", " missing   BUTTON ", Description.ToUpperInvariant()));
        Assert.Equal("report.cooldown", Limiter().Reserve(Target.Repo, "Other", Description));
        _now = _now.AddMinutes(1); Assert.Equal("report.cooldown", Limiter().Reserve(Target.Repo, "Other", Description));
        _now = _now.AddMinutes(9); Assert.Null(Limiter().Reserve(Target.Repo, "Other", Description));
        _now = _now.AddMinutes(10); Assert.Null(Limiter().Reserve(Target.Repo, "Third", Description));
        _now = _now.AddMinutes(10); Assert.Equal("report.rate_limit", Limiter().Reserve(Target.Repo, "Fourth", Description));
        _now = _now.AddHours(1); Assert.Null(Limiter().Reserve(Target.Repo, "Fourth", Description));
        Assert.Equal("report.duplicate", Limiter().Reserve(Target.Repo, Title, Description));
        _now = _now.AddDays(1); Assert.Null(Limiter().Reserve(Target.Repo, Title, Description));
    }
    [Fact]
    public void Daily_limit_and_clock_rollback_are_blocked()
    {
        for (int i = 0; i < 10; i++) { Assert.Null(Limiter().Reserve(Target.Repo, "Title " + i, Description)); _now = _now.AddHours(1); }
        Assert.Equal("report.rate_limit", Limiter().Reserve(Target.Repo, "Another", Description));
        _now = _now.AddHours(-12);
        Assert.Equal("report.limit_clock", Limiter().Reserve(Target.Repo, "Another", Description));
    }
    [Fact]
    public void Corrupted_limit_file_does_not_silently_reset_protection()
    {
        Directory.CreateDirectory(_folder); File.WriteAllText(Path.Combine(_folder, "limits.json"), "garbage");
        Assert.Equal("report.limit_storage", Limiter().Reserve(Target.Repo, Title, Description));
    }
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }

    [Fact]
    public void Console_payload_uses_exact_keys_utc_timestamps_and_bounded_utf8_bytes()
    {
        var logs = Enumerable.Range(0, 100).Select(i => new ReportLog("warn", i + new string('界', 512),
            new DateTimeOffset(2026, 10, 10, 2, 30, 0, TimeSpan.Zero))).ToArray();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(BugReporter.CreatePayload(Target, Title, Description, logs, 1920, 1080, "en-US")));
        var entries = json.RootElement.GetProperty("consoleLogs");
        Assert.InRange(entries.GetArrayLength(), 1, 50);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(entries.GetRawText()) <= BugReporter.MaxLogBytes);
        Assert.EndsWith("Z", entries[0].GetProperty("timestamp").GetString());
        Assert.Equal(new[] { "level", "message", "timestamp" }, entries[0].EnumerateObject().Select(p => p.Name));
        Assert.StartsWith("99", BugReporter.PrepareLogs(logs).Last().Message);
    }
    [Fact]
    public async Task Rejection_returns_status_and_service_error_and_allows_retry_after_cooldown()
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        { Content = new StringContent("""{"error":"GitHub App not installed on this repository","code":"APP_NOT_INSTALLED"}""") })));
        var reporter = new BugReporter(client, Limiter());
        var result = await reporter.SendAsync(Target, Title, Description, Payload());
        Assert.Equal("report.app_not_installed", result.MessageKey); Assert.Equal(403, result.HttpStatus);
        Assert.Equal("APP_NOT_INSTALLED", result.ServiceCode); Assert.Contains("not installed", result.ServiceError);
        Assert.Equal("report.cooldown", (await reporter.SendAsync(Target, Title, Description, Payload())).MessageKey);
        _now = _now.AddMinutes(10);
        Assert.Equal("report.app_not_installed", (await reporter.SendAsync(Target, Title, Description, Payload())).MessageKey);
        Assert.Equal(2, JsonSerializer.Deserialize<List<ReportLimiter.Attempt>>(File.ReadAllText(Path.Combine(_folder, "limits.json")))!.Count);
    }
    [Fact]
    public async Task Excessive_payload_is_rejected_locally_without_consuming_an_attempt()
    {
        using var client = new HttpClient(new Handler(_ => throw new InvalidOperationException("Must not send")));
        var reporter = new BugReporter(client, Limiter());
        var result = await reporter.SendAsync(Target, Title, Description, new { huge = new string('x', BugReporter.MaxPayloadBytes) });
        Assert.Equal("report.payload_large", result.MessageKey);
        Assert.False(File.Exists(Path.Combine(_folder, "limits.json")));
    }

    [Theory]
    [InlineData(201, "{\"success\":true}", true, "report.sent")]
    [InlineData(200, "{\"success\":false}", false, "report.failed")]
    [InlineData(200, "{\"error\":\"unavailable\"}", false, "report.failed")]
    [InlineData(429, "", false, "report.server_limit")]
    [InlineData(500, "", false, "report.failed")]
    [InlineData(200, "<html>Bad gateway</html>", false, "report.uncertain")]
    public async Task Submission_posts_expected_json_and_reports_service_result(int status, string body, bool success, string message)
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(async request =>
        {
            calls++; Assert.Equal(HttpMethod.Post, request.Method); Assert.Equal(BugReporter.Endpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var json = JsonDocument.Parse(await request.Content.ReadAsStringAsync());
            Assert.Equal(Target.Repo, json.RootElement.GetProperty("repo").GetString());
            return new((HttpStatusCode)status) { Content = new StringContent(body) };
        }));
        var reporter = new BugReporter(client, Limiter());
        var result = await reporter.SendAsync(Target, Title, Description, Payload());
        Assert.Equal(success, result.Success); Assert.Equal(message, result.MessageKey); Assert.Equal(status, result.HttpStatus);
        Assert.True(result.PayloadBytes > 0);
        Assert.Equal(status == 429 ? "report.cooldown" : "report.duplicate", (await reporter.SendAsync(Target, Title, Description, Payload())).MessageKey);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Concurrent_clicks_do_not_send_multiple_requests()
    {
        var released = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = new HttpClient(new Handler(_ => released.Task));
        var reporter = new BugReporter(client, Limiter());
        var first = reporter.SendAsync(Target, Title, Description, Payload());
        Assert.True(reporter.IsSending);
        Assert.Equal("report.sending", (await reporter.SendAsync(Target, "Other title", Description, Payload())).MessageKey);
        released.SetResult(new(HttpStatusCode.Created) { Content = new StringContent("{}") });
        Assert.True((await first).Success); Assert.False(reporter.IsSending);
    }
    [Fact]
    public async Task Invalid_form_does_not_consume_limits_or_send_and_large_responses_are_bounded()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(_ => { calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(new string('x', 20000)) }); }));
        var reporter = new BugReporter(client, Limiter());
        Assert.Equal("report.invalid", (await reporter.SendAsync(Target, "", Description, Payload())).MessageKey);
        Assert.Equal(0, calls);
        Assert.Equal("report.uncertain", (await reporter.SendAsync(Target, Title, Description, Payload())).MessageKey);
        Assert.Equal(1, calls);
    }
    [Fact]
    public async Task Timeout_keeps_duplicate_protection_and_releases_submission_lock()
    {
        using var client = new HttpClient(new Handler(_ => throw new TaskCanceledException()));
        var reporter = new BugReporter(client, Limiter());
        Assert.Equal("report.uncertain", (await reporter.SendAsync(Target, Title, Description, Payload())).MessageKey);
        Assert.False(reporter.IsSending);
        Assert.Equal("report.duplicate", (await reporter.SendAsync(Target, Title, Description, Payload())).MessageKey);
    }
}
