namespace StoneForge.Patcher;

/// <summary>The patcher's output: a console window, opened only when there's something to show (prepare runs
/// silently at start-up, unless it has to rebuild the game data).</summary>
internal static class PatcherConsole
{
    private static bool _shown;

    public static void Show()
    {
        if (_shown)
            return;
        _shown = true;
        if (Win32.GetConsoleWindow() == IntPtr.Zero)
            Win32.AllocConsole();
        Console.Title = "StoneForge";
    }

    public static void Log(string line)
    {
        if (_shown)
            Console.WriteLine(line);
    }

    /// <summary>Waits for a key - if there's anyone to press one (not when the input is redirected).</summary>
    public static void WaitForKey()
    {
        if (Console.IsInputRedirected)
            return;
        try { Console.ReadKey(true); }
        catch (InvalidOperationException) { }
    }
}
