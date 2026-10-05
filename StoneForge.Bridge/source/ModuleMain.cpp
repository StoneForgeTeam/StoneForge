// StoneForge.Bridge: an Aurie module that runs C# mods inside Stoneshard.
//
// It starts the .NET runtime in the game process (hostfxr, from the installed .NET 10), loads the managed
// loader (<game>\dotnet\StoneForge.Loader.dll) and hands it a small C-style API over YYToolkit:
// log, call built-ins and GML scripts, read and write instance / global variables, subscribe to code
// entries by name (object events: "gml_Object_o_player_Step_0"...). In return the loader gives us its
// callbacks: every frame, and before / after each subscribed code entry (before can skip the original).
// Only subscribed entries cross into .NET - the game runs thousands of code entries a second.
// Log: <game>\dotnet\bridge.log (a second game running at once: bridge-2.log, and so on).
// Crashes: the code entries the game runs are traced as they start and end (a ring of the last 512, and the ones
// running now). When the game itself faults - an access violation in StoneShard.exe - the report goes to
// <game>\dotnet\crash-report.txt (the GML running, innermost first; the native stack; the trace) and a window shows
// it before the game closes.
#include <YYToolkit/YYTK_Shared.hpp>
#include <nethost/hostfxr.h>
#include <nethost/coreclr_delegates.h>
#include <climits>
#include <cstdarg>
#include <cstdio>
#include <fstream>
#include <share.h>
#include <string>
#include <unordered_map>
#include <unordered_set>
using namespace Aurie;
using namespace YYTK;

// A value crossing to and from C#. kind: 0 real, 1 string (UTF-8), 5 undefined, 6 instance / the global scope
// (ptr), 7 array / 8 struct (a reference C# holds: its id in real, the game's pointer in ptr - see References),
// 13 bool (real 0/1), 15 reference (its id in real, raw value in ptr), 2 other (its text in str).
struct NValue
{
	int32_t Kind;
	int32_t Pad;
	double Real;
	const char* Str;
	void* Ptr;
};

struct BridgeApi
{
    int32_t Size;
    int32_t Version;
	void (*Log)(const char* Text);
	int (*CallBuiltin)(const char* Name, void* Self, void* Other, const NValue* Args, int ArgCount, NValue* Result);
	int (*CallScript)(const char* Name, void* Self, void* Other, const NValue* Args, int ArgCount, NValue* Result);
	int (*GetVar)(void* Instance, const char* Name, NValue* Result);
	int (*SetVar)(void* Instance, const char* Name, const NValue* Value);
	int (*HookCode)(const char* CodeName);
	void* (*InstanceFromId)(int Id);
    const char* (*LastError)();
    int (*InstanceId)(void* Instance);
    // Lets go of references (arrays and structs) C# no longer holds.
    void (*ReleaseRefs)(const int64_t* Ids, int Count);
    // An element of an instance's indexed engine variable (alarm[n]...).
    int (*GetVarAt)(void* Instance, const char* Name, int Index, NValue* Result);
    int (*SetVarAt)(void* Instance, const char* Name, int Index, const NValue* Value);
    // Every deactivated instance of the current room (the game's culling): ids and object indexes, in one walk.
    int (*InactiveInstances)(int* Ids, int* Objects, int Capacity);
};

struct ManagedCallbacks
{
    int32_t Size;
    int32_t Version;
	void (*OnFrame)();
	int (*OnCodeBefore)(const char* CodeName, void* Self, void* Other);
	void (*OnCodeAfter)(const char* CodeName, void* Self, void* Other);
	// A hooked script was called (StoneModHooks' block): its name, self / other, arguments. Returns 1 when a
	// mod replaced the call - Result is then what the script returns.
	int (*OnScript)(const char* Name, void* Self, void* Other, const NValue* Args, int ArgCount, NValue* Result);
};

static YYTKInterface* g_Yytk = nullptr;
static std::ofstream g_Log;
static BridgeApi g_Api = {};
static ManagedCallbacks g_Callbacks = {};
static bool g_ManagedReady = false;
static std::unordered_set<std::string> g_HookedNames;
static std::unordered_map<CCode*, bool> g_HookedCache;
static TRoutine g_OriginalStringConcat = nullptr;
static DWORD g_GameThread = 0;
static thread_local std::string t_LastError;
static bool RequireGameThread()
{
    t_LastError.clear();
    if (g_GameThread != 0 && GetCurrentThreadId() == g_GameThread) return true;
    t_LastError = "game access attempted outside the game thread";
    return false;
}
static const char* ApiLastError() { return t_LastError.c_str(); }
static int CallStatus(AurieStatus status, const char* name)
{
    if (AurieSuccess(status)) return 1;
    t_LastError = std::string(name) + " failed (YYToolkit status " + std::to_string(static_cast<int>(status)) + ")";
    return 0;
}
static_assert(sizeof(NValue) == 32, "Managed/native NValue layout mismatch");

static void Log(const std::string& Line)
{
	if (g_Log.is_open())
	{
		g_Log << Line << std::endl;
		g_Log.flush();
	}
}

static CInstance* GlobalInstance()
{
	CInstance* global = nullptr;
	g_Yytk->GetGlobalInstance(&global);
	return global;
}

// ---- references ----

// An array or struct handed to C# becomes a reference C# holds by id: a copy kept here, to hand back when C#
// passes it in again, and rooted in the global struct __stoneforge_refs - GameMaker's garbage collector only keeps
// what the game can reach, and a copy in native memory isn't that. C# lets go of it (ReleaseRefs) when it's done.
// (Through the game's own built-ins: YYToolkit's array and struct access needs layouts this GameMaker version lacks.)
static std::unordered_map<int64_t, RValue> g_Refs;
static int64_t g_NextRef = 1;
static RValue g_RefRoot;

