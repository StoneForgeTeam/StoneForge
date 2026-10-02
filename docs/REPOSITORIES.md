# Repository layout

The organization is `StoneForgeTeam`. Both GitHub repositories exist. Each local checkout tracks its matching origin/main; project source has not been committed or pushed yet.

| Repository | Contents | Versioning |
|---|---|---|
| [StoneForge](https://github.com/StoneForgeTeam/StoneForge) | API, loader, native bridge, patcher, source generators, DataDump, tests, build/install tooling and documentation | One coordinated StoneForge release |
| [ExampleMod](https://github.com/StoneForgeTeam/ExampleMod) | Sample C# code, GML, assets, manifest and editor project | Independent mod versions; `mod.json` declares the minimum StoneForge version |

Keep the checkouts as siblings, with the sample folder named `ExampleMod`. There are no submodules or project references between them. The sample references the API and GML generator from an installed StoneForge release; its README explains SDK configuration and installation.

The API, bridge and loader share runtime contracts. The patcher, DataDump and generators also depend on the same game-data and generated-code conventions. Keeping them together lets a change to these contracts be built, tested and released as one unit. Documentation stays with the implementation it describes.

Third-party libraries remain pinned under `lib/`, with their licenses, source provenance and rebuild tooling. The small Aurie patch stays alongside its pin; YYToolkit is unmodified. Separate forks are unnecessary for the current scope. A dedicated Aurie fork would make sense if native changes grow enough to need their own releases or upstream collaboration.

Regression fixtures stay in the main repository so its tests do not require the sample checkout. New independent mods can follow ExampleMod's repository structure.
