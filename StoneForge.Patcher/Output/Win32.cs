using System.Runtime.InteropServices;

namespace StoneForge.Patcher;

/// <summary>The Windows functions the patcher's console needs.</summary>
internal static class Win32
{
    [DllImport("kernel32")] public static extern bool AllocConsole();
    [DllImport("kernel32")] public static extern IntPtr GetConsoleWindow();
}