static RValue CallGame(const char* Name, std::vector<RValue> Args)
{
	RValue result;
	g_Yytk->CallBuiltinEx(result, Name, GlobalInstance(), GlobalInstance(), std::move(Args));
	return result;
}

// The root struct, made (again, should the game have lost its globals) as needed.
static bool RefRoot()
{
	RValue current = CallGame("variable_global_get", { RValue(std::string_view("__stoneforge_refs")) });
	if (current.m_Kind == VALUE_OBJECT && g_RefRoot.m_Kind == VALUE_OBJECT && current.m_Pointer == g_RefRoot.m_Pointer)
		return true;
	RValue root = CallGame("json_parse", { RValue(std::string_view("{}")) });
	if (root.m_Kind != VALUE_OBJECT)
		return false;
	CallGame("variable_global_set", { RValue(std::string_view("__stoneforge_refs")), root });
	g_RefRoot = root;
	// (Whatever the old root held went with it.)
	g_Refs.clear();
	return true;
}

static std::string RefKey(int64_t Id) { return "r" + std::to_string(Id); }

static int64_t KeepRef(const RValue& Value)
{
	if (!RefRoot())
		return 0;
	int64_t id = g_NextRef++;
	CallGame("variable_struct_set", { g_RefRoot, RValue(std::string_view(RefKey(id))), Value });
	g_Refs.emplace(id, Value);
	return id;
}

static void ApiReleaseRefs(const int64_t* Ids, int Count)
{
	if (!RequireGameThread() || !Ids)
		return;
	for (int i = 0; i < Count; i++)
	{
		if (g_Refs.erase(Ids[i]) > 0 && g_RefRoot.m_Kind == VALUE_OBJECT)
			CallGame("variable_struct_remove", { g_RefRoot, RValue(std::string_view(RefKey(Ids[i]))) });
	}
}

// ---- values ----

static RValue ToRValue(const NValue& V)
{
	switch (V.Kind)
	{
	case 0: return RValue(V.Real);
	case 1: return RValue(std::string_view(V.Str ? V.Str : ""));
	case 6: return V.Ptr ? RValue(static_cast<CInstance*>(V.Ptr)) : RValue();
	case 7:
	case 8:
	{
		auto found = g_Refs.find(static_cast<int64_t>(V.Real));
		return found != g_Refs.end() ? found->second : RValue();
	}
	case 13: return RValue(V.Real != 0.0);
	default: return RValue();
	}
}

// Strings handed to C# live here until the next call on this thread (C# copies them at once).
static thread_local std::string t_StringOut[8];
static thread_local int t_StringNext = 0;

static void FromRValue(const RValue& R, NValue& Out)
{
	Out = {};
	switch (R.m_Kind)
	{
	case VALUE_REAL:
	case VALUE_INT32:
	case VALUE_INT64:
		Out.Kind = 0;
		Out.Real = R.ToDouble();
		break;
	case VALUE_BOOL:
		Out.Kind = 13;
		Out.Real = R.ToDouble();
		break;
	case VALUE_STRING:
	{
		std::string& slot = t_StringOut[t_StringNext++ % 8];
		slot = R.ToString();
		Out.Kind = 1;
		Out.Str = slot.c_str();
		break;
	}
	case VALUE_OBJECT:
	{
		// An instance (or the global scope) by pointer, as always; any other object - a struct, a method - is a
		// reference C# holds.
		auto* object = static_cast<YYObjectBase*>(R.m_Pointer);
		if (!object || object->m_ObjectKind == OBJECT_KIND_CINSTANCE || R.m_Pointer == GlobalInstance())
		{
			Out.Kind = 6;
			Out.Ptr = R.m_Pointer;
			break;
		}
		Out.Kind = 8;
		Out.Real = static_cast<double>(KeepRef(R));
		Out.Ptr = R.m_Pointer;
		break;
	}
	case VALUE_ARRAY:
		Out.Kind = 7;
		Out.Real = static_cast<double>(KeepRef(R));
		Out.Ptr = R.m_Pointer;
		break;
	case VALUE_UNDEFINED:
		Out.Kind = 5;
		break;
	case VALUE_REF:
		// GameMaker 2022's reference (instance_find, instance_nearest... give these): the id in the low 32
		// bits, its kind (instance, sprite...) above. C# makes it an Instance by id.
		Out.Kind = 15;
		Out.Real = static_cast<double>(static_cast<int32_t>(R.m_i64 & 0xFFFFFFFF));
		Out.Ptr = reinterpret_cast<void*>(R.m_i64);
		break;
	default:
	{
		std::string& slot = t_StringOut[t_StringNext++ % 8];
		slot = R.ToString();
		Out.Kind = 2;
		Out.Str = slot.c_str();
		break;
	}
	}
}

// As FromRValue, but strings kept in Store for as long as the caller needs them (any number of them).
static void FromRValueKept(const RValue& R, NValue& Out, std::vector<std::string>& Store)
{
	if (R.m_Kind == VALUE_STRING)
	{
		Store.push_back(R.ToString());
		Out = {};
		Out.Kind = 1;
		return;
	}
	FromRValue(R, Out);
}

static std::vector<RValue> ToArgs(const NValue* Args, int Count)
{
	std::vector<RValue> args;
	for (int i = 0; i < Count; i++)
		args.push_back(ToRValue(Args[i]));
	return args;
}

// ---- the API C# calls ----

static void ApiLog(const char* Text)
{
	Log(std::string("[C#] ") + (Text ? Text : ""));
}

static int ApiCallBuiltin(const char* Name, void* Self, void* Other, const NValue* Args, int ArgCount, NValue* Result)
{
	*Result = {}; Result->Kind = 5;
	if (!RequireGameThread()) return 0;
	RValue result;
	// (Called with no instance: as the global scope, as GML's own code at global scope is. With none at all,
	// a script run through script_execute reads and writes its variables through a null instance and
	// corrupts the heap.)
	CInstance* self = Self ? static_cast<CInstance*>(Self) : GlobalInstance();
	CInstance* other = Other ? static_cast<CInstance*>(Other) : self;
	AurieStatus st = g_Yytk->CallBuiltinEx(result, Name, self, other, ToArgs(Args, ArgCount));
	FromRValue(result, *Result);
	return CallStatus(st, Name);
}

