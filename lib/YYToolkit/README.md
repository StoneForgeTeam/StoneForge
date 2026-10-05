# YYToolkit

`YYToolkit.dll` is built from [YYToolkit](https://github.com/AurieFramework/YYToolkit) (AGPL-3.0, see `LICENSE`) at commit `86c133dc7f4e87991dd2cb7f2eac056dc5f13124`, with `stoneforge.patch`: its GameMaker error hook (`HkYYError`) no longer resolves game script names for its stack trace - that walk (`GetScriptData`) faults on GameMaker 2022.9, which turned every GML error into a crash before anything was logged. The game's own error message shows again. And its console window ("YYToolkit Log") opens only on request: a file named `yytoolkit-console.on` in the game's `dotnet` folder. `YYToolkit.log` is written either way. StoneForge.Bridge also compiles YYToolkit's shared headers and sources (`StoneForge.Bridge\include`), which is why the bridge is AGPL-3.0 too.

Rebuild from source with `build\BuildThirdParty.ps1`.

| File | SHA-256 |
|---|---|
| `YYToolkit.dll` | `B814A79EA950B8F3FC97ECF3D4222E59007182B3FE88F9AF2E3BAE943593514A` |

(Built with Visual Studio 18, toolset v145, Release x64. Rebuilds aren't byte-identical: compilers stamp their output.)
