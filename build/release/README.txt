StoneForge {VERSION} - C# mods for Stoneshard
=============================================

StoneForge lets Stoneshard load mods written in C#. Mods are plain C# source in the game's mods folder; StoneForge
compiles and checks them when the game starts (only what's safe for a mod is allowed), and you can switch them on
and off from the Mods button on the main menu.


Requirements
------------
- Windows, and Stoneshard (Steam), on its default (native) branch or the VM modbranch. Mods with GML of their own
  and MSL packages need the VM modbranch: on the native branch they don't run.
- The .NET 10 Windows Desktop Runtime (x64): https://dotnet.microsoft.com/download/dotnet/10.0
  Choose Windows Desktop Runtime for MSL packages. The base .NET Runtime supports ordinary StoneForge mods only.
  (The installer tells you if it's missing.)


Installing
----------
1. Close Stoneshard.
2. Run "Install StoneForge.cmd". It finds Stoneshard through Steam (or asks for its folder), copies StoneForge in,
   and sets the game up. Your game's own files are kept, so it can all be undone.
3. Start Stoneshard as usual. The main menu has a Mods button.

Installing a newer StoneForge the same way updates it.


Mods
----
Each mod is a folder in Stoneshard's mods folder (<Stoneshard>\mods\<Mod>\) holding its .cs files, and its pictures
(and, if it has one, an icon.png) in an Assets folder inside it. Mods load when the game starts; the Mods
window switches them on and off at once.

MSL packages (VM modbranch only): put .sml files directly in mods, then start the game. Packages are enabled by
default and execute unrestricted C#: only install ones you trust. The Mods window shows their details and warning;
untick Enabled to disable a package on the next start. The bundled MSL 0.13.2.0
helper patches them before StoneForge; no separate launcher is needed. These packages cannot be hot-reloaded.
MSL launcher UI and scripting-server integrations are unsupported. Patch failures are logged in dotnet\msl-patch.log.

MSL Enhanced 1.15 is included in dotnet\msle. To override its location, set EnhancedDirectory in
dotnet\msl-runtime.json to its extracted folder. Example:
    { "Mode": "auto", "EnhancedDirectory": "msle" }
Enhanced format/API requirements are detected automatically. Set Mode to enhanced to force that runtime, or
standard to force the bundled regular MSL. Optional PackageOrder lists .sml filenames to run first, in order.
Enhanced dependencies/order/resource conflicts stop preparation and are explained in the patch log.
Audio, font and shader-file changes are staged with game data; originals are preserved in dotnet\msl-audio-base
and restored on removal/uninstall. Keep those originals until restoration. Applied Enhanced packages show [MSLE].
Enhanced build provenance and licence are in LICENSES\MSLE-SOURCE.md and LICENSES\MSLE-GPL-3.0.txt.

Writing mods: https://github.com/StoneForgeTeam/StoneForgeDocs


Steam updates
-------------
Game updates and Steam's "Verify integrity of game files" put the game's own StoneShard.exe back, which turns
StoneForge off until you install it again. To have it put back automatically, set Stoneshard's launch options in
Steam (right-click Stoneshard > Properties > General > Launch options) to:

    "<Stoneshard>\dotnet\patcher\StoneForge.Patcher.exe" run %command%

(The installer prints this line with your game's folder filled in.)


Uninstalling
------------
Close Stoneshard and run "Uninstall StoneForge.cmd". The game's own files are put back and StoneForge's removed;
your mods folder is kept.


What it changes in the game folder
----------------------------------
- StoneShard.exe is patched to load StoneForge (the original is kept as StoneShard.exe.vanilla).
- data.win gets StoneForge's additions (the original is kept as dotnet\data_base.win).
- AurieCore.dll, aurie\ and dotnet\ hold StoneForge itself; mods\ holds your mods.


Licences
--------
StoneForge is MIT-licensed, except its native bridge (StoneForge.Bridge), which includes YYToolkit's code and so
is AGPL-3.0. It's built on Aurie and YYToolkit (AGPL-3.0) and other components - see LICENSES\.