static int ApiCallScript(const char* Name, void* Self, void* Other, const NValue* Args, int ArgCount, NValue* Result)
{
	*Result = {}; Result->Kind = 5;
	if (!RequireGameThread()) return 0;
	RValue result;
	CInstance* self = Self ? static_cast<CInstance*>(Self) : GlobalInstance();
	CInstance* other = Other ? static_cast<CInstance*>(Other) : self;
	AurieStatus st = g_Yytk->CallGameScriptEx(result, Name, self, other, ToArgs(Args, ArgCount));
	FromRValue(result, *Result);
	return CallStatus(st, Name);
}

// An engine built-in's accessors, as the engine keeps them (YYToolkit's RVariableRoutine, which its shared headers only
// declare - 64-bit layout: name, getter, setter, whether it's settable).
struct BuiltinAccess
{
	const char* Name;
	bool (*Get)(CInstance* Instance, int Index, RValue* Value);
	bool (*Set)(CInstance* Instance, int Index, RValue* Value);
	bool CanBeSet;
};
static_assert(sizeof(BuiltinAccess) == 32, "RVariableRoutine's layout");

// A variable name's built-in accessors (x, y, image_index, alarm, id...), or null if it isn't one. Found once per name
// and kept: YYToolkit's GetBuiltin / SetBuiltin look the name up on every call, the bulk of an alarm's read.
static const BuiltinAccess* Builtin(const char* Name)
{
	static std::unordered_map<std::string, const BuiltinAccess*> cache;
	auto found = cache.find(Name);
	if (found != cache.end())
		return found->second;
	const BuiltinAccess* access = nullptr;
	size_t index = 0;
	RVariableRoutine* routine = nullptr;
	if (AurieSuccess(g_Yytk->GetBuiltinVariableIndex(Name, index))
		&& AurieSuccess(g_Yytk->GetBuiltinVariableInformation(index, routine)) && routine)
		access = reinterpret_cast<const BuiltinAccess*>(routine);
	cache.emplace(Name, access);
	return access;
}

// Whether a variable name is one of the engine's built-ins (x, y, image_index, id...). Those must never go
// through the member lookup: on this runtime it doesn't fail cleanly for them and hands back a bad pointer
// (writing through it corrupted memory).
static bool IsBuiltin(const char* Name) { return Builtin(Name) != nullptr; }

// A built-in read and written through its accessors (as YYToolkit's GetBuiltin / SetBuiltin: a missing getter or
// setter refuses; whether it's "settable" isn't asked).
static bool GetBuiltinValue(const char* Name, CInstance* Instance, int Index, RValue& Value)
{
	const BuiltinAccess* access = Builtin(Name);
	if (!access || !access->Get)
		return false;
	access->Get(Instance, Index, &Value);
	return true;
}

static bool SetBuiltinValue(const char* Name, CInstance* Instance, int Index, RValue& Value)
{
	const BuiltinAccess* access = Builtin(Name);
	if (!access || !access->Set)
		return false;
	access->Set(Instance, Index, &Value);
	return true;
}

// GameMaker's per-instance built-ins: asked for with no instance (as a global), the engine reads them
// from nothing and crashes - refused instead.
static bool IsInstanceBuiltin(const char* Name)
{
	static const std::unordered_set<std::string> names = {
		"x", "y", "xprevious", "yprevious", "xstart", "ystart", "hspeed", "vspeed", "speed", "direction",
		"friction", "gravity", "gravity_direction", "image_index", "image_speed", "image_xscale", "image_yscale",
		"image_angle", "image_alpha", "image_blend", "image_number", "image_single", "sprite_index",
		"sprite_width", "sprite_height", "sprite_xoffset", "sprite_yoffset", "mask_index", "depth", "layer",
		"visible", "solid", "persistent", "object_index", "id", "alarm", "bbox_left", "bbox_right", "bbox_top",
		"bbox_bottom", "path_index", "path_position", "path_positionprevious", "path_speed", "path_scale",
		"path_orientation", "path_endaction", "timeline_index", "timeline_position", "timeline_speed",
		"timeline_running", "timeline_loop", "in_sequence", "sequence_instance", "drawn_by_sequence",
		"managed", "on_ui_layer"
	};
	return names.count(Name) > 0;
}

// Whether an instance (or, with none, global) has a variable. The member lookup must only be asked for
// one that exists: for a missing one it raises a GameMaker error, which unwinds through the .NET frames
// that called us and kills the process.
static bool HasMember(CInstance* Instance, const char* Name)
{
	RValue result;
	if (Instance)
		g_Yytk->CallBuiltinEx(result, "variable_instance_exists", Instance, Instance, { Instance->ToRValue(), RValue(std::string_view(Name)) });
	else
		g_Yytk->CallBuiltinEx(result, "variable_global_exists", GlobalInstance(), GlobalInstance(), { RValue(std::string_view(Name)) });
	return result.ToDouble() > 0.5;
}

// Instance (or global, with no instance) variables: a built-in through the engine's built-in accessors,
// anything else as the object's own variable.
static int ApiGetVar(void* Instance, const char* Name, NValue* Result)
{
	*Result = {}; Result->Kind = 5;
	if (!RequireGameThread()) return 0;
	CInstance* inst = Instance ? static_cast<CInstance*>(Instance) : GlobalInstance();
	*Result = {};
	Result->Kind = 5;
	if (!inst)
		return 0;
	if (IsBuiltin(Name))
	{
		if (!Instance && IsInstanceBuiltin(Name))
			return 0;
		RValue value;
		if (!GetBuiltinValue(Name, Instance ? inst : nullptr, INT_MIN, value))
			return 0;
		FromRValue(value, *Result);
		return 1;
	}
	if (!HasMember(Instance ? inst : nullptr, Name))
		return 0;
	if (RValue* member = inst->GetRefMember(Name))
	{
		FromRValue(*member, *Result);
		return 1;
	}
	return 0;
}

