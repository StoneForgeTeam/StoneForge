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
    // A room with culling, for the culled-instance tests (null: not modelled).
    protected static FakeWorld? World;

    /// <summary>A room: instances (id -> object), which are active, object parents, and one culling controller whose
    /// deactivatedInstancesList holds the culled ones' ids - as the game keeps them.</summary>
    protected sealed class FakeWorld
    {
        public const int ControllerId = 900, ListId = 500, PointerBase = 1_000_000;
        public readonly Dictionary<int, int> Objects = new();
        public readonly HashSet<int> Active = new();
        public readonly Dictionary<int, int> Parents = new();
        public readonly List<int> Culled = new();
        public readonly List<int> Destroyed = new();
        // Instances whose pointer the bridge can't find (as before it looked among the room's deactivated ones).
        public readonly HashSet<int> Unresolvable = new();
        public double CachedSize;

        public FakeWorld()
        {
            Objects[ControllerId] = (int)GameObjectId.o_cullingController;
            Active.Add(ControllerId);
        }

        public void Add(int id, int obj, bool culled = false)
        {
            Objects[id] = obj;
            if (culled)
                Culled.Add(id);
            else
                Active.Add(id);
            CachedSize = Culled.Count;
        }

        public bool IsA(int obj, int of)
        {
            for (int o = obj, guard = 0; guard < 32; guard++)
            {
                if (o == of)
                    return true;
                if (!Parents.TryGetValue(o, out o))
                    return false;
            }
            return false;
        }

        // A builtin, as the game answers it in this room (false: not one modelled here).
        internal bool Answer(string function, NValue* args, int count, NValue* result)
        {
            double A(int i) => i < count ? args[i].Real : -1;
            int arg = (int)A(0);
            switch (function)
            {
                case "instance_number": result->Real = Active.Count(i => IsA(Objects[i], arg)); return true;
                case "instance_find":
                    result->Kind = 15;
                    result->Real = Active.Where(i => IsA(Objects[i], arg)).OrderBy(i => i).ElementAt((int)A(1));
                    return true;
                case "instance_exists": result->Kind = 13; result->Real = Active.Contains(arg) ? 1 : 0; return true;
                case "object_is_ancestor": result->Kind = 13; result->Real = arg != (int)A(1) && IsA(arg, (int)A(1)) ? 1 : 0; return true;
                case "ds_exists": result->Kind = 13; result->Real = arg == ListId ? 1 : 0; return true;
                case "ds_list_size": result->Real = Culled.Count; return true;
                case "ds_list_find_value": result->Kind = 15; result->Real = Culled[(int)A(1)]; return true;
                case "ds_list_delete": Culled.RemoveAt((int)A(1)); return true;
                case "instance_activate_object": Active.Add(arg); Culled.Remove(arg); return true;
                // (The game's: an instance that's deactivated isn't found, and nothing happens.)
                case "instance_destroy":
                    if (Active.Remove(arg))
                        Destroyed.Add(arg);
                    return true;
                case "variable_instance_get":
                    result->Real = arg == ControllerId ? ListId : -1;
                    return true;
                case "variable_instance_set":
                    if (arg == ControllerId)
                        CachedSize = args[2].Real;
                    return true;
            }
            return false;
        }
    }

    protected FakeGame()
    {
        Game.Api = Api;
        Game.MarkGameThread();
        Game.Running = true;
        Alive = true;
        Fail = ReplaceFails = false;
        Culling.Reset();
    }

    public virtual void Dispose()
    {
        Game.Api = null;
        Game.Running = false;
        Hooks.Faulted = null;
        ConsumableInstances = null;
        World = null;
    }

    private static BridgeApi* Create()
    {
        var api = (BridgeApi*)NativeMemory.AllocZeroed((nuint)sizeof(BridgeApi));
        *api = new BridgeApi { Size = sizeof(BridgeApi), Version = BridgeApi.ExpectedVersion,
            Log = &Log, CallBuiltin = &Call, CallScript = &Call, GetVar = &Get, SetVar = &Set,
            HookCode = &Hook, InstanceFromId = &Resolve, LastError = &Error, InstanceId = &Id,
            InactiveInstances = &Inactive };
        return api;
    }

    [UnmanagedCallersOnly] private static void Log(byte* text) { }
    [UnmanagedCallersOnly] private static byte* Error() => null;
    [UnmanagedCallersOnly] private static int Hook(byte* name) => 1;
    [UnmanagedCallersOnly] private static IntPtr Resolve(int id) => World is { } world && world.Objects.ContainsKey(id) && !world.Unresolvable.Contains(id) ? (IntPtr)(FakeWorld.PointerBase + id) : IntPtr.Zero;
    // The room's deactivated instances: the world's culled ones (those the bridge can find).
    [UnmanagedCallersOnly] private static int Inactive(int* ids, int* objects, int capacity)
    {
        if (World is not { } world)
            return 0;
        var culled = world.Culled.Where(id => !world.Unresolvable.Contains(id)).ToList();
        for (int i = 0; i < culled.Count && i < capacity && ids != null; i++)
        {
            ids[i] = culled[i];
            objects[i] = world.Objects[culled[i]];
        }
        return culled.Count;
    }
    [UnmanagedCallersOnly] private static int Id(IntPtr ptr) => ptr == (IntPtr)42 ? 123 : -1;
    [UnmanagedCallersOnly] private static int Get(IntPtr ptr, byte* name, NValue* result)
    {
        Reads++;
        // (A world instance's built-in, read through its pointer - a culled one's object_index.)
        if (World is { } world && (long)ptr >= FakeWorld.PointerBase
            && world.Objects.TryGetValue((int)((long)ptr - FakeWorld.PointerBase), out int obj))
        {
            *result = Marshal.PtrToStringUTF8((IntPtr)name) == "object_index" ? new NValue { Kind = 0, Real = obj } : new NValue { Kind = 5 };
            return 1;
        }
        *result = new NValue { Kind = 0, Real = 55 };
        return 1;
    }
    [UnmanagedCallersOnly] private static int Set(IntPtr ptr, byte* name, NValue* value) => 1;
    [UnmanagedCallersOnly] private static int Call(byte* name, IntPtr self, IntPtr other, NValue* args, int count, NValue* result)
    {
        string function = Marshal.PtrToStringUTF8((IntPtr)name)!;
        Calls.Add(function);
        *result = new NValue { Kind = 0 };
        if (World is { } world && world.Answer(function, args, count, result))
            return 1;
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
        }
        return 1;
    }
}
