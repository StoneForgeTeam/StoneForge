using System.Runtime.InteropServices;

namespace StoneForge.Patcher;

/// <summary>The Windows functions the patcher's console needs.</summary>
internal static class Win32
{
    [DllImport("kernel32")] public static extern bool AllocConsole();
    // (ATTACH_PARENT_PROCESS: the console of the process that started us - a terminal's.)
    public const int AttachParentProcess = -1;
    [DllImport("kernel32")] public static extern bool AttachConsole(int processId);
    [DllImport("kernel32")] public static extern IntPtr GetConsoleWindow();
}
