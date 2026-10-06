using System.Runtime.InteropServices;
using System.Text;

namespace StoneForge;

/// <summary>Calls into the game by name: built-in functions, GML scripts, global variables. The typed API
/// (<see cref="Gm"/>, the generated <c>Scripts</c>) is built on this.</summary>
public static unsafe partial class Game
{
    internal static BridgeApi* Api;

    /// <summary>Whether this is the game's native (YYC) build - its GML compiled into the exe - rather than the VM one
    /// (its GML in data.win, run by the runner). On the native build any script can be hooked, and nothing's patched.</summary>
    public static bool IsNative { get; internal set; }

    // The native build: the game's hotkey checks held off (true) while a mod's text box is typed in - the bridge's guard.
    internal static void SetTyping(bool typing)
    {
        if (IsNative && Api != null)
            Api->SetTyping(typing ? 1 : 0);
    }

    // The native build: whether the exe has a compiled function by this name ("gml_Object_o_enemy_Step_0").
    internal static bool HasFunction(string name)
    {
        if (!IsNative || Api == null)
            return false;
        byte* p = Utf8(name);
        try { return Api->HasFunction(p) != 0; }
        finally { NativeMemory.Free(p); }
    }
    private static int _gameThread;
    internal static void MarkGameThread() => _gameThread = Environment.CurrentManagedThreadId;
    internal static void EnsureGameThread()
    {
        if (Api == null || Environment.CurrentManagedThreadId != _gameThread)
            throw new InvalidOperationException("Game access requires the game's thread (a hook, Tick or DrawGui).");
    }

    internal static GameCallException CallFailed(string name)
        => new(name, FromUtf8(Api->LastError()) is { Length: > 0 } error ? error : "the native bridge refused the call");

    /// <summary>Global variables (global.name).</summary>
    public static GlobalVariables Global { get; } = new();

    /// <summary>Calls a GameMaker built-in function (instance_number, show_debug_message...). Those that reach
    /// outside the game (files, network, other programs...) aren't available to mods: they throw
    /// <see cref="UnauthorizedAccessException"/> (files: <see cref="ModContext.Files"/>).</summary>
    public static GmValue CallBuiltin(string name, params GmValue[] args) => CallBuiltinAs(name, default, default, args);

    /// <summary>Calls a built-in with <paramref name="self"/> / <paramref name="other"/> as its instances
    /// (the same limits as <see cref="CallBuiltin"/>).</summary>
    public static GmValue CallBuiltinAs(string name, Instance self, Instance other, params GmValue[] args)
    {
        CheckRunning(name);
        if (!BuiltinPolicy.Allowed(name, out string reason))
            throw new UnauthorizedAccessException(reason);
        return Call(Api->CallBuiltin, name, PointerOf(self), PointerOf(other), args);
    }

    /// <summary>Calls any built-in, those <see cref="CallBuiltin"/> refuses too (files, network, steam_...). For trusted
    /// mods only (mod.json <c>"trusted": true</c>): a mod in StoneForge's sandbox isn't allowed to use it.</summary>
    public static GmValue CallBuiltinUnrestricted(string name, Instance self, Instance other, params GmValue[] args)
    {
        CheckRunning(name);
        return CallBuiltinTrusted(name, self, other, args);
    }

    // An instance as the game holds it - one known by its id (a script's result, instance_find's) found by it:
    // as self or other it must be the instance itself, or the call runs as no instance.
    // Every deactivated instance of the room - id -> object index - in one native walk of the room's inactive list.
    internal static Dictionary<int, int> InactiveInstances()
    {
        EnsureGameThread();
        var found = new Dictionary<int, int>();
        if (Api->InactiveInstances == null)
            return found;
        int count = Api->InactiveInstances(null, null, 0);
        while (count > 0)
        {
            var ids = new int[count];
            var objects = new int[count];
            int total;
            fixed (int* idsPtr = ids, objectsPtr = objects)
                total = Api->InactiveInstances(idsPtr, objectsPtr, count);
            // (More turned up since it was counted: again, with room for them.)
            if (total > count)
            {
                count = total;
                continue;
            }
            for (int i = 0; i < total; i++)
                found.TryAdd(ids[i], objects[i]);
            break;
        }
        return found;
    }

    // A culled (deactivated) instance's pointer, by its id - the bridge looks among the room's deactivated instances too.
    // Zero if it can't be found.
    internal static IntPtr CulledPointer(int id)
    {
        EnsureGameThread();
        return id < 0 ? IntPtr.Zero : Api->InstanceFromId(id);
    }

