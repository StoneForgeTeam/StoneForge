using System.Runtime.InteropServices;

namespace StoneForge;

// Windows the player sees when C# goes wrong, with its stack trace (the game's own crashes the bridge shows itself):
// - A mod's handler threw, and StoneForge caught it: shown the first time for each mod and place, on a thread of its
//   own - the game goes on behind it (the mod is paused if it keeps failing, as ever).
// - An exception nothing caught: the game is about to close - shown, and written to dotnet\crash-report.txt, first.
// Only in the game: the loader switches them on (never in tests), unless stoneforge.json's "errorWindows" is false.
internal static class ErrorWindows
{
    private const uint IconError = 0x10, IconWarning = 0x30, TopMost = 0x40000, Foreground = 0x10000;

    private static readonly HashSet<(string Mod, string Where)> Shown = new();

    internal static bool Enabled { get; set; }

    /// <summary>A mod's handler threw (caught: the game goes on).</summary>
    internal static void ModFailed(string mod, string where, Exception e)
    {
        if (!Enabled || !Shown.Add((mod, where)))
            return;
        string text = $"{mod} threw in {where}:\r\n\r\n{e}\r\n\r\nThe game goes on. Later errors from this go to dotnet\\bridge.log only. "
            + "Ctrl+C copies this message.";
        var thread = new Thread(() => Show(text, $"StoneForge - {mod} error", IconWarning)) { IsBackground = true, Name = "StoneForge error window" };
        thread.Start();
    }

    /// <summary>An exception nothing caught: the game closes after this. Written to the crash report, then shown (the
    /// call returns once the player has closed the window).</summary>
    internal static void Fatal(Exception e, string reportPath)
    {
        string text = $"Stoneshard crashed: an exception in C# that nothing caught.\r\n\r\n{e}";
        try { File.WriteAllText(reportPath, text); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (Enabled)
            Show(text + "\r\n\r\nThe report is in dotnet\\crash-report.txt. Ctrl+C copies this message.", "Stoneshard crashed - StoneForge", IconError);
    }

    private static void Show(string text, string caption, uint icon) => MessageBoxW(IntPtr.Zero, text, caption, icon | TopMost | Foreground);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string text, string caption, uint type);
}