static int ApiSetVar(void* Instance, const char* Name, const NValue* Value)
{
	if (!RequireGameThread()) return 0;
	CInstance* inst = Instance ? static_cast<CInstance*>(Instance) : GlobalInstance();
	if (!inst)
		return 0;
	RValue value = ToRValue(*Value);
	if (IsBuiltin(Name))
	{
		if (!Instance && IsInstanceBuiltin(Name))
			return 0;
		return SetBuiltinValue(Name, Instance ? inst : nullptr, INT_MIN, value) ? 1 : 0;
	}
	RValue* member = HasMember(Instance ? inst : nullptr, Name) ? inst->GetRefMember(Name) : nullptr;
	if (member)
	{
		*member = value;
		return 1;
	}
	// A new variable on the instance (or global): the game's own variable_instance_set / variable_global_set.
	RValue ignored;
	if (Instance)
		return AurieSuccess(g_Yytk->CallBuiltinEx(ignored, "variable_instance_set", nullptr, nullptr, { inst->ToRValue(), RValue(std::string_view(Name)), value })) ? 1 : 0;
	return AurieSuccess(g_Yytk->CallBuiltinEx(ignored, "variable_global_set", nullptr, nullptr, { RValue(std::string_view(Name)), value })) ? 1 : 0;
}

// An element of an instance's indexed engine variable - alarm[n] and the like - through the engine's own accessor with
// that index (the whole-variable access above passes none). Only the engine's built-ins: an array a script keeps in a
// variable of its own is a reference C# reads directly.
static int ApiGetVarAt(void* Instance, const char* Name, int Index, NValue* Result)
{
	*Result = {}; Result->Kind = 5;
	if (!RequireGameThread() || !Instance || Index < 0 || !IsBuiltin(Name))
		return 0;
	RValue value;
	if (!GetBuiltinValue(Name, static_cast<CInstance*>(Instance), Index, value))
		return 0;
	FromRValue(value, *Result);
	return 1;
}

static int ApiSetVarAt(void* Instance, const char* Name, int Index, const NValue* Value)
{
	if (!RequireGameThread() || !Instance || Index < 0 || !IsBuiltin(Name))
		return 0;
	RValue value = ToRValue(*Value);
	return SetBuiltinValue(Name, static_cast<CInstance*>(Instance), Index, value) ? 1 : 0;
}

static int ApiHookCode(const char* CodeName)
{
	g_HookedNames.insert(CodeName);
	g_HookedCache.clear();
	return 1;
}

// A deactivated instance of the current room (instance_deactivate_object: the game's culling of what's off screen): the
// engine's id lookup (GetInstanceObject) only has the active ones, so the room's inactive list is walked for it.
static CInstance* InactiveInstance(int Id)
{
	CRoom* room = nullptr;
	if (!AurieSuccess(g_Yytk->GetCurrentRoomData(room)) || !room)
		return nullptr;
	auto& inactive = room->GetMembers().m_InactiveInstances;
	// (Bounded by the list's own count, should a link ever be stale.)
	int32_t left = inactive.m_Count;
	for (CInstance* inst = inactive.m_First; inst && left-- > 0; inst = inst->GetMembers().m_Flink)
		if (inst->GetMembers().m_ID == Id)
			return inst;
	return nullptr;
}

// Every deactivated instance of the current room - its id and object index - into Ids / Objects, up to Capacity; how
// many there are (more than Capacity: call again with room for them). One walk of the list however many C# needs.
static int ApiInactiveInstances(int* Ids, int* Objects, int Capacity)
{
	if (!RequireGameThread()) return 0;
	CRoom* room = nullptr;
	if (!AurieSuccess(g_Yytk->GetCurrentRoomData(room)) || !room)
		return 0;
	auto& inactive = room->GetMembers().m_InactiveInstances;
	int total = 0;
	int32_t left = inactive.m_Count;
	for (CInstance* inst = inactive.m_First; inst && left-- > 0; inst = inst->GetMembers().m_Flink)
	{
		if (total < Capacity && Ids && Objects)
		{
			Ids[total] = inst->GetMembers().m_ID;
			Objects[total] = inst->GetMembers().m_ObjectIndex;
		}
		total++;
	}
	return total;
}

static void* ApiInstanceFromId(int Id)
{
	if (!RequireGameThread()) return nullptr;
	if (CInstance* active = CInstance::FromInstanceID(Id))
		return active;
	return InactiveInstance(Id);
}

// Only inspect a pointer while the engine is lending it to a callback/call result.
static int ApiInstanceId(void* Instance)
{
    if (!RequireGameThread() || !Instance) return -1;
    auto* object = static_cast<YYObjectBase*>(Instance);
    if (object->m_ObjectKind != OBJECT_KIND_CINSTANCE) return -1;
    RValue id;
    if (!GetBuiltinValue("id", static_cast<CInstance*>(Instance), INT_MIN, id)) return -1;
    return id.m_Kind == VALUE_REF ? static_cast<int32_t>(id.m_i64 & 0xFFFFFFFF) : static_cast<int>(id.ToDouble());
}

static bool InstanceStillExists(int Id)
{
    if (Id < 0) return true;
    RValue result;
    return AurieSuccess(g_Yytk->CallBuiltinEx(result, "instance_exists", GlobalInstance(), GlobalInstance(), { RValue(Id) }))
        && result.ToDouble() > 0.5;
}

// ---- hooked scripts ----