    internal static IntPtr PointerOf(Instance instance)
    {
        EnsureGameThread();
        if (instance.IsNone) return IntPtr.Zero;
        if (instance.CanUsePointer) return instance.Pointer;
        if (instance.Id < 0)
            throw new InvalidOperationException("This temporary struct/global handle has expired; read it inside its callback.");
        // (A culled one - deactivated, off screen - is still the engine's: its built-ins are read through it.)
        if (!instance.Exists && !Culling.Contains(instance.Id)) throw new InvalidOperationException($"Instance {instance.Id} no longer exists.");
        IntPtr pointer = Api->InstanceFromId(instance.Id);
        return pointer != IntPtr.Zero ? pointer
            : throw new GameCallException("instance lookup", $"could not resolve instance {instance.Id}; refusing to run in global scope");
    }

    /// <summary>Whether the game has started running frames. Before that (while mods load) its state isn't
    /// set up, and calls into it can crash it: mods get an error instead (do it from an event, Frame or Tick).</summary>
    public static bool Running { get; internal set; }

    internal static void CheckRunning(string what)
    {
        EnsureGameThread();
        if (!Running)
            throw new InvalidOperationException($"{what}: the game isn't running yet. Call into the game from an event, Frame or Tick, not from Load.");
    }

    // The loader's own calls: any built-in.
    internal static GmValue CallBuiltinTrusted(string name, Instance self, Instance other, params GmValue[] args)
        => Call(Api->CallBuiltin, name, PointerOf(self), PointerOf(other), args);

    private static readonly Dictionary<string, int> ScriptIndexes = new();

    // (Tests: each fake game numbers its scripts its own way.)
    internal static void ResetScriptIndexesForTests() => ScriptIndexes.Clear();

    /// <summary>Calls a GML script of the game ("scr_atr"), as <paramref name="self"/> (the global scope if
    /// none) - through the game's own script_execute, which works on the bytecode runner. A hooked script's
    /// hooks run too (<see cref="Script.CallOriginal(ScriptCall)"/> skips them).</summary>
    public static GmValue CallScript(string name, Instance self, params GmValue[] args) => CallScript(name, self, self, args);

    // A script with its own self and other. With lent, a self or other the game lent for the call under way goes back as
    // that pointer as it is - the original of a hooked call, whose self may be on its way out (its own Destroy event).
    internal static GmValue CallScript(string name, Instance self, Instance other, GmValue[] args, bool lent = false)
    {
        CheckRunning(name);
        if (!ScriptIndexes.TryGetValue(name, out int index))
        {
            index = CallBuiltin("asset_get_index", name).AsInt;
            ScriptIndexes[name] = index;
        }
        if (index < 0)
            throw new ArgumentException($"No script called {name}");
        var all = new GmValue[args.Length + 1];
        all[0] = index;
        args.CopyTo(all, 1);
        return lent
            ? Call(Api->CallBuiltin, "script_execute", LentPointerOf(self), LentPointerOf(other), all)
            : CallBuiltinTrusted("script_execute", self, other, all);
    }

    private static IntPtr LentPointerOf(Instance instance) => instance.IsLentNow ? instance.Pointer : PointerOf(instance);

    /// <summary>Writes a line to the loader's log (dotnet\bridge.log - a second game running at once: bridge-2.log...).</summary>
    public static void Log(string text)
    {
        if (Api == null) return;
        byte* p = Utf8(text);
        try { Api->Log(p); }
        finally { NativeMemory.Free(p); }
    }

    private static GmValue Call(delegate* unmanaged<byte*, IntPtr, IntPtr, NValue*, int, NValue*, int> fn, string name, IntPtr self, IntPtr other, GmValue[] args)
    {
        EnsureGameThread();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (args.Length > 1024) throw new ArgumentOutOfRangeException(nameof(args), "At most 1024 game arguments are supported.");
        byte* namePtr = Utf8(name);
        var values = stackalloc NValue[Math.Max(1, args.Length)];
        var strings = new List<IntPtr>();
        try
        {
            for (int i = 0; i < args.Length; i++)
                values[i] = ToNative(args[i], strings);
            NValue result = new() { Kind = 5 };
            if (fn(namePtr, self, other, values, args.Length, &result) == 0)
                throw CallFailed(name);
            return FromNative(result);
        }
        finally
        {
            NativeMemory.Free(namePtr);
            foreach (var s in strings)
                NativeMemory.Free((void*)s);
        }
    }

