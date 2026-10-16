# Aurie (modified)

`AurieCore.dll` and `AuriePatcher.exe` are built from [Aurie](https://github.com/AurieFramework/Aurie) (AGPL-3.0, see `LICENSE`) at commit `5c4839ea19d47c6afb7d459358f53643b2774e3a`, with `stoneforge.patch` applied. The patch and that commit are the complete corresponding source.

What the patch changes: Aurie modules load from `<game>\aurie` instead of `<game>\mods\aurie`, and the `<game>\mods\native` folder isn't loaded. `<game>\mods` is StoneForge's C# mods folder. Ctrl+C > Close log window hides the window while keeping the console connection alive until game exit; detaching it from the control handler could hang and invalidate live logging handles. The unload action leaves detachment to framework cleanup. AuriePatcher is unmodified.

Rebuild from source with `build\BuildThirdParty.ps1` (clones upstream, applies the patch, builds, replaces these files).

After building, run `build\TestConsoleClose.ps1` to exercise the real control handler in a hidden test console, with concurrent output and file logging checked after dismissing the window.

| File | SHA-256 |
|---|---|
| `AurieCore.dll` | `03C21425CD30A840F75E6F4397658AFA4446436D17991B887C00BF765D027B40` |
| `AuriePatcher.exe` | `BBBEBD3DC11B2FECAF6B1A3DBD1DB34B12338D859FC4B0D8E569F47DEA35335B` |

(Built with Visual Studio 18, toolset v145, Release x64. Rebuilds aren't byte-identical: compilers stamp their output.)