// string_concat, which Stoneshard never calls itself: a hooked script's first block (StoneModHooks) calls it
// as string_concat("__stonemod_script__", "<script>", [its arguments]) - handed to C#; a mod replacing the
// call is answered with [its value], which the block returns. Any other call is the real string_concat.
static void HookedStringConcat(RValue& Result, CInstance* Self, CInstance* Other, int ArgCount, RValue* Args)
{
	g_GameThread = GetCurrentThreadId();
	if (ArgCount >= 3 && Args[0].m_Kind == VALUE_STRING && Args[0].ToString() == "__stonemod_script__")
	{
		Result = RValue();
		if (!g_ManagedReady || !g_Callbacks.OnScript)
			return;
		std::string name = Args[1].ToString();
		// (The arguments array is read with the game's own array_length / array_get: YYToolkit's array access
		// needs an internal layout it can't find on this GameMaker version.)
		std::vector<RValue> args;
		if (Args[2].m_Kind == VALUE_ARRAY)
		{
			RValue length;
			g_Yytk->CallBuiltinEx(length, "array_length", Self, Other, { Args[2] });
			int count = static_cast<int>(length.ToDouble());
			for (int i = 0; i < count; i++)
			{
				RValue element;
				g_Yytk->CallBuiltinEx(element, "array_get", Self, Other, { Args[2], RValue(static_cast<double>(i)) });
				args.push_back(element);
			}
		}
		std::vector<NValue> values(args.size());
		std::vector<std::string> strings;
		strings.reserve(args.size());
		for (size_t i = 0; i < args.size(); i++)
			FromRValueKept(args[i], values[i], strings);
		// (Point the string values at their kept copies now that the store won't move.)
		size_t s = 0;
		for (size_t i = 0; i < args.size(); i++)
			if (args[i].m_Kind == VALUE_STRING)
				values[i].Str = strings[s++].c_str();
		NValue result = {};
		result.Kind = 5;
		if (g_Callbacks.OnScript(name.c_str(), Self, Other, values.data(), static_cast<int>(values.size()), &result))
		{
			// [value], made by the game's array_create(1, value) for the same reason.
			RValue replaced;
			g_Yytk->CallBuiltinEx(replaced, "array_create", Self, Other, { RValue(1.0), ToRValue(result) });
			Result = replaced;
		}
		return;
	}
	g_OriginalStringConcat(Result, Self, Other, ArgCount, Args);
}

static void HookStringConcat(AurieModule* Module)
{
	PVOID routine = nullptr;
	if (!AurieSuccess(g_Yytk->GetNamedRoutinePointer("string_concat", &routine)) || !routine)
	{
		Log("string_concat not found - script hooks unavailable");
		return;
	}
	PVOID trampoline = nullptr;
	if (!AurieSuccess(MmCreateHook(Module, "StoneModScriptHooks", routine, reinterpret_cast<PVOID>(HookedStringConcat), &trampoline)) || !trampoline)
	{
		Log("Hooking string_concat failed - script hooks unavailable");
		return;
	}
	g_OriginalStringConcat = reinterpret_cast<TRoutine>(trampoline);
	Log("Script hooks ready (string_concat)");
}

// ---- crashes ----

struct TraceEntry
{
	CCode* Code;
	int SelfId;
	int Depth;
	bool Start;
};

static constexpr int TraceSize = 512, StackSize = 64, ReportSize = 96 * 1024;
static bool g_TraceOn = false;
static TraceEntry g_Trace[TraceSize];
static long long g_TraceNext = 0;
// The code entries running now, outermost first (deeper than StackSize: counted, not kept).
static TraceEntry g_Running[StackSize];
static int g_TraceDepth = 0;
static std::wstring g_ReportPath;
static const BuiltinAccess* g_IdAccess = nullptr;
// (The report and the window's text: kept off the stack and the heap - either may be what broke.)
static char g_Report[ReportSize];
static wchar_t g_ReportWide[16 * 1024];
static volatile LONG g_Reported = 0;

// An instance's id, through its accessor (looked up once): the trace keeps ids, never pointers - what crashes is
// often an instance that's gone.
static int TraceId(CInstance* Instance)
{
	if (!Instance || !g_IdAccess || !g_IdAccess->Get)
		return -1;
	if (static_cast<YYObjectBase*>(Instance)->m_ObjectKind != OBJECT_KIND_CINSTANCE)
		return -1;
	RValue id;
	g_IdAccess->Get(Instance, INT_MIN, &id);
	return id.m_Kind == VALUE_REF ? static_cast<int32_t>(id.m_i64 & 0xFFFFFFFF) : static_cast<int>(id.ToDouble());
}

static void TraceStart(CCode* Code, int SelfId)
{
	TraceEntry entry = { Code, SelfId, g_TraceDepth, true };
	g_Trace[g_TraceNext++ % TraceSize] = entry;
	if (g_TraceDepth < StackSize)
		g_Running[g_TraceDepth] = entry;
	g_TraceDepth++;
}

static void TraceEnd(CCode* Code, int SelfId)
{
	g_TraceDepth--;
	g_Trace[g_TraceNext++ % TraceSize] = { Code, SelfId, g_TraceDepth, false };
}

// The report so far, appended to (cut short at its size).
static int g_ReportLength = 0;
static void Report(const char* Format, ...)
{
	if (g_ReportLength >= ReportSize - 1)
		return;
	va_list args;
	va_start(args, Format);
	int n = vsnprintf(g_Report + g_ReportLength, ReportSize - g_ReportLength, Format, args);
	va_end(args);
	if (n > 0)
		g_ReportLength = g_ReportLength + n < ReportSize - 1 ? g_ReportLength + n : ReportSize - 1;
}

