# MSL compatibility host

This GPL-3.0 executable runs MSL 0.13.2.0's own patching code (its release `ModShardLauncher.dll`, copied beside it) in a separate process. See [provenance](../lib/MSL/README.md) for the binaries, their checksums and why the host must not be named `ModShardLauncher`. It is intentionally not a reference of the loader or API.

Running the host requires the **.NET 10 Windows Desktop Runtime (x64)**. The installer warns when it is missing, and SML preparation checks it before starting the helper. Ordinary StoneForge mods need only the base .NET 10 Runtime.

The patcher invokes its apphost with one JSON request path containing `Input`, `Output`, `Metadata` and ordered `Packages`. All paths are absolute. It reads the preserved VM base, loads the selected packages, injects MSL's normal support scripts, applies the packages and writes a separate intermediate result and metadata JSON. Parent scripts and their nested functions are grouped before serialization, as required by the legacy writer. StoneForge reads that result with its own newer library and applies its patches before replacing live data.

The parent owns the request directory, five-minute timeout and diagnostic log. It snapshots each enabled package and verifies its SHA-256 before execution. The helper does not sandbox mod code. Initial discovery does not load assemblies; a package in the mods folder is enabled unless it is in `mods.json`'s Disabled list (the Mods window's Enabled box), by its `sml:<filename>` identity.

## Validation

- Offline discovery tests cover enabled-by-default discovery, case-insensitive IDs, stable top-level discovery, same-size/same-time content edits, removal/disable invalidation and malformed configuration handling.
- Opt-in integration test: set `STONEFORGE_TEST_DATA` to clean VM data and `STONEFORGE_TEST_SML` to a trusted package, then run `dotnet test StoneForge.Patcher.Tests -c Release`. Never point it at untrusted code. Without both inputs it skips.
- Tested with the local `HelloStoneshard.sml`: combined patches serialize and reload, caching works, malformed replacement preserves the last output, and removing the package removes MSL content while preserving the base.
- Regression tests cover script grouping, missing parents, cyclic parents, runtime requirements and applied-package counts. The real-package test also verifies metadata caching and live console forwarding.
- A nine-package set, including BW's Notebooks, was serialized successfully and read back with both the legacy and current UndertaleModLib. Stoneshard launched with all nine packages applied and StoneForge's C# Example Mod loaded. Individual mods' gameplay and MSL settings still require validation.

Current scope excludes launcher UI/server integrations and guarantees for arbitrary third-party packages. Use clean VM data and a test save when validating another mod.
