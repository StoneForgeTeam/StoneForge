# StoneForge

A C# mod loader for Stoneshard on Windows. Mods are source folders with a `mod.json` manifest, C# code and optional assets and GML bindings. StoneForge provides context APIs for items, consumables, buffs, skills, UI and game hooks.

## Requirements

- Stoneshard (Steam), on the **VM modbranch**.
- The **.NET 10 Runtime, x64** to play with mods.
- To build: .NET 10 SDK and Visual Studio with MSBuild and C++ tools (the current native build uses toolset `v145`).

MSL is not required. StoneForge uses UndertaleModLib 0.9.2.0 and Underanalyzer directly to patch game data. Game data is read from your local install; it is not included in this repository.

## Install

Use the packaged release under `artifacts/StoneForge-<version>`. Close Stoneshard, extract the release and run `Install StoneForge.cmd`. Launch the game normally afterward. Re-running the installer updates an existing installation; `Uninstall StoneForge.cmd` restores the preserved game files and keeps your mods.

Mod folders go in `<Stoneshard>/mods`. Open the in-game Mods window to enable or disable them. GML mods carry a warning: their scripts execute directly in GameMaker, outside the C# source restrictions, and need a game restart after edits.

## Build and test

From a Visual Studio developer PowerShell:

```powershell
msbuild StoneForge.slnx -restore -p:Configuration=Release -p:PlatformToolset=v145
dotnet test StoneForge.Tests -c Release --no-build
dotnet test StoneForge.Patcher.Tests -c Release --no-build
powershell -ExecutionPolicy Bypass -File build/Package.ps1 -NoBuild
```

The API build discovers Stoneshard through Steam. To choose another install, set `STONESHARD_DIR` or pass `-p:StoneshardDir="C:\path\to\Stoneshard"`. DataDump writes generated game metadata under `StoneForge.API/obj/GameData`. Integration tests use `STONEFORGE_TEST_DATA` or Steam's preserved `dotnet/data_base.win`; they skip when no suitable data is available.

Pinned native binaries and their rebuild instructions are under `lib/Aurie`, `lib/YYToolkit` and `build/BuildThirdParty.ps1`. Library provenance and checksums are under `lib/UndertaleModLib`.

## Write a mod

- [Mod manifests and content IDs](docs/MODS.md)
- [GML bindings](docs/GML.md)
- [Development tools, testing and lifetime rules](docs/DEVELOPMENT.md)
- [Example Mod](https://github.com/StoneForgeTeam/ExampleMod): the separate sample repository for items, buffs, skills, UI and GML.

The example builds against an installed StoneForge SDK and has its own release history. See [repository layout](docs/REPOSITORIES.md) for the organization and repository boundaries.

## License

StoneForge's source license is in [LICENSE](LICENSE). Native components and game-data libraries have their own licenses and provenance in `lib/`; release notices are in [THIRD-PARTY.txt](build/release/LICENSES/THIRD-PARTY.txt). Stoneshard and its game data belong to their respective owners.

See [CHANGELOG.md](CHANGELOG.md) for the development history and compatibility changes.