// Where an address is: its module's file name and the offset in it.
static void ReportAddress(const char* Indent, DWORD64 Address)
{
	HMODULE module = nullptr;
	char path[MAX_PATH] = "?";
	if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
		reinterpret_cast<LPCSTR>(Address), &module) && module)
	{
		GetModuleFileNameA(module, path, MAX_PATH);
		const char* name = strrchr(path, '\\');
		Report("%s%s+0x%llx\r\n", Indent, name ? name + 1 : path, static_cast<unsigned long long>(Address - reinterpret_cast<DWORD64>(module)));
	}
	else
		Report("%s0x%llx\r\n", Indent, static_cast<unsigned long long>(Address));
}

// The native stack at the fault, from its context (x64 unwind data: no symbols needed).
static void ReportNativeStack(const CONTEXT* Fault)
{
	CONTEXT context = *Fault;
	for (int frame = 0; frame < 24 && context.Rip; frame++)
	{
		ReportAddress("  ", context.Rip);
		DWORD64 imageBase = 0;
		PRUNTIME_FUNCTION function = RtlLookupFunctionEntry(context.Rip, &imageBase, nullptr);
		if (!function)
		{
			// (A leaf function: its return address is on top of the stack.)
			if (!context.Rsp)
				break;
			context.Rip = *reinterpret_cast<DWORD64*>(context.Rsp);
			context.Rsp += 8;
			continue;
		}
		PVOID handlerData = nullptr;
		DWORD64 establisher = 0;
		RtlVirtualUnwind(UNW_FLAG_NHANDLER, imageBase, context.Rip, function, &context, &handlerData, &establisher, nullptr);
	}
}

static void ReportEntry(const TraceEntry& Entry, const char* Indent)
{
	const char* name = Entry.Code ? Entry.Code->GetName() : nullptr;
	Report("%s%s (self %d)\r\n", Indent, name ? name : "?", Entry.SelfId);
}

// The game faulted: the report written and shown - once - then the game's own crash handling goes on (it closes).
static LONG CALLBACK OnFault(EXCEPTION_POINTERS* Info)
{
	if (!g_TraceOn || Info->ExceptionRecord->ExceptionCode != EXCEPTION_ACCESS_VIOLATION)
		return EXCEPTION_CONTINUE_SEARCH;
	// (Only the game's own faults: .NET raises and handles access violations of its own.)
	HMODULE game = GetModuleHandleW(nullptr);
	HMODULE at = nullptr;
	if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
		static_cast<LPCWSTR>(Info->ExceptionRecord->ExceptionAddress), &at) || at != game)
		return EXCEPTION_CONTINUE_SEARCH;
	if (InterlockedExchange(&g_Reported, 1) != 0)
		return EXCEPTION_CONTINUE_SEARCH;

	g_ReportLength = 0;
	const ULONG_PTR* info = Info->ExceptionRecord->ExceptionInformation;
	bool hasAddress = Info->ExceptionRecord->NumberParameters > 1;
	Report("Stoneshard crashed: an access violation %s 0x%llx at StoneShard.exe+0x%llx.\r\n\r\n",
		hasAddress && info[0] == 1 ? "writing" : hasAddress && info[0] == 8 ? "executing" : "reading",
		static_cast<unsigned long long>(hasAddress ? info[1] : 0),
		static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(Info->ExceptionRecord->ExceptionAddress) - reinterpret_cast<uintptr_t>(game)));
	Report("GML running, innermost first:\r\n");
	if (g_TraceDepth == 0)
		Report("  (none - between code entries)\r\n");
	if (g_TraceDepth > StackSize)
		Report("  (%d more, deeper)\r\n", g_TraceDepth - StackSize);
	for (int i = (g_TraceDepth < StackSize ? g_TraceDepth : StackSize) - 1; i >= 0; i--)
		ReportEntry(g_Running[i], "  ");
	Report("\r\nNative stack:\r\n");
	ReportNativeStack(Info->ContextRecord);
	int shown = g_ReportLength;
	Report("\r\nThe last code entries, oldest first (> started, < ended; indented by depth):\r\n");
	for (long long i = g_TraceNext > TraceSize ? g_TraceNext - TraceSize : 0; i < g_TraceNext; i++)
	{
		const TraceEntry& e = g_Trace[i % TraceSize];
		char indent[96];
		int width = (e.Depth < 40 ? e.Depth : 40) * 2;
		snprintf(indent, sizeof(indent), "%*s%c ", width, "", e.Start ? '>' : '<');
		ReportEntry(e, indent);
	}

	HANDLE file = CreateFileW(g_ReportPath.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
	if (file != INVALID_HANDLE_VALUE)
	{
		DWORD written = 0;
		WriteFile(file, g_Report, g_ReportLength, &written, nullptr);
		CloseHandle(file);
	}
	// The window: what's shown above the trace, and where the rest is.
	g_ReportLength = shown;
	Report("\r\nThe full report, with the last 512 code entries, is in dotnet\\crash-report.txt. Ctrl+C copies this message.");
	int wide = MultiByteToWideChar(CP_UTF8, 0, g_Report, g_ReportLength, g_ReportWide, static_cast<int>(sizeof(g_ReportWide) / sizeof(wchar_t)) - 1);
	g_ReportWide[wide > 0 ? wide : 0] = L'\0';
	MessageBoxW(nullptr, g_ReportWide, L"Stoneshard crashed - StoneForge", MB_OK | MB_ICONERROR | MB_TOPMOST | MB_SETFOREGROUND);
	return EXCEPTION_CONTINUE_SEARCH;
}

static void StartCrashReports(const fs::path& DotnetDir)
{
	g_IdAccess = Builtin("id");
	g_ReportPath = (DotnetDir / "crash-report.txt").wstring();
	g_TraceOn = AddVectoredExceptionHandler(1, OnFault) != nullptr;
	if (!g_TraceOn)
		Log("Crash reports: couldn't add the fault handler");
}

// ---- game events ----

static void FrameCallback(FWFrame& Context)
{
	g_GameThread = GetCurrentThreadId();
	UNREFERENCED_PARAMETER(Context);
	if (g_ManagedReady && g_Callbacks.OnFrame)
		g_Callbacks.OnFrame();
}

