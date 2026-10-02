# Aurie (modified)

`AurieCore.dll` and `AuriePatcher.exe` are built from [Aurie](https://github.com/AurieFramework/Aurie) (AGPL-3.0, see `LICENSE`) at commit `5c4839ea19d47c6afb7d459358f53643b2774e3a`, with `stoneforge.patch` applied. The patch and that commit are the complete corresponding source.

What the patch changes (`Aurie/source/AurieMain.cpp` only): Aurie modules load from `<game>\aurie` instead of `<game>\mods\aurie`, and the `<game>\mods\native` folder isn't loaded. `<game>\mods` is StoneForge's C# mods folder. AuriePatcher is unmodified.

Rebuild from source with `build\BuildThirdParty.ps1` (clones upstream, applies the patch, builds, replaces these files).

| File | SHA-256 |
|---|---|
| `AurieCore.dll` | `1D7B620E4E115AC26869FB1279740B3ED9F45FBB9FDDAF86AF1478AF7C658BC7` |
| `AuriePatcher.exe` | `BBBEBD3DC11B2FECAF6B1A3DBD1DB34B12338D859FC4B0D8E569F47DEA35335B` |

(Built with Visual Studio 18, toolset v145, Release x64. Rebuilds aren't byte-identical: compilers stamp their output.)
