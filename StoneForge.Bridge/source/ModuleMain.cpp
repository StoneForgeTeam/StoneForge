// StoneForge.Bridge: an Aurie module that runs C# mods inside Stoneshard.
//
// It starts the .NET runtime in the game process (hostfxr, from the installed .NET 10), loads the managed
// loader (<game>\dotnet\StoneForge.Loader.dll) and hands it a small C-style API over YYToolkit:
// log, call built-ins and GML scripts, read and write instance / global variables, subscribe to code
// entries by name (object events: "gml_Object_o_player_Step_0"...). In return the loader gives us its
// callbacks: every frame, and before / after each subscribed code entry (before can skip the original).
// Only subscribed entries cross into .NET - the game runs thousands of code entries a second.
// Log: <game>\dotnet\bridge.log.
#include <YYToolkit/YYTK_Shared.hpp>
#include <nethost/hostfxr.h>
#include <nethost/coreclr_delegates.h>
#include <climits>
#include <fstream>
#include <string>
#include <unordered_map>
#include <unordered_set>
using namespace Aurie;
using namespace YYTK;

// A value crossing to and from C#. kind: 0 real, 1 string (UTF-8), 5 undefined, 6 instance / struct
// (ptr), 13 bool (real 0/1), 15 reference (its id in real, raw value in ptr), 2 other (its text in str).
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

// ---- values ----

static RValue ToRValue(const NValue& V)
{
	switch (V.Kind)
	{
	case 0: return RValue(V.Real);
	case 1: return RValue(std::string_view(V.Str ? V.Str : ""));
	case 6: return V.Ptr ? RValue(static_cast<CInstance*>(V.Ptr)) : RValue();
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
		Out.Kind = 6;
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

static CInstance* GlobalInstance()
{
	CInstance* global = nullptr;
	g_Yytk->GetGlobalInstance(&global);
	return global;
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

// Whether a variable name is one of the engine's built-ins (x, y, image_index, id...). Those must never go
// through the member lookup: on this runtime it doesn't fail cleanly for them and hands back a bad pointer
// (writing through it corrupted memory). Cached per name.
static bool IsBuiltin(const char* Name)
{
	static std::unordered_map<std::string, bool> cache;
	auto found = cache.find(Name);
	if (found != cache.end())
		return found->second;
	size_t index = 0;
	bool builtin = AurieSuccess(g_Yytk->GetBuiltinVariableIndex(Name, index));
	cache[Name] = builtin;
	return builtin;
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
		if (!AurieSuccess(g_Yytk->GetBuiltin(Name, Instance ? inst : nullptr, INT_MIN, value)))
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
		return AurieSuccess(g_Yytk->SetBuiltin(Name, Instance ? inst : nullptr, INT_MIN, value)) ? 1 : 0;
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

static int ApiHookCode(const char* CodeName)
{
	g_HookedNames.insert(CodeName);
	g_HookedCache.clear();
	return 1;
}

static void* ApiInstanceFromId(int Id)
{
	if (!RequireGameThread()) return nullptr;
	return CInstance::FromInstanceID(Id);
}

// Only inspect a pointer while the engine is lending it to a callback/call result.
static int ApiInstanceId(void* Instance)
{
    if (!RequireGameThread() || !Instance) return -1;
    auto* object = static_cast<YYObjectBase*>(Instance);
    if (object->m_ObjectKind != OBJECT_KIND_CINSTANCE) return -1;
    RValue id;
    if (!AurieSuccess(g_Yytk->GetBuiltin("id", static_cast<CInstance*>(Instance), INT_MIN, id))) return -1;
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

// ---- game events ----

static void FrameCallback(FWFrame& Context)
{
	g_GameThread = GetCurrentThreadId();
	UNREFERENCED_PARAMETER(Context);
	if (g_ManagedReady && g_Callbacks.OnFrame)
		g_Callbacks.OnFrame();
}

static void CodeCallback(FWCodeEvent& Context)
{
	g_GameThread = GetCurrentThreadId();
	if (!g_ManagedReady || g_HookedNames.empty())
		return;
	CInstance* self = std::get<0>(Context.Arguments());
	CInstance* other = std::get<1>(Context.Arguments());
	CCode* code = std::get<2>(Context.Arguments());
	if (!code)
		return;
	auto cached = g_HookedCache.find(code);
	bool hooked;
	if (cached != g_HookedCache.end())
		hooked = cached->second;
	else
	{
		const char* name = code->GetName();
		hooked = name && g_HookedNames.count(name) > 0;
		g_HookedCache[code] = hooked;
	}
	if (!hooked)
		return;
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
	g_Api.Version = 2;
	g_Callbacks.Size = sizeof(ManagedCallbacks);
	g_Callbacks.Version = 2;
	g_Api.Log = ApiLog;
	g_Api.CallBuiltin = ApiCallBuiltin;
	g_Api.CallScript = ApiCallScript;
	g_Api.GetVar = ApiGetVar;
	g_Api.SetVar = ApiSetVar;
	g_Api.HookCode = ApiHookCode;
	g_Api.InstanceFromId = ApiInstanceFromId;
	g_Api.LastError = ApiLastError;
	g_Api.InstanceId = ApiInstanceId;
	rc = initialize(&g_Api, &g_Callbacks);
	Log("StoneForge initialized: " + std::to_string(rc));
	return rc == 0;
}

// The log, <game>\dotnet\bridge.log - started afresh each run, by whichever stage comes first.
static void OpenLog(const fs::path& ModulePath)
{
	if (g_Log.is_open())
		return;
	fs::path dotnetDir = ModulePath.parent_path().parent_path() / "dotnet";
	fs::create_directories(dotnetDir);
	g_Log.open(dotnetDir / "bridge.log", std::ios::out | std::ios::trunc);
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
	g_ManagedReady = StartDotNet(dotnetDir);
	return AURIE_SUCCESS;
}
