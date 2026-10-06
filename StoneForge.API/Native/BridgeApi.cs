using System.Runtime.InteropServices;

namespace StoneForge;

// The C-style API the native StoneForge.Bridge (an Aurie module over YYToolkit) gives us, and the callbacks we
// give it. Layouts match StoneForge.Bridge's ModuleMain.cpp exactly.

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct BridgeApi
{
    internal const int ExpectedVersion = 6;
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
    // An element of an instance's indexed engine variable (alarm[n]...).
    public delegate* unmanaged<IntPtr, byte*, int, NValue*, int> GetVarAt;
    public delegate* unmanaged<IntPtr, byte*, int, NValue*, int> SetVarAt;
    // Every deactivated instance of the current room (the game's culling): ids and object indexes into the two
    // buffers, up to the capacity; returns how many there are.
    public delegate* unmanaged<int*, int*, int, int> InactiveInstances;
    // 1 on the game's native (YYC) build - its GML compiled into the exe -, 0 on the VM one.
    public delegate* unmanaged<int> IsNative;
    // The native build: a script hooked (its compiled function detoured) - its calls come to OnScript from now on.
    // 0 if there's no such script (LastError says), or this is the VM build (the patcher hooks scripts there).
    public delegate* unmanaged<byte*, int> HookScript;
}
