using System.Runtime.InteropServices;

namespace StoneForge;

// The C-style API the native StoneForge.Bridge (an Aurie module over YYToolkit) gives us, and the callbacks we
// give it. Layouts match StoneForge.Bridge's ModuleMain.cpp exactly.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ManagedCallbacks
{
    public int Size;
    public int Version;
    public delegate* unmanaged<void> OnFrame;
    public delegate* unmanaged<byte*, IntPtr, IntPtr, int> OnCodeBefore;
    public delegate* unmanaged<byte*, IntPtr, IntPtr, void> OnCodeAfter;
    public delegate* unmanaged<byte*, IntPtr, IntPtr, NValue*, int, NValue*, int> OnScript;
}