    internal static GmValue GetVar(IntPtr instance, string name)
    {
        EnsureGameThread();
        byte* namePtr = Utf8(name);
        try
        {
            NValue result;
            return Api->GetVar(instance, namePtr, &result) != 0 ? FromNative(result) : GmValue.Undefined;
        }
        finally { NativeMemory.Free(namePtr); }
    }

    internal static bool SetVar(IntPtr instance, string name, GmValue value)
    {
        EnsureGameThread();
        byte* namePtr = Utf8(name);
        var strings = new List<IntPtr>();
        try
        {
            NValue v = ToNative(value, strings);
            return Api->SetVar(instance, namePtr, &v) != 0;
        }
        finally
        {
            NativeMemory.Free(namePtr);
            foreach (var s in strings)
                NativeMemory.Free((void*)s);
        }
    }

    // An element of an instance's indexed engine variable (alarm[n]...); undefined if it can't be read.
    internal static GmValue GetVarAt(Instance instance, string name, int index)
    {
        IntPtr pointer = PointerOf(instance);
        byte* namePtr = Utf8(name);
        try
        {
            NValue result;
            return Api->GetVarAt(pointer, namePtr, index, &result) != 0 ? FromNative(result) : GmValue.Undefined;
        }
        finally { NativeMemory.Free(namePtr); }
    }

    internal static bool SetVarAt(Instance instance, string name, int index, GmValue value)
    {
        IntPtr pointer = PointerOf(instance);
        byte* namePtr = Utf8(name);
        var strings = new List<IntPtr>();
        try
        {
            NValue v = ToNative(value, strings);
            return Api->SetVarAt(pointer, namePtr, index, &v) != 0;
        }
        finally
        {
            NativeMemory.Free(namePtr);
            foreach (var s in strings)
                NativeMemory.Free((void*)s);
        }
    }

    internal static byte* Utf8(string text)
    {
        int length = Encoding.UTF8.GetByteCount(text);
        byte* p = (byte*)NativeMemory.Alloc((nuint)length + 1);
        Encoding.UTF8.GetBytes(text, new Span<byte>(p, length));
        p[length] = 0;
        return p;
    }

    internal static string FromUtf8(byte* p) => p == null ? "" : Marshal.PtrToStringUTF8((IntPtr)p) ?? "";

    internal static NValue ToNative(GmValue value, List<IntPtr> strings)
    {
        var v = new NValue();
        switch (value.Kind)
        {
            case GmKind.Undefined: v.Kind = 5; break;
            case GmKind.Bool: v.Kind = 13; v.Real = value.AsReal; break;
            case GmKind.Real: v.Kind = 0; v.Real = value.AsReal; break;
            case GmKind.String:
                v.Kind = 1;
                var p = (IntPtr)Utf8(value.AsString);
                strings.Add(p);
                v.Str = p;
                break;
            case GmKind.Instance:
                var inst = value.AsInstance;
                // (A room instance goes over as its id - as the game's own code keeps one - even while its pointer is
                // lent: a pointer kept in the game - in a list, a variable - outlives the instance, and reading through
                // it once it's destroyed crashes the game. Only a struct or the global scope goes as its pointer.)
                if (inst.Id >= 0) { v.Kind = 0; v.Real = inst.Id; }
                else if (inst.CanUsePointer) { v.Kind = 6; v.Ptr = inst.Pointer; }
                else if (inst.IsNone) v.Kind = 5;
                else throw new InvalidOperationException("A temporary game handle was used after its callback returned.");
                break;
            case GmKind.Array:
            case GmKind.Struct:
                GmRef reference = value.Kind == GmKind.Array ? value.AsArray! : value.AsStruct!;
                v.Kind = value.Kind == GmKind.Array ? 7 : 8;
                v.Real = reference.Id;
                v.Ptr = reference.Pointer;
                break;
        }
        return v;
    }

    internal static GmValue FromNative(NValue v) => v.Kind switch
    {
        0 => v.Real,
        13 => v.Real != 0,
        1 or 2 => Marshal.PtrToStringUTF8(v.Str),
        6 => new Instance(v.Ptr),
        // (An id of 0: the bridge couldn't keep it.)
        7 => v.Real > 0 ? new GmArray((long)v.Real, v.Ptr) : GmValue.Undefined,
        8 => v.Real > 0 ? new GmStruct((long)v.Real, v.Ptr) : GmValue.Undefined,
        15 => Instance.FromId((int)v.Real),
        _ => GmValue.Undefined,
    };
}
