# Pre-commit status

The UndertaleModLib/.NET upgrade is complete. Both projects start their public version history at 0.1.0. Both local repositories are initialized and track their existing GitHub main branches. No project-source commit or push has been made.

| Item | Status |
|---|---|
| `.gitignore` / `.gitattributes` | Added; excludes build/IDE output, artifacts, third-party work trees and local game data. Pinned library binaries remain eligible for tracking. |
| Example Mod repository | Separated into the sibling `ExampleMod` directory for `StoneForgeTeam/ExampleMod`. Targets .NET 10 and references the installed SDK. |
| Self-contained native release build | Existing `lib/Aurie`, `lib/YYToolkit` and rebuild script retained. |
| Dead code / stale excase | Previous cleanup retained. |
| Root README | Added build, install, modding and license pointers. |
| Documentation/analyzer warnings | Ambiguous XML links fixed; GML analyzer diagnostic tracked in release files. |
| Public author | Existing source attribution remains `failmelon`; repositories belong to `StoneForgeTeam`. |
| Locally generated game data | Retained under ignored `obj/GameData`; no game data added to source. |
| UndertaleModLib pin / .NET 10 | Completed: release binaries verified, licenses included, patcher/DataDump ported, managed/native build and live startup checked. |
| Version/changelog policy | Reset to 0.1.0 in both repositories. The old development changelog is archived locally under ignored `artifacts/`. |
| Repository layout | Organization: `StoneForgeTeam`. Prepared `StoneForge` and `ExampleMod` as separate repository directories; see `REPOSITORIES.md`. Both remotes are connected; project-source commits and pushes remain pending. |

## Validation evidence

- `dotnet test StoneForge.Tests -c Release`: 129 passed, none skipped.
- `dotnet test StoneForge.Patcher.Tests -c Release`: 8 passed, none skipped, using real preserved VM game data. Includes serialized global initialization entries for loader and mod GML functions.
- ExampleMod editor build on .NET 10: no warnings/errors.
- Before the version reset, the development package ran in a disposable Stoneshard copy on .NET 10.0.11 with runtime mod compilation. This is earlier runtime evidence, not a new live test of the renamed release.
- ExampleMod GML `Twice(21)` and `Add(20, 22)` both returned 42. Main menu reached room 6; numeric/UTF-8 bridge round trips and live lifetime checks passed.
- Full gameplay, custom skill casting and save/load still need separate validation. Argument-rewrite simplification was deliberately left outside the upgrade.
- ExampleMod reload completed with correct GML results and no runtime error, but the unload audit reported the previous assembly still retained in memory. The cause and whether it predates the upgrade have not been established; track this separately before describing reload as leak-free.

Generated logs and release packages are under ignored `artifacts/`.

## Public release preparation

- StoneForge and ExampleMod versions and minimum-version examples reset to 0.1.0.
- Full Release solution build passed; 129 unit tests and 8 real-game-data integration tests passed without skips after the reset.
- Packaged StoneForge 0.1.0 and verified the loader reports file version 0.1.0.0.
- Standalone ExampleMod builds against the packaged 0.1.0 SDK without warnings or errors.
- Required pinned binaries are explicitly included despite global Git ignore rules. Build outputs and game data remain excluded.
- Both repositories are staged for review; no source commit or push has been made. Vendored-source whitespace is preserved.
