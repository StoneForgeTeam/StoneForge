using System.Diagnostics;

namespace StoneForge.Loader;

/// <summary>How loading the mods at start is going (ModManager fills it in; the loading screen shows it).</summary>
internal sealed class StartupProgress
{
    private readonly Stopwatch _clock = new();

    /// <summary>Mod folders to load, and how many are done (loaded, or not loadable).</summary>
    public int Total { get; private set; }
    public int Done { get; set; }
    /// <summary>Folders that didn't compile or load.</summary>
    public int Failed { get; set; }
    /// <summary>The folder being compiled or loaded now.</summary>
    public string? Current { get; private set; }
    public bool Finished { get; private set; }
    /// <summary>Mods loaded, once finished.</summary>
    public int Loaded { get; private set; }
    /// <summary>Seconds since it began (or, finished, how long it took).</summary>
    public double Seconds => _clock.Elapsed.TotalSeconds;
    /// <summary>Seconds since it finished (0 until then).</summary>
    public double SecondsSinceFinished => Finished ? _sinceFinished.Elapsed.TotalSeconds : 0;

    private readonly Stopwatch _sinceFinished = new();

    public void Begin(int total)
    {
        Total = total;
        Done = Failed = Loaded = CurrentIndex = 0;
        Current = null;
        Finished = false;
        _sinceFinished.Reset();
        _clock.Restart();
    }

    /// <summary>Its place among the folders (from 0).</summary>
    public int CurrentIndex { get; private set; }

    public void Loading(string folder, int index)
    {
        Current = folder;
        CurrentIndex = index;
    }

    public void Finish(int loaded, int failed)
    {
        Loaded = loaded;
        Failed = failed;
        Current = null;
        Finished = true;
        _clock.Stop();
        _sinceFinished.Restart();
    }
}