// Whether a mod hooked this code entry (by name, looked up once per entry).
static bool IsHooked(CCode* Code)
{
	if (!g_ManagedReady || g_HookedNames.empty())
		return false;
	auto cached = g_HookedCache.find(Code);
	if (cached != g_HookedCache.end())
		return cached->second;
	const char* name = Code->GetName();
	bool hooked = name && g_HookedNames.count(name) > 0;
	g_HookedCache[Code] = hooked;
	return hooked;
}

// A code entry: handed to C# if a mod hooked it (before can skip the original; after runs either way). One nobody
// hooked is left to the game, which runs it after us - unless RunAnyway (the trace, to see where it ends).
static void RunCode(FWCodeEvent& Context, CInstance* self, CInstance* other, CCode* code, bool RunAnyway)
{
	if (!IsHooked(code))
	{
		if (RunAnyway)
			Context.Call();
		return;
	}
	const char* name = code->GetName();
	const int selfId = ApiInstanceId(self), otherId = ApiInstanceId(other);
	if (g_Callbacks.OnCodeBefore && g_Callbacks.OnCodeBefore(name, self, other))
	{
		// A mod replaced it: the game's own code doesn't run.
		Context.Override(true);
	}
	else
		Context.Call();
	if (g_Callbacks.OnCodeAfter)
		g_Callbacks.OnCodeAfter(name, InstanceStillExists(selfId) ? self : nullptr, InstanceStillExists(otherId) ? other : nullptr);
}

static void CodeCallback(FWCodeEvent& Context)
{
	g_GameThread = GetCurrentThreadId();
	CInstance* self = std::get<0>(Context.Arguments());
	CInstance* other = std::get<1>(Context.Arguments());
	CCode* code = std::get<2>(Context.Arguments());
	if (!code)
		return;
	if (!g_TraceOn)
	{
		RunCode(Context, self, other, code, false);
		return;
	}
	int selfId = TraceId(self);
	TraceStart(code, selfId);
	RunCode(Context, self, other, code, true);
	TraceEnd(code, selfId);
}

// ---- starting .NET ----

// The installed .NET's hostfxr.dll: <dotnet root>\host\fxr\<newest version>\hostfxr.dll. The root is
// DOTNET_ROOT if set, else Program Files\dotnet (where the .NET installers put it).
static fs::path FindHostfxr()
{
	std::vector<fs::path> roots;
	wchar_t env[MAX_PATH] = {};
	if (GetEnvironmentVariableW(L"DOTNET_ROOT", env, MAX_PATH))
		roots.push_back(env);
	wchar_t programFiles[MAX_PATH] = {};
	if (GetEnvironmentVariableW(L"ProgramW6432", programFiles, MAX_PATH) || GetEnvironmentVariableW(L"ProgramFiles", programFiles, MAX_PATH))
		roots.push_back(fs::path(programFiles) / "dotnet");
	for (const fs::path& root : roots)
	{
		fs::path fxr = root / "host" / "fxr";
		std::error_code ec;
		if (!fs::is_directory(fxr, ec))
			continue;
		fs::path best;
		std::vector<int> bestVersion;
		for (const auto& entry : fs::directory_iterator(fxr, ec))
		{
			if (!entry.is_directory() || !fs::exists(entry.path() / "hostfxr.dll"))
				continue;
			// Compare versions numerically, part by part ("10.0.3" over "9.0.1").
			std::vector<int> version;
			std::string name = entry.path().filename().string();
			size_t start = 0;
			while (start <= name.size())
			{
				size_t dot = name.find('.', start);
				version.push_back(atoi(name.substr(start, dot - start).c_str()));
				if (dot == std::string::npos)
					break;
				start = dot + 1;
			}
			if (best.empty() || version > bestVersion)
			{
				best = entry.path() / "hostfxr.dll";
				bestVersion = version;
			}
		}
		if (!best.empty())
			return best;
	}
	return {};
}

static bool StartDotNet(const fs::path& DotnetDir)
{
	fs::path hostfxrPath = FindHostfxr();
	if (hostfxrPath.empty())
	{
		Log("No .NET runtime found - install the .NET 10 Runtime");
		return false;
	}
	Log("Using " + hostfxrPath.string());
	HMODULE hostfxr = LoadLibraryW(hostfxrPath.c_str());
	int rc = 0;
	auto init = reinterpret_cast<hostfxr_initialize_for_runtime_config_fn>(GetProcAddress(hostfxr, "hostfxr_initialize_for_runtime_config"));
	auto getDelegate = reinterpret_cast<hostfxr_get_runtime_delegate_fn>(GetProcAddress(hostfxr, "hostfxr_get_runtime_delegate"));
	auto close = reinterpret_cast<hostfxr_close_fn>(GetProcAddress(hostfxr, "hostfxr_close"));
	if (!init || !getDelegate || !close)
	{
		Log("hostfxr is missing its entry points");
		return false;
	}

	fs::path config = DotnetDir / "StoneForge.Loader.runtimeconfig.json";
	fs::path assembly = DotnetDir / "StoneForge.Loader.dll";
	hostfxr_handle context = nullptr;
	rc = init(config.c_str(), nullptr, &context);
	if (rc != 0 || !context)
	{
		Log("hostfxr_initialize_for_runtime_config failed: " + std::to_string(rc));
		close(context);
		return false;
	}
	load_assembly_and_get_function_pointer_fn loadAssembly = nullptr;
	rc = getDelegate(context, hdt_load_assembly_and_get_function_pointer, reinterpret_cast<void**>(&loadAssembly));
	close(context);
	if (rc != 0 || !loadAssembly)
	{
		Log("hostfxr_get_runtime_delegate failed: " + std::to_string(rc));
		return false;
	}

	using InitializeFn = int (CORECLR_DELEGATE_CALLTYPE*)(BridgeApi*, ManagedCallbacks*);
	InitializeFn initialize = nullptr;
	rc = loadAssembly(assembly.c_str(), L"StoneForge.Loader.Bridge, StoneForge.Loader", L"InitializeV2", UNMANAGEDCALLERSONLY_METHOD, nullptr, reinterpret_cast<void**>(&initialize));
	if (rc != 0 || !initialize)
	{
		Log("Loading StoneForge.Loader.Bridge.InitializeV2 failed; install matching native and managed files: " + std::to_string(rc));
		return false;
	}

	g_Api.Size = sizeof(BridgeApi);
	g_Api.Version = 5;
	g_Callbacks.Size = sizeof(ManagedCallbacks);
	g_Callbacks.Version = 5;
	g_Api.Log = ApiLog;
	g_Api.CallBuiltin = ApiCallBuiltin;
	g_Api.CallScript = ApiCallScript;
	g_Api.GetVar = ApiGetVar;
	g_Api.SetVar = ApiSetVar;
	g_Api.HookCode = ApiHookCode;
	g_Api.InstanceFromId = ApiInstanceFromId;
	g_Api.LastError = ApiLastError;
	g_Api.InstanceId = ApiInstanceId;
	g_Api.ReleaseRefs = ApiReleaseRefs;
	g_Api.GetVarAt = ApiGetVarAt;
	g_Api.SetVarAt = ApiSetVarAt;
	g_Api.InactiveInstances = ApiInactiveInstances;
	rc = initialize(&g_Api, &g_Callbacks);
	Log("StoneForge initialized: " + std::to_string(rc));
	return rc == 0;
}

