// StoneForge.Patcher: sets Stoneshard up for C# mods, so the game is started as usual (Steam, the exe) and the
// mods just load. Installed in <game>\dotnet\patcher\; a release has a copy in files\dotnet\patcher\.
//
//   install    (the default) - from a release, StoneForge's files copied into the game; then StoneShard.exe
//              backed up (StoneShard.exe.vanilla) and patched with Aurie's patcher - a section that loads
//              <game>\AurieCore.dll before the game's own code - and prepare.
//   uninstall  - the game's own exe and data.win put back, StoneForge's files removed (mods\ kept).
//   prepare    - data.win made current: the loader's own additions and the scripts mods hook, built from the
//              game's own data.win (kept as dotnet\data_base.win). Run by the loader every time the game
//              starts, before the game reads its data - instant unless something changed (mods' hooks, a game
//              update, a re-patch with another tool), when it shows a window while it rebuilds.
//   run        - Steam's launch option (run %command%): the exe patched again if Steam put the game's own back,
//              then the game started.
//
// usage: StoneForge.Patcher [install|uninstall|prepare] [game folder]   |   StoneForge.Patcher run <game exe> [args]
using System.IO;
using StoneForge.Patcher;

string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "install";
bool interactive = mode is "install" or "uninstall";

// (Run by the loader at start-up, or by Steam, there's no console: one is opened only if there's something to show.)
if (interactive)
    PatcherConsole.Show();
try
{
    if (mode == "run")
        return RunCommand.Run(args.Skip(1).ToArray());
    var game = GameFolder.Locate(args.Length > 1 ? args[1] : null)
        ?? throw new DirectoryNotFoundException("Stoneshard wasn't found - give its folder: StoneForge.Patcher " + mode + " \"<Stoneshard folder>\"");
    if (!File.Exists(game.Exe))
        throw new FileNotFoundException("Stoneshard not found: " + game.Exe);
    switch (mode)
    {
        case "install":
            InstallCommand.Run(game);
            break;
        case "uninstall":
            UninstallCommand.Run(game);
            break;
        case "prepare":
            GameDataBuilder.Prepare(game);
            break;
        default:
            PatcherConsole.Log("usage: StoneForge.Patcher [install|uninstall|prepare] [game folder]  |  StoneForge.Patcher run <game exe> [args]");
            return 1;
    }
    return 0;
}
catch (Exception e)
{
    PatcherConsole.Show();
    PatcherConsole.Log("Failed: " + e.Message);
    if (!interactive)
    {
        PatcherConsole.Log("The game starts without the changes. (Press a key.)");
        PatcherConsole.WaitForKey();
    }
    return 2;
}
finally
{
    if (interactive)
    {
        PatcherConsole.Log("\nPress a key to close.");
        PatcherConsole.WaitForKey();
    }
}
