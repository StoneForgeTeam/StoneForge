# UndertaleModLib 0.9.2.0

These unmodified binaries are extracted from the official [UndertaleModTool 0.9.2.0 Windows release](https://github.com/UnderminersTeam/UndertaleModTool/releases/tag/0.9.2.0), asset `UndertaleModTool_v0.9.2.0-Windows.zip`. Both DLL checksums were verified against the downloaded release archive.

| File | SHA-256 |
|---|---|
| UndertaleModLib.dll | `BB12B0E22B46BE6A8E6E498767CA61E8974E5ACAC6DF46E66000AFE1DD35FBF5` |
| Underanalyzer.dll | `13866EFFB6DB10AA0C7FEA1B9F017BB362E8B9E1266295896BEBCD920C3571FA` |

UndertaleModLib's file version is 0.9.2.0, from [source commit 03c7048438347799e0717b6e57a9358d000472e0](https://github.com/UnderminersTeam/UndertaleModTool/tree/03c7048438347799e0717b6e57a9358d000472e0). It is GPL-3.0; `LICENSE.txt` is copied from the same release archive.

Underanalyzer's assembly/file version is 1.0.0.0; its informational version identifies [source commit 4ff50a866b4c1a7acee8cebe6a56d6a48709b453](https://github.com/UnderminersTeam/Underanalyzer/tree/4ff50a866b4c1a7acee8cebe6a56d6a48709b453). It is MPL-2.0; `Underanalyzer-LICENSE.txt` is from that exact revision.

StoneForge uses UndertaleModLib's `CodeImportGroup` and Underanalyzer to compile/decompile GML, and UndertaleModLib to serialize game data. This release requires .NET 10. The UndertaleModTool desktop application is not shipped; image conversion and interactive scripting features are not used. The two DLLs are kept together in the patcher distribution.
