# MSL Enhanced runtime

Pinned binaries from the player's supplied September 17, 2026 MSLE installation,
corresponding to Nexus version 1.15 by Tbonex28b:
https://www.nexusmods.com/stoneshard/mods/103?tab=description

The Enhanced launcher retains file version 0.13.2.0, so the adapter and release packaging
validate these binaries by SHA-256 rather than that version number:

| File | SHA-256 |
| --- | --- |
| `ModShardLauncher.dll` | `26168221007AD5F0ADEF8B9474D8D8263FA477FD50F3622AB543560FDBCA1D4E` |
| `UndertaleModLib.dll` | `EBFC4AC77ABABE4BAB27FCB717DA6DBC23D246F8E0FD7A08CC63AF13D53604A8` |
| `UndertaleModTool.dll` | `3E90401CCFBE4257F193ECA65315F29A5765B953F66A31991721A773F6BBF745` |

`build/Package.ps1` installs this runtime and its Windows x64 dependencies in
`dotnet/msle`. It contains no mods, launcher executable, test tools, logs or user settings.
The standard helper remains in `dotnet/patcher/msl`; the patcher's own UndertaleModLib
is separate. Enhanced reuses the bundled helper executable and its shared dependencies.
No external MSLE installation or `EnhancedDirectory` override is required.

MSL Enhanced declares GPL-3.0 on its mod page; the licence text is included here.
The page links the original MSL source at https://github.com/ModShardTeam/ModShardLauncher;
source for this Enhanced fork was not supplied or located. These are the supplied
build's binaries, not a StoneForge rebuild of the Enhanced fork.

Additional runtime dependencies retained from the supplied build are Magick.NET
(Apache-2.0) and its native ImageMagick library (ImageMagick licence), SharpZipLib
(MIT), XamlAnimatedGif (Apache-2.0), and Microsoft.Win32.SystemEvents (MIT).
