# YYToolkit

`YYToolkit.dll` is built from [YYToolkit](https://github.com/AurieFramework/YYToolkit) (AGPL-3.0, see `LICENSE`) at commit `86c133dc7f4e87991dd2cb7f2eac056dc5f13124`, with `stoneforge.patch`: its GameMaker error hook (`HkYYError`) no longer resolves game script names for its stack trace - that walk (`GetScriptData`) faults on GameMaker 2022.9, which turned every GML error into a crash before anything was logged. The game's own error message shows again. StoneForge.Bridge also compiles YYToolkit's shared headers and sources (`StoneForge.Bridge\include`), which is why the bridge is AGPL-3.0 too.

Rebuild from source with `build\BuildThirdParty.ps1`.

| File | SHA-256 |
|---|---|
| `YYToolkit.dll` | `2EDE9214742D1ED7178CE13E1907AFBA0D03398E222497C1976E1D32715B0A5F` |

(Built with Visual Studio 18, toolset v145, Release x64. Rebuilds aren't byte-identical: compilers stamp their output.)