// Most games running at once that get a log of their own.
static constexpr int MaxLogs = 8;

// The log, <game>\dotnet\bridge.log - started afresh each run, by whichever stage comes first. Each game keeps
// its log closed to other writers while it runs, so a second game running at once (two players on one PC) can't
// open it and takes the next free one instead - bridge-2.log, bridge-3.log... - as Unreal numbers its logs.
static void OpenLog(const fs::path& ModulePath)
{
	if (g_Log.is_open())
		return;
	fs::path dotnetDir = ModulePath.parent_path().parent_path() / "dotnet";
	fs::create_directories(dotnetDir);
	for (int n = 1; n <= MaxLogs; n++)
	{
		fs::path file = dotnetDir / (n == 1 ? std::string("bridge.log") : "bridge-" + std::to_string(n) + ".log");
		// (Others may read it, not write it: one held by a running game fails here, untouched.)
		if (FILE* f = _wfsopen(file.c_str(), L"w", _SH_DENYWR))
		{
			g_Log = std::ofstream(f);
			if (n > 1)
				Log("Another game has bridge.log: this one logs to " + file.filename().string());
			return;
		}
	}
}

// Before the game's own code runs (the process is still suspended): the game data made current for the
// mods - StoneForge.Patcher prepare, which returns at once unless the mods' hooks, the loader's menus or the
// game's data changed (then it rebuilds data.win, in a window of its own). The game reads data.win after this.
EXPORTED AurieStatus ModulePreinitialize(
	IN AurieModule* Module,
	IN const fs::path& ModulePath
)
{
	UNREFERENCED_PARAMETER(Module);
	OpenLog(ModulePath);
	fs::path gameDir = ModulePath.parent_path().parent_path();
	fs::path patcher = gameDir / "dotnet" / "patcher" / "StoneForge.Patcher.exe";
	if (!fs::exists(patcher))
	{
		Log("No " + patcher.string() + " - the game data isn't prepared for mods");
		return AURIE_SUCCESS;
	}
	std::wstring commandLine = L"\"" + patcher.wstring() + L"\" prepare \"" + gameDir.wstring() + L"\"";
	STARTUPINFOW si = { sizeof(si) };
	PROCESS_INFORMATION pi = {};
	// (Detached: no console unless it opens one to show a rebuild.)
	if (!CreateProcessW(patcher.c_str(), commandLine.data(), nullptr, nullptr, FALSE, DETACHED_PROCESS, nullptr, gameDir.c_str(), &si, &pi))
	{
		Log("Couldn't run StoneForge.Patcher prepare: error " + std::to_string(GetLastError()));
		return AURIE_SUCCESS;
	}
	WaitForSingleObject(pi.hProcess, INFINITE);
	DWORD exitCode = 0;
	GetExitCodeProcess(pi.hProcess, &exitCode);
	CloseHandle(pi.hThread);
	CloseHandle(pi.hProcess);
	Log("Game data prepared (StoneForge.Patcher exit " + std::to_string(exitCode) + ")");
	return AURIE_SUCCESS;
}

EXPORTED AurieStatus ModuleInitialize(
	IN AurieModule* Module,
	IN const fs::path& ModulePath
)
{
	// (We sit in <game>\aurie\; the managed side is in <game>\dotnet\.)
	fs::path dotnetDir = ModulePath.parent_path().parent_path() / "dotnet";
	OpenLog(ModulePath);
	Log("StoneForge.Bridge starting, module at " + ModulePath.string());

	g_Yytk = YYTK::GetInterface();
	if (!g_Yytk)
	{
		Log("No YYToolkit interface - is YYToolkit.dll in aurie?");
		return AURIE_MODULE_DEPENDENCY_NOT_RESOLVED;
	}
	if (!AurieSuccess(g_Yytk->CreateCallback(Module, EVENT_FRAME, FrameCallback, 0)) || !AurieSuccess(g_Yytk->CreateCallback(Module, EVENT_OBJECT_CALL, CodeCallback, 0)))
		Log("Registering the game callbacks failed");

	HookStringConcat(Module);
	StartCrashReports(dotnetDir);
	g_ManagedReady = StartDotNet(dotnetDir);
	return AURIE_SUCCESS;
}
