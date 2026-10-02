using System.Runtime.InteropServices;

namespace StoneForge;

// The C-style API the native StoneForge.Bridge (an Aurie module over YYToolkit) gives us, and the callbacks we
// give it. Layouts match StoneForge.Bridge's ModuleMain.cpp exactly.

/// <summary>A value crossing to and from the game. Kind: 0 real, 1 string (UTF-8), 2 other (its text),
/// 5 undefined, 6 instance / struct (Ptr), 13 bool (Real 0/1), 15 reference (its id in Real).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct NValue
{
    public int Kind;
    public int Pad;
    public double Real;
    public IntPtr Str;
    public IntPtr Ptr;
}
