using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace StoneForge.Loader;

// Opt-in, local-user-only diagnostics. All game operations are drained on the game thread.
// Requests are bounded, have deadlines, and never invoke C# source or arbitrary reflection.
internal static class DevelopmentHost
{
    private sealed class Request
    {
        internal required JsonElement Data;
        internal DateTime Deadline = DateTime.UtcNow.AddSeconds(10);
        internal readonly TaskCompletionSource<object> Reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int State; // 0 queued, 1 executing, 2 expired/completed
    }

    private static readonly ConcurrentQueue<Request> Pending = new();
    private static readonly Queue<object> Trace = new();
    private static readonly HashSet<string> Watches = new(StringComparer.Ordinal);
    private const string Owner = "StoneForge.Diagnostics";
    private static bool _started;
    internal static bool Enabled { get; private set; }
    internal static string PipeName => $"stoneforge-{Environment.ProcessId}";

    internal static void Start()
    {
        if (_started) return;
        _started = true;
        string folder = Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!;
        Enabled = Environment.GetEnvironmentVariable("STONEFORGE_TEST") == "1"
            || File.Exists(Path.Combine(folder, "testhost.enable"));
        if (!Enabled) return;
        File.WriteAllText(Path.Combine(folder, "testhost.pipe"), PipeName);
        _ = Task.Run(Serve);
        Game.Log("Development host enabled: " + PipeName);
    }

