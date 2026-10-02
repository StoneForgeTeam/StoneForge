# StoneForge development tools

## Building

The managed runtime projects use .NET 10; install the .NET 10 SDK for builds and the x64 .NET 10 Runtime for the game. Roslyn source generators intentionally remain `netstandard2.0` so Visual Studio can load them. The ExampleMod editor project targets `net10.0` too.

Build the solution with Visual Studio's MSBuild (the native bridge is a C++ project, which `dotnet build` can't load):

```powershell
& "<Visual Studio>\MSBuild\Current\Bin\MSBuild.exe" StoneForge.slnx -restore -p:Configuration=Release -p:PlatformToolset=v145
```

`build\Package.ps1` builds and makes a release in `artifacts\`.

**Stoneshard must be installed** (the Steam version, VM modbranch). The typed game API (`Sprite`, `GameObject`, `Scripts`, `GameItems`, `GameSkills`...) is generated from the game's own data, which isn't in the repository: the API build runs `StoneForge.DataDump` on this machine's install into `StoneForge.API\obj\GameData`. It reads `dotnet\data_base.win` (StoneForge's preserved unpatched copy) when StoneForge is installed, else `data.win`, and only re-reads it when that file changes (about 4 s; otherwise a fraction of a second). The game is found through Steam; elsewhere, set `STONESHARD_DIR` or pass `-p:StoneshardDir=<folder>`. Run it by hand with `dotnet StoneForge.DataDump\bin\Release\net10.0\StoneForge.DataDump.dll <output folder> [--game <folder>] [--data <data.win>]`.

## Developer host

The developer host is **off by default**. Enable it for a test session with `STONEFORGE_TEST=1`, or create an empty `dotnet/testhost.enable` file beside the loader before launching. Remove the marker/unset the variable and restart to disable it. The shipped package includes neither marker nor probe mod.

The host creates a current-user-only named pipe, `stoneforge-<process id>`, and records its name in `dotnet/testhost.pipe`. Requests execute on the game thread after startup completes. The queue is bounded, input lines are limited to 16,384 characters, and queued requests expire after ten seconds. A timeout after execution starts does not guarantee a mutation was cancelled.

From the source workspace, PowerShell examples:

```powershell
.\build\Inspect.ps1 -Request '{"cmd":"smoke"}'
.\build\Inspect.ps1 -Request '{"cmd":"status"}'
.\build\Inspect.ps1 -Request '{"cmd":"objects","name":"o_mainMenuButton"}'
.\build\Inspect.ps1 -Request '{"cmd":"inspect","id":397872,"names":["x","y","object_index"]}'
.\build\Inspect.ps1 -Request '{"cmd":"globals","names":["room","mainMenuRoom"]}'
.\build\Inspect.ps1 -Request '{"cmd":"builtin","name":"abs","args":[-17.25]}'
.\build\Inspect.ps1 -Request '{"cmd":"trace.watch","name":"scr_stonemod_draw_gui"}'
.\build\Inspect.ps1 -Request '{"cmd":"trace.read"}'
.\build\Inspect.ps1 -Request '{"cmd":"trace.stop"}'
```

Use `-GameFolder 'C:\path\to\test-game'` for another installation. IDs above are examples; use `objects` to obtain current IDs. Inspection reads selected names, not a full enumeration of every variable. Built-in calls use the normal mod API restrictions; this interface does not evaluate arbitrary C#.

Tracing currently records **input arguments and self ID**, not the original script's return value. It can attach to a script whose hook flag already exists, usually because a loaded mod declared and subscribed to it. It does not patch a new script while the game is running. Traces retain at most 128 calls and 32 watches; `trace.clear` clears recordings and `trace.stop` removes all diagnostic watches.

`mod.reload` and `mod.disable` take a `name` and queue a change for the next frame. They do not persist the player's enabled preference. Use `status` afterward to inspect the result. Reloading game-content mods can remove their items, just as the Mods window does.

## Reproducible live probe

Use a disposable copy of the game at its main menu, without loading a save. Copy the **contents** of `StoneForge.Tests/live` into `mods/ReliabilityProbe` and enable the developer host. The probe needs game data already prepared by StoneForge, including `o_stonemod_gui` and `o_stonemod_modal`.

At startup the log should show `LIVE PASS` for sprite import/deduplication, stored callback instances, stored-instance call context, and rejecting a destroyed instance. The probe imports `Assets/probe.png`, a 2×2 test image. It creates and immediately destroys its own temporary modal instance.

```powershell
.\build\Inspect.ps1 -Request '{"cmd":"mod.reload","name":"Reliability Probe"}'
.\build\Inspect.ps1 -Request '{"cmd":"status"}'
.\build\Inspect.ps1 -Request '{"cmd":"mod.disable","name":"Reliability Probe"}'
```

Repeated reloads should retain one active sprite with the same ID, printed in the log. Disable should leave zero active probe sprites and one retired sprite; `sprite_get_width` for that ID should return 1 instead of 2. Retired IDs remain valid because game objects might still reference them. Reuse is restricted to the same owner/path/options; adding entirely new asset paths still allocates new IDs.

To test fault isolation, enable/reload the probe and set its development flag:

```powershell
.\build\Inspect.ps1 -Request '{"cmd":"builtin","name":"variable_global_set","args":["stoneforge_probe_fault",true]}'
```

After three game frames, `status` should show `RuntimeError`, and the log should contain one exception plus one pause message. A `smoke` request should still pass. Set the flag to `false`, then reload the probe to recover. Remove the probe after testing.

## API lifetime rules

### Context content APIs

Prefer `context.Items.Add(item)`, `context.Buffs.Add(buff)` and `context.Skills.Add(skill)` in `Load(ModContext context)`. The owning context is supplied automatically. Use `context.Items.Give(item)` and `context.Buffs.Apply(buff, target, turns)` for operations. Inside item, consumable, buff and skill subclasses, use the inherited `Context` property, for example `Context.Buffs.Apply(buff, target, 3)`.

This allows your mod to use namespaces such as `MyMod.Items`, `MyMod.Buffs` and `MyMod.Skills` without qualifying the StoneForge static classes. The old static APIs continue working. Registration, duplicate checks and unload cleanup share their existing implementations; content keys remain game-wide and must still be unique. Item queries and buff operations are not restricted to content owned by the context.

- Store room instances as `Instance` or call `Persist()` for an explicitly ID-only reference. Check `Exists` before use; IDs are identities in the running game, not save-file identities.
- Struct/global handles borrowed through script values are temporary and must be consumed inside the callback that received them. Full engine-GC rooting/array ownership is a separate future feature.
- Native function failures raise `GameCallException`. Missing variable reads still return `Undefined`. Native engine crashes or GML failures that bypass the bridge's status return are not converted into recoverable managed exceptions.
- Load sprites and sounds through `ModContext` for ownership tracking. Do not manually delete an owned sprite/stream: the loader needs its ID for retirement and cleanup.
- A paused mod keeps imported sprites alive until unload. Its registered callbacks stop, and its windows close on the next frame. Resetting failure state/reloading is explicit.
- Existing typed wrappers and source-only Roslyn policy remain in place. No external source code was copied into StoneForge for these changes.

## Patcher and UndertaleModLib

StoneForge owns the small editing adapter in `StoneForge.Patcher/GameData/GameDataEditor.cs`. Each build has its own `UndertaleData` context. UndertaleModLib reads/writes data.win and compiles/decompiles GML; WPF is not a dependency. The VM **modbranch is still required**.

Both Patcher and DataDump reference UndertaleModLib 0.9.2.0 and its matching Underanalyzer DLL in `lib/UndertaleModLib`; see its README for exact source revisions, checksums and licenses. The editor uses `CodeImportGroup` and the Underanalyzer decompiler. Functions are imported as standard global scripts, with their global initialization entries maintained by the library. The argument rewrite remains in place for this upgrade; simplifying it is separate work.

The integration tests (`StoneForge.Patcher.Tests`, xUnit) patch unpatched VM game data once, save it to a temporary file and read it back. They check all loader patches, custom consumable/skill inheritance, mod GML, script hooks, function metadata after serialization, unchanged original asset IDs, and error handling. The data is `STONEFORGE_TEST_DATA` if set, otherwise the Steam install's `dotnet\data_base.win` (StoneForge's preserved unpatched copy); with neither, the tests are skipped. The input is never changed.

```powershell
dotnet test StoneForge.Patcher.Tests -c Release
$env:STONEFORGE_TEST_DATA = "C:\path\to\unpatched-data.win"; dotnet test StoneForge.Patcher.Tests -c Release
```

The offline tests (`StoneForge.Tests`, xUnit) need no game: the mod sandbox cases, ModFiles' path rules, the loader against a fake bridge, the context APIs and GML bindings. Run `dotnet test StoneForge.Tests -c Release`, or both projects from Visual Studio's Test Explorer.

Use a disposable game copy for live testing. Packaging rejects stale UndertaleModTool application and Serilog DLLs; clean/rebuild the patcher if that check fails. An installed upgrade removes obsolete files recorded in StoneForge's installation manifest.

## Third-party native binaries

Aurie (`AurieCore.dll`, `AuriePatcher.exe`) and YYToolkit (`YYToolkit.dll`) are prebuilt in `lib/Aurie` and `lib/YYToolkit`, so packaging needs nothing outside the repository. Each folder's README names the upstream commit, the SHA-256 of the binaries and, for Aurie, the patch applied (`lib/Aurie/stoneforge.patch`: modules load from `<game>\aurie`). Both are AGPL-3.0; releases include the licences, the patch and those notes under `LICENSES\`.

To rebuild them from source (needs git and Visual Studio's C++ tools; clones into `build\.thirdparty`):

```powershell
powershell -ExecutionPolicy Bypass -File build\BuildThirdParty.ps1
```

It prints the new checksums; update the READMEs with them. To move to a newer upstream version, change the commit in the script and check the patch still applies.
