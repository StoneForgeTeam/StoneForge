# YYToolkit

`YYToolkit.dll` is built from [YYToolkit](https://github.com/AurieFramework/YYToolkit) (AGPL-3.0, see `LICENSE`) at commit `86c133dc7f4e87991dd2cb7f2eac056dc5f13124`, unmodified. StoneForge.Bridge also compiles YYToolkit's shared headers and sources (`StoneForge.Bridge\include`), which is why the bridge is AGPL-3.0 too.

Rebuild from source with `build\BuildThirdParty.ps1`.

| File | SHA-256 |
|---|---|
| `YYToolkit.dll` | `EEE0B7FE025234A0C0A50DD1971C5D06E3305E12E9A4DACF4A5B2FEB7710AE23` |

(Built with Visual Studio 18, toolset v145, Release x64. Rebuilds aren't byte-identical: compilers stamp their output.)