    private static async Task Serve()
    {
        while (true)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                object response;
                try
                {
                    string line = await ReadBoundedLine(reader, timeout.Token);
                    using var json = JsonDocument.Parse(line);
                    if (Pending.Count >= 32) throw new InvalidOperationException("Development queue is full.");
                    var request = new Request { Data = json.RootElement.Clone() };
                    Pending.Enqueue(request);
                    try { response = await request.Reply.Task.WaitAsync(TimeSpan.FromSeconds(12), timeout.Token); }
                    catch (TimeoutException)
                    {
                        bool expired = Interlocked.CompareExchange(ref request.State, 2, 0) == 0;
                        response = new { ok = false, error = expired ? "Expired before execution." : "Execution started; outcome unknown. Do not blindly retry mutations." };
                    }
                }
                catch (Exception e) { response = new { ok = false, error = e.Message }; }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response).AsMemory(), timeout.Token);
            }
            catch (Exception)
            {
                // Disconnected or idle clients must not stop the host. No game access on this thread.
                await Task.Delay(250);
            }
        }
    }

    private static async Task<string> ReadBoundedLine(StreamReader reader, CancellationToken token)
    {
        var text = new StringBuilder();
        var c = new char[1];
        while (await reader.ReadAsync(c.AsMemory(), token) > 0)
        {
            if (c[0] == '\n') return text.ToString();
            if (text.Length >= 16384) throw new InvalidOperationException("Request exceeds 16 KiB.");
            text.Append(c[0]);
        }
        throw new EndOfStreamException("Expected a newline-terminated JSON request.");
    }

    internal static void Frame()
    {
        if (!Enabled || !ModManager.Startup.Finished) return;
        for (int i = 0; i < 4 && Pending.TryDequeue(out var request); i++)
        {
            if (DateTime.UtcNow > request.Deadline)
            {
                Interlocked.Exchange(ref request.State, 2);
                request.Reply.TrySetResult(new { ok = false, error = "Expired before execution." });
                continue;
            }
            if (Interlocked.CompareExchange(ref request.State, 1, 0) != 0) continue;
            try { request.Reply.TrySetResult(new { ok = true, result = Execute(request.Data) }); }
            catch (Exception e) { request.Reply.TrySetResult(new { ok = false, error = e.Message }); }
            finally { Interlocked.Exchange(ref request.State, 2); }
        }
    }

    private static object Value(GmValue value) => new { kind = value.Kind.ToString(), value = value.ToString() };
    private static string[] Names(JsonElement request) => request.GetProperty("names").EnumerateArray()
        .Take(128).Select(v => v.GetString() ?? "").ToArray();

    internal static object Execute(JsonElement request)
    {
        Game.EnsureGameThread();
        switch (request.GetProperty("cmd").GetString())
        {
            case "status": return new { version = LoaderVersion.Text, mods = ModRegistry.All,
                activeSprites = ModContent.ActiveSprites, retiredSprites = ModContent.RetiredSprites };
            case "mod.reload":
            case "mod.disable":
            {
                // (By its ID, or its name.)
                string given = request.GetProperty("name").GetString()!;
                var mod = ModRegistry.All.FirstOrDefault(m => m.Id == given || m.Name == given) ?? throw new ArgumentException("Unknown mod.");
                if (mod.IsSml) throw new InvalidOperationException("MSL packages cannot be hot-reloaded. Use the Mods window and restart.");
                string name = mod.Id;
                ModManager.Request(name, false);
                if (request.GetProperty("cmd").GetString() == "mod.reload") ModManager.Request(name, true);
                return "Queued for the next frame; inspect status for the outcome.";
            }
            case "inspect":
            {
                var instance = Instance.FromId(request.GetProperty("id").GetInt32());
                if (!instance.Exists) throw new InvalidOperationException("Instance no longer exists.");
                var names = request.TryGetProperty("names", out _) ? Names(request)
                    : new[] { "id", "object_index", "x", "y", "sprite_index", "HP", "MP" };
                return names.Distinct().ToDictionary(n => n, n => Value(instance.Get(n)));
            }
            case "globals": return Names(request).Distinct().ToDictionary(n => n, n => Value(Game.Global[n]));
            case "objects":
            {
                int obj = Game.CallBuiltin("asset_get_index", request.GetProperty("name").GetString()).AsInt;
                if (obj < 0 || !Game.CallBuiltin("object_exists", obj).AsBool) throw new ArgumentException("Unknown object.");
                int count = Game.CallBuiltin("instance_number", obj).AsInt;
                return new { count, instances = Enumerable.Range(0, Math.Clamp(count, 0, 256))
                    .Select(i => Game.CallBuiltin("instance_find", obj, i).AsInstance.Id).ToArray() };
            }
            case "builtin":
            {
                var args = request.TryGetProperty("args", out var values) ? values.EnumerateArray().Select(Argument).ToArray() : Array.Empty<GmValue>();
                return Value(Game.CallBuiltin(request.GetProperty("name").GetString()!, args));
            }
            case "trace.watch":
            {
                string name = request.GetProperty("name").GetString()!;
                // Observe only scripts whose patch already exists. Never set flags for unprepared hooks.
                if (Game.CallBuiltin("asset_get_index", name).AsInt < 0
                    || Game.Global["__smh_" + name].IsUndefined)
                    throw new ArgumentException("Script is not prepared for hooks. Declare it with HookScript and restart first.");
                if (Watches.Count >= 32) throw new InvalidOperationException("At most 32 watches.");
                if (Watches.Add(name)) Hooks.AddScript(Owner, name, call =>
                {
                    if (Trace.Count == 128) Trace.Dequeue();
                    Trace.Enqueue(new { name, self = call.Self.Id, args = call.Args.Select(Value).ToArray() });
                    return false;
                });
                return "Watching input arguments for " + name;
            }
            case "trace.read": return Trace.ToArray();
            case "trace.clear": Trace.Clear(); return "Cleared";
            case "trace.stop": Hooks.RemoveMod(Owner); Watches.Clear(); Trace.Clear(); return "Stopped";
            case "smoke":
            {
                var number = Game.CallBuiltin("abs", -17.25);
                var text = Game.CallBuiltin("string", "StoneForge bridge ✓");
                if (number.AsReal != 17.25 || text.AsString != "StoneForge bridge ✓")
                    throw new InvalidOperationException("Bridge value round-trip failed.");
                return new { numericRoundTrip = true, stringRoundTrip = true, room = Value(Game.Global["room"]) };
            }
            default: throw new ArgumentException("Unknown command. Use status, inspect, globals, objects, builtin, trace.watch/read/clear/stop, smoke.");
        }
    }

    private static GmValue Argument(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(), JsonValueKind.String => value.GetString(),
        JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Null => GmValue.Undefined,
        _ => throw new ArgumentException("Arguments must be numbers, strings, booleans or null.")
    };
}
