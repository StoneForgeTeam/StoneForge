using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
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
    // The game's ds_maps and ds_lists, for the DsMap / DsList tests (null: not modelled).
    protected static FakeDs? Ds;
    // The game's random generator, for the seeded random tests (null: not modelled).
    protected static FakeRandom? Rng;
    // The game's scripts, for the script hook tests (null: not modelled).
    protected static FakeScripts? GameScripts;
    // What's on screen, for the busy / cutscene tests (null: not modelled).
    protected static FakeScene? Scene;
    // The game's arrays and structs, for the game value JSON tests (null: not modelled).
    protected static FakeRefs? Refs;
    // Global variables read by name (any not here reads as the number 55, as before).
    protected static readonly Dictionary<string, GmValue> Globals = new();

    /// <summary>The game's arrays and structs, as the bridge hands them to C#: by an id (kind 7 array, 8 struct), with
    /// the game's pointer standing for which one it is. An element or member that's itself an array or struct is kept by
    /// its id, as the game keeps a reference.</summary>
    protected sealed class FakeRefs
    {
        // (An element: a plain value, or an array / struct by id.)
        private readonly record struct Item(GmValue Plain, int Ref, bool IsArray);

        private readonly Dictionary<int, List<Item>> _arrays = new();
        private readonly Dictionary<int, List<KeyValuePair<string, Item>>> _structs = new();
        public readonly HashSet<int> Methods = new();
        private int _next = 1;
        private readonly List<IntPtr> _strings = new();

        public int NewArray() { _arrays[_next] = new(); return _next++; }
        public int NewStruct() { _structs[_next] = new(); return _next++; }
        public void Push(int array, GmValue value) => _arrays[array].Add(Keep(value));
        public void PushRef(int array, int reference, bool isArray) => _arrays[array].Add(new Item(default, reference, isArray));
        public void SetMember(int strukt, string name, GmValue value) => Put(strukt, name, Keep(value));
        public void SetRef(int strukt, string name, int reference, bool isArray) => Put(strukt, name, new Item(default, reference, isArray));
        public GmValue Array(int id) => Game.FromNative(new NValue { Kind = 7, Real = id, Ptr = id });
        public GmValue Struct(int id) => Game.FromNative(new NValue { Kind = 8, Real = id, Ptr = id });
        public int Count(int array) => _arrays[array].Count;

        private void Put(int strukt, string name, Item item)
        {
            var members = _structs[strukt];
            int at = members.FindIndex(m => m.Key == name);
            if (at >= 0)
                members[at] = new(name, item);
            else
                members.Add(new(name, item));
        }

        // An array or struct handed in is kept by its id: the C# handle may be let go of after.
        private static Item Keep(GmValue value) => value.Kind switch
        {
            GmKind.Array => new Item(default, (int)value.AsArray!.Id, true),
            GmKind.Struct => new Item(default, (int)value.AsStruct!.Id, false),
            _ => new Item(value, 0, false),
        };

        private NValue Out(Item item) => item.Ref == 0 ? Game.ToNative(item.Plain, _strings)
            : new NValue { Kind = item.IsArray ? 7 : 8, Real = item.Ref, Ptr = item.Ref };

        // A builtin, as the game answers it (false: not one modelled here).
        internal bool Answer(string function, NValue* args, int count, NValue* result)
        {
            int id = count > 0 ? (int)args[0].Real : -1;
            GmValue A(int i) => Game.FromNative(args[i]);
            switch (function)
            {
                case "array_create":
                    int made = NewArray();
                    for (int i = 0; i < (int)args[0].Real; i++)
                        _arrays[made].Add(count > 1 ? Keep(A(1)) : default);
                    *result = new NValue { Kind = 7, Real = made, Ptr = made };
                    return true;
                case "array_length": *result = new NValue { Kind = 0, Real = _arrays[id].Count }; return true;
                case "array_get": *result = Out(_arrays[id][(int)args[1].Real]); return true;
                case "array_set":
                    var items = _arrays[id];
                    while (items.Count <= (int)args[1].Real)
                        items.Add(default);
                    items[(int)args[1].Real] = Keep(A(2));
                    *result = new NValue { Kind = 5 };
                    return true;
                case "array_push":
                    for (int i = 1; i < count; i++)
                        _arrays[id].Add(Keep(A(i)));
                    *result = new NValue { Kind = 5 };
                    return true;
                case "json_parse" when Marshal.PtrToStringUTF8(args[0].Str) == "{}":
                    int strukt = NewStruct();
                    *result = new NValue { Kind = 8, Real = strukt, Ptr = strukt };
                    return true;
                case "variable_struct_get":
                    string name = A(1).AsString;
                    *result = _structs[id].FirstOrDefault(m => m.Key == name) is { Key: not null } found ? Out(found.Value) : new NValue { Kind = 5 };
                    return true;
                case "variable_struct_set":
                    Put(id, A(1).AsString, Keep(A(2)));
                    *result = new NValue { Kind = 5 };
                    return true;
                case "variable_struct_get_names":
                    int names = NewArray();
                    foreach (var member in _structs[id])
                        _arrays[names].Add(new Item(member.Key, 0, false));
                    *result = new NValue { Kind = 7, Real = names, Ptr = names };
                    return true;
                case "variable_struct_names_count": *result = new NValue { Kind = 0, Real = _structs[id].Count }; return true;
                case "is_method": *result = new NValue { Kind = 13, Real = args[0].Kind == 8 && Methods.Contains(id) ? 1 : 0 }; return true;
                default: return false;
            }
        }
    }

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

    /// <summary>The game's ds_maps and ds_lists, as GameMaker keeps them: maps and lists numbered apart, each key or element
    /// marked as a nested map or list or not. As the game does (seen by the reliability probe): ds_map_delete and
    /// ds_list_delete leave a nested one undestroyed, a mark moves with its element, ds_list_replace clears it, and
    /// json_decode makes a map of anything. ds_map_set keeps a key's mark (the worst it could do).</summary>
    protected sealed class FakeDs
    {
        public sealed class Slot
        {
            public GmValue Value;
            public int Mark;
        }

        public readonly Dictionary<int, List<KeyValuePair<GmValue, Slot>>> Maps = new();
        public readonly Dictionary<int, List<Slot>> Lists = new();
        private int _nextMap, _nextList;
        // (Strings handed back to the API: kept, as the game keeps its own.)
        private readonly List<IntPtr> _strings = new();

        public int NewMap() { Maps[_nextMap] = new(); return _nextMap++; }
        public int NewList() { Lists[_nextList] = new(); return _nextList++; }

        private Slot? Find(int map, GmValue key) => Maps[map].FirstOrDefault(p => p.Key == key).Value;

        public void DestroyMap(int id)
        {
            if (!Maps.Remove(id, out var map))
                return;
            foreach (var (_, slot) in map)
                DestroyNested(slot);
        }

        public void DestroyList(int id)
        {
            if (!Lists.Remove(id, out var list))
                return;
            foreach (var slot in list)
                DestroyNested(slot);
        }

        private void DestroyNested(Slot slot)
        {
            if (slot.Mark == 1)
                DestroyMap(slot.Value.AsInt);
            else if (slot.Mark == 2)
                DestroyList(slot.Value.AsInt);
        }

        public string Encode(int map) => EncodeMap(map).ToJsonString();

        private JsonNode? EncodeSlot(Slot slot) => slot.Mark switch
        {
            1 => EncodeMap(slot.Value.AsInt),
            2 => new JsonArray(Lists[slot.Value.AsInt].Select(EncodeSlot).ToArray()),
            _ => slot.Value.Kind switch
            {
                GmKind.Real => JsonValue.Create(slot.Value.AsReal),
                GmKind.Bool => JsonValue.Create(slot.Value.AsBool),
                GmKind.Undefined => null,
                _ => JsonValue.Create(slot.Value.AsString),
            },
        };

        private JsonObject EncodeMap(int map)
        {
            var o = new JsonObject();
            foreach (var (key, slot) in Maps[map])
                o[key.AsString] = EncodeSlot(slot);
            return o;
        }

        // (The game's: an object's a map, anything else goes in a map's "default" key; a map, empty, if it isn't JSON.)
        public int Decode(string json)
        {
            JsonNode? node;
            try { node = JsonNode.Parse(json); }
            catch (System.Text.Json.JsonException) { return NewMap(); }
            if (node is JsonObject)
                return Read(node).Value.AsInt;
            int map = NewMap();
            Maps[map].Add(new("default", Read(node)));
            return map;
        }

        private Slot Read(JsonNode? node)
        {
            switch (node)
            {
                case JsonObject o:
                    int map = NewMap();
                    foreach (var (key, value) in o)
                        Maps[map].Add(new(key, Read(value)));
                    return new Slot { Value = map, Mark = 1 };
                case JsonArray a:
                    int list = NewList();
                    foreach (var value in a)
                        Lists[list].Add(Read(value));
                    return new Slot { Value = list, Mark = 2 };
                case JsonValue v when v.TryGetValue(out bool b):
                    return new Slot { Value = b };
                case JsonValue v when v.TryGetValue(out string? text):
                    return new Slot { Value = text };
                case JsonValue v:
                    return new Slot { Value = v.GetValue<double>() };
                default:
                    return new Slot();
            }
        }

        // A builtin, as the game answers it (false: not one modelled here).
        internal bool Answer(string function, NValue* args, int count, NValue* result)
        {
            var a = new GmValue[count];
            for (int i = 0; i < count; i++)
                a[i] = Game.FromNative(args[i]);
            int id = count > 0 ? a[0].AsInt : -1;
            GmValue answer = GmValue.Undefined;
            switch (function)
            {
                case "ds_exists": answer = a[1].AsInt == 1 ? Maps.ContainsKey(id) : a[1].AsInt == 2 && Lists.ContainsKey(id); break;
                case "ds_map_create": answer = NewMap(); break;
                case "ds_map_destroy": DestroyMap(id); break;
                case "ds_map_size": answer = Maps[id].Count; break;
                case "ds_map_exists": answer = Find(id, a[1]) != null; break;
                case "ds_map_find_value": answer = Find(id, a[1])?.Value ?? GmValue.Undefined; break;
                case "ds_map_is_map": answer = Find(id, a[1])?.Mark == 1; break;
                case "ds_map_is_list": answer = Find(id, a[1])?.Mark == 2; break;
                case "ds_map_set":
                    if (Find(id, a[1]) is { } set)
                        set.Value = a[2];
                    else
                        Maps[id].Add(new(a[1], new Slot { Value = a[2] }));
                    break;
                case "ds_map_add_map":
                case "ds_map_add_list":
                    if (Find(id, a[1]) == null)
                        Maps[id].Add(new(a[1], new Slot { Value = a[2], Mark = function == "ds_map_add_map" ? 1 : 2 }));
                    break;
                case "ds_map_delete":
                    int at = Maps[id].FindIndex(p => p.Key == a[1]);
                    if (at >= 0)
                        Maps[id].RemoveAt(at);
                    break;
                case "ds_map_find_first": answer = Maps[id].Count > 0 ? Maps[id][0].Key : GmValue.Undefined; break;
                case "ds_map_find_next":
                    int next = Maps[id].FindIndex(p => p.Key == a[1]) + 1;
                    answer = next > 0 && next < Maps[id].Count ? Maps[id][next].Key : GmValue.Undefined;
                    break;
                case "ds_list_create": answer = NewList(); break;
                case "ds_list_destroy": DestroyList(id); break;
                case "ds_list_size": answer = Lists[id].Count; break;
                case "ds_list_find_value": answer = a[1].AsInt < Lists[id].Count ? Lists[id][a[1].AsInt].Value : GmValue.Undefined; break;
                case "ds_list_is_map": answer = Lists[id][a[1].AsInt].Mark == 1; break;
                case "ds_list_is_list": answer = Lists[id][a[1].AsInt].Mark == 2; break;
                case "ds_list_add": Lists[id].Add(new Slot { Value = a[1] }); break;
                case "ds_list_insert": Lists[id].Insert(a[1].AsInt, new Slot { Value = a[2] }); break;
                case "ds_list_replace": Lists[id][a[1].AsInt] = new Slot { Value = a[2] }; break;
                case "ds_list_delete": Lists[id].RemoveAt(a[1].AsInt); break;
                case "ds_list_mark_as_map": Lists[id][a[1].AsInt].Mark = 1; break;
                case "ds_list_mark_as_list": Lists[id][a[1].AsInt].Mark = 2; break;
                case "json_encode": answer = Encode(id); break;
                case "json_decode": answer = Decode(a[0].AsString); break;
                default: return false;
            }
            *result = Game.ToNative(answer, _strings);
            return true;
        }
    }

    /// <summary>The game's random generator, as GameMaker's: random_get_seed gives the seed it was last set to (not where
    /// it is), and setting a seed starts its sequence over.</summary>
    protected sealed class FakeRandom
    {
        public long Seed;
        public readonly List<long> SeedsSet = new();
        private ulong _state;

        public FakeRandom(long seed) => Set(seed);

        public void Set(long seed)
        {
            Seed = seed;
            SeedsSet.Add(seed);
            _state = (ulong)seed * 6364136223846793005UL + 1442695040888963407UL;
        }

        public long Next(long max)
        {
            _state = _state * 6364136223846793005UL + 1442695040888963407UL;
            return (long)((_state >> 33) % (ulong)(max + 1));
        }

        // A builtin, as the game answers it (false: not one modelled here).
        internal bool Answer(string function, NValue* args, int count, NValue* result)
        {
            switch (function)
            {
                case "random_set_seed": Set((long)args[0].Real); return true;
                case "random_get_seed": result->Real = Seed; return true;
                case "irandom": result->Real = Next((long)args[0].Real); return true;
                case "randomize": Set(Environment.TickCount64 % int.MaxValue); return true;
            }
            return false;
        }
    }

    /// <summary>GML scripts, each a C# body, called with script_execute. A hooked one calls in first, as the patcher's
    /// block at the top of its body does (unless it's <see cref="Unhooked"/>: not hookable in the game data).</summary>
    protected sealed class FakeScripts
    {
        private const int FirstIndex = 5000;
        private readonly List<string> _names = new();
        private readonly Dictionary<string, Func<GmValue[], GmValue>> _bodies = new();
        public readonly HashSet<string> Unhooked = new();
        // How many times each one's own code ran; the self and other of the last run.
        public readonly Dictionary<string, int> Runs = new();
        public IntPtr LastSelf, LastOther;

        public void Add(string name, Func<GmValue[], GmValue> body)
        {
            _names.Add(name);
            _bodies[name] = body;
            Runs[name] = 0;
        }

        internal bool Answer(string function, IntPtr self, IntPtr other, NValue* args, int count, NValue* result)
        {
            if (function == "asset_get_index" && Game.FromNative(args[0]).AsString is var asset && _bodies.ContainsKey(asset))
            {
                result->Real = FirstIndex + _names.IndexOf(asset);
                return true;
            }
            int index = count > 0 ? (int)args[0].Real - FirstIndex : -1;
            if (function != "script_execute" || index < 0 || index >= _names.Count)
                return false;
            string name = _names[index];
            var values = new GmValue[count - 1];
            for (int i = 1; i < count; i++)
                values[i - 1] = Game.FromNative(args[i]);
            GmValue value;
            if (Unhooked.Contains(name) || !Hooks.ScriptCalled(name, new Instance(self), new Instance(other), values, out value))
            {
                Runs[name]++;
                (LastSelf, LastOther) = (self, other);
                value = _bodies[name](values);
            }
            *result = Game.ToNative(value, new List<IntPtr>());
            return true;
        }
    }

    /// <summary>The objects in the room, and scr_is_cutscene's answer - which, as the game's, needs to run as an instance
    /// (it reads object_index): run with none, it fails.</summary>
    protected sealed class FakeScene
    {
        public const int PlayerPointer = 0x600;
        public readonly HashSet<GameObjectId> Present = new();
        public bool Cutscene;
        public int CutsceneChecks;
        public IntPtr CheckedAs;

        internal bool Answer(string function, IntPtr self, NValue* args, int count, NValue* result)
        {
            switch (function)
            {
                case "instance_exists":
                    result->Kind = 13;
                    result->Real = Present.Contains((GameObjectId)(int)args[0].Real) ? 1 : 0;
                    return true;
                case "instance_find":
                    if ((int)args[0].Real == (int)GameObjectId.o_player && Present.Contains(GameObjectId.o_player))
                        *result = new NValue { Kind = 6, Ptr = PlayerPointer };
                    else
                        result->Kind = 5;
                    return true;
                case "asset_get_index": result->Real = 777; return true;
                case "script_execute":
                    CutsceneChecks++;
                    CheckedAs = self;
                    if (self == IntPtr.Zero)
                        return false;
                    result->Kind = 13;
                    result->Real = Cutscene ? 1 : 0;
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
        Ds = null;
        Rng = null;
        GameScripts = null;
        Scene = null;
        Refs = null;
        Globals.Clear();
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
        if (ptr == IntPtr.Zero && Globals.TryGetValue(Marshal.PtrToStringUTF8((IntPtr)name)!, out GmValue global))
        {
            *result = Game.ToNative(global, new List<IntPtr>());
            return 1;
        }
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
        if (GameScripts is { } scripts && scripts.Answer(function, self, other, args, count, result))
            return 1;
        if (Scene is { } scene)
            return scene.Answer(function, self, args, count, result) ? 1 : 0;
        if (Rng is { } rng && rng.Answer(function, args, count, result))
            return 1;
        if (Ds is { } ds && ds.Answer(function, args, count, result))
            return 1;
        if (Refs is { } refs && refs.Answer(function, args, count, result))
            return 1;
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
