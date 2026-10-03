using System.Runtime.InteropServices;

namespace StoneForge;

// The C-style API the native StoneForge.Bridge (an Aurie module over YYToolkit) gives us, and the callbacks we
// give it. Layouts match StoneForge.Bridge's ModuleMain.cpp exactly.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct BridgeApi
{
    internal const int ExpectedVersion = 3;
    public int Size;
    public int Version;
    public delegate* unmanaged<byte*, void> Log;
    public delegate* unmanaged<byte*, IntPtr, IntPtr, NValue*, int, NValue*, int> CallBuiltin;
    public delegate* unmanaged<byte*, IntPtr, IntPtr, NValue*, int, NValue*, int> CallScript;
    public delegate* unmanaged<IntPtr, byte*, NValue*, int> GetVar;
    public delegate* unmanaged<IntPtr, byte*, NValue*, int> SetVar;
    public delegate* unmanaged<byte*, int> HookCode;
    public delegate* unmanaged<int, IntPtr> InstanceFromId;
    public delegate* unmanaged<byte*> LastError;
    // Returns an instance id, or -1 for a struct/global. Only called on freshly lent pointers.
    public delegate* unmanaged<IntPtr, int> InstanceId;
    // Lets go of references (arrays and structs: GmRef) C# no longer holds.
    public delegate* unmanaged<long*, int, void> ReleaseRefs;
}
