namespace StoneForge.Loader;

// Preserve request order (including reload's off/on pair), presenting each enable for two drawn frames
// before its synchronous game-thread Load. Enable all is one batch, with a frame between mods.
internal sealed class RuntimeLoadBatch
{
    private readonly Queue<(string Id, bool On)> _requests;
    private readonly Func<string, string> _name;
    private int _drawn, _loaded;
    internal StartupProgress Progress { get; } = new();

    internal RuntimeLoadBatch(IEnumerable<(string Id, bool On)> requests, Func<string, string> name)
    {
        _requests = new(requests);
        _name = name;
        Progress.Begin(_requests.Count(r => r.On));
        NameNext();
    }

    internal void Drawn() { if (!Progress.Finished) _drawn++; }

    internal void Step(Action<string, bool> change, Func<string, bool> running, Action<string, Exception> failed)
    {
        if (Progress.Finished || _drawn < 2) return;
        while (_requests.TryDequeue(out var request))
        {
            bool success = false;
            try
            {
                change(request.Id, request.On);
                success = !request.On || running(request.Id);
            }
            catch (Exception e) { failed(request.Id, e); }
            if (!request.On) continue;
            if (success) _loaded++; else Progress.Failed++;
            Progress.Done++;
            _drawn = 0;
            NameNext();
            // Trailing disable requests still run, preserving the original queue's order.
            if (!_requests.Any(r => r.On))
            {
                while (_requests.TryDequeue(out var trailing))
                    try { change(trailing.Id, false); }
                    catch (Exception e) { failed(trailing.Id, e); }
                Progress.Finish(_loaded, Progress.Failed);
            }
            return;
        }
        Progress.Finish(_loaded, Progress.Failed);
    }

    private void NameNext()
    {
        if (_requests.FirstOrDefault(r => r.On) is { On: true } next)
            Progress.Loading(_name(next.Id), Progress.Done);
    }
}
