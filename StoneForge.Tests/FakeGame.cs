using System.Runtime.InteropServices;
using StoneForge;
using StoneForge.Loader;

/// <summary>A stand-in for the native bridge: the game's builtins and variables answered in C#, so the loader
/// runs without the game. A test class deriving from it has the game "running" on its test's thread.</summary>
public abstract unsafe class FakeGame : IDisposable
{
    // (The bridge's function table lives in native memory, as the real one does.)
    private static readonly BridgeApi* Api = Create();

    protected static bool Alive = true, Fail, ReplaceFails;
    protected static int Reads, Adds, Replaces;
    protected static readonly List<string> Calls = new();
    // Consumable instances for cleanup tests: instance id -> object index (null: not modelled).
    protected static Dictionary<int, int>? ConsumableInstances;
    // Sprites' sizes for UI tests: sprite -> width, height (others are 0 by 0).
    protected static readonly Dictionary<int, (double Width, double Height)> SpriteSizes = new();

    protected FakeGame()
    {
        Game.Api = Api;
        Game.MarkGameThread();
        Game.Running = true;
        Alive = true;
        Fail = ReplaceFails = false;
    }

    public virtual void Dispose()
    {
        Game.Api = null;
        Game.Running = false;
        Hooks.Faulted = null;
        ConsumableInstances = null;
        SpriteSizes.Clear();
    }

    private static BridgeApi* Create()
    {
        var api = (BridgeApi*)NativeMemory.AllocZeroed((nuint)sizeof(BridgeApi));
        *api = new BridgeApi { Size = sizeof(BridgeApi), Version = BridgeApi.ExpectedVersion,
            Log = &Log, CallBuiltin = &Call, CallScript = &Call, GetVar = &Get, SetVar = &Set,
            HookCode = &Hook, InstanceFromId = &Resolve, LastError = &Error, InstanceId = &Id };
        return api;
    }

    [UnmanagedCallersOnly] private static void Log(byte* text) { }
    [UnmanagedCallersOnly] private static byte* Error() => null;
    [UnmanagedCallersOnly] private static int Hook(byte* name) => 1;
    [UnmanagedCallersOnly] private static IntPtr Resolve(int id) => IntPtr.Zero;
    [UnmanagedCallersOnly] private static int Id(IntPtr ptr) => ptr == (IntPtr)42 ? 123 : -1;
    [UnmanagedCallersOnly] private static int Get(IntPtr ptr, byte* name, NValue* result)
    { Reads++; *result = new NValue { Kind = 0, Real = 55 }; return 1; }
    [UnmanagedCallersOnly] private static int Set(IntPtr ptr, byte* name, NValue* value) => 1;
    [UnmanagedCallersOnly] private static int Call(byte* name, IntPtr self, IntPtr other, NValue* args, int count, NValue* result)
    {
        string function = Marshal.PtrToStringUTF8((IntPtr)name)!;
        Calls.Add(function);
        *result = new NValue { Kind = 0 };
        if (ConsumableInstances is { } instances)
        {
            int arg = count > 0 ? (int)args[0].Real : -1;
            switch (function)
            {
                case "instance_number": result->Real = instances.Values.Count(v => v == arg); return 1;
                case "instance_find": result->Kind = 15; result->Real = instances.Where(p => p.Value == arg).ElementAt((int)args[1].Real).Key; return 1;
                case "instance_exists": result->Real = instances.ContainsKey(arg) ? 1 : 0; return 1;
                case "variable_instance_get": result->Real = instances.GetValueOrDefault(arg, -1); return 1;
                case "script_execute": instances.Remove((int)args[1].Real); return 1;
                case "instance_destroy": instances.Remove(arg); return 1;
            }
        }
        if (Fail || function == "sprite_replace" && ReplaceFails) return 0;
        switch (function)
        {
            case "instance_exists": result->Kind = 13; result->Real = Alive ? 1 : 0; break;
            case "sprite_exists": result->Kind = 13; result->Real = 1; break;
            case "variable_instance_get": result->Real = 55; break;
            case "asset_get_index": result->Real = 1; break;
            case "sprite_add": Adds++; result->Real = 99; break;
            case "sprite_replace": Replaces++; result->Real = args[0].Real; break;
            case "sprite_get_width": result->Real = SpriteSizes.GetValueOrDefault((int)args[0].Real).Width; break;
            case "sprite_get_height": result->Real = SpriteSizes.GetValueOrDefault((int)args[0].Real).Height; break;
            // (Scripts run as script_execute(index, ...); a sprite asked about - scr_adaptiveMenusGetSprite, the
            // Settings menu's for every resolution - is answered with itself.)
            case "script_execute" when count > 1 && SpriteSizes.ContainsKey((int)args[1].Real): result->Real = args[1].Real; break;
        }
        return 1;
    }
}
