# MSL compatibility helper

The separate `StoneForge.MslHost` process runs the bundled ModShardLauncher **0.13.2.0** DLL's own patching code.
The selected `ModShardLauncher.dll` reports file version `0.13.2.0` and product version
`1.0.0+35fec305aaa2b676b054db5d76a58eca684ad320`, identifying this upstream source revision:
https://github.com/ModShardTeam/ModShardLauncher/tree/35fec305aaa2b676b054db5d76a58eca684ad320

The hash below pins the actual bundled binary. It is a different build from the older local MSL install with
revision `2703478b5b1272e41d3c78fe6170a9b3cea26984`; those binaries must not share a checksum entry.
The legacy UndertaleModLib and UndertaleModTool binaries are retained unchanged from the MSL install.

| File | SHA-256 |
| --- | --- |
| `ModShardLauncher.dll` | `7B46A74223B3AC376C3ECE9133DB2C2766D9D881A9520653779FCF0CAAFFDEED` |
| `UndertaleModLib.dll` | `AF6D7CCB05204B823017C00C7E1BCC2EEEC6F1FA9FB6A196D997AD8533025565` |
| `UndertaleModTool.dll` | `3E90401CCFBE4257F193ECA65315F29A5765B953F66A31991721A773F6BBF745` |

All three are GPL-3.0 (`LICENSE.txt`); the same licence text is shipped in the release.

The host is its own small assembly, `StoneForge.MslHost`, with MSL's `ModShardLauncher.dll` copied beside it, so a mod's
references to MSL's API (`ModShardLauncher.ModFile`, `Mods.Mod`...) resolve to MSL itself. It must never be named
`ModShardLauncher`: its own output would replace MSL's dll, and mods fail with `TypeLoadException`. MSL is a WPF app, so the
host requires the **.NET 10 Windows Desktop Runtime (x64)**, but it shows no UI: MSL's launcher window and mod list are stand-ins made
without running their constructors (MSL's patching only reads its version and the list of mods).

MSL's `UndertaleModLib.dll` is its old library, used only inside the helper; it must not replace StoneForge's newer one.
`UndertaleModTool.dll` is needed for MSL's room-layer helpers (its disclaimer room).

Mods depending on the launcher's UI or its interface server are unsupported. The helper runs unrestricted mod code; it is
process isolation for dependencies, not a security sandbox.

Build: `dotnet build StoneForge.MslHost -c Release`. The patcher build and release packaging copy its output into
`dotnet/patcher/msl/`. Dependencies restored by the project: Newtonsoft.Json (MIT), Serilog and Serilog.Sinks.Console
(Apache-2.0), System.Drawing.Common (MIT).
