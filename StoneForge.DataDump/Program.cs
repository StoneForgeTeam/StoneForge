// StoneForge.DataDump: writes what the typed StoneForge API is generated from (StoneForge.Generators), read from the
// player's own Stoneshard game data. StoneForge.API's build runs it; it does nothing when its output is already from
// the same game data.
//
// usage: StoneForge.DataDump <output folder> [--game <Stoneshard folder>] [--data <data.win>]
//   The game data: --data; else <game>\dotnet\data_base.win (StoneForge's preserved unpatched copy) when StoneForge is
//   installed, else <game>\data.win. The game: --game, else STONESHARD_DIR, else Steam's libraries.
using StoneForge;
using StoneForge.DataDump;

string? output = null, game = null, data = null;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--game" when i + 1 < args.Length: game = args[++i]; break;
        case "--data" when i + 1 < args.Length: data = args[++i]; break;
        default:
            if (args[i].StartsWith("--") || output != null)
                return Usage();
            output = args[i];
            break;
    }
}
if (output == null)
    return Usage();

try
{
    string source = data ?? GameDataFile(game);
    if (Dump.IsCurrent(source, output))
        return 0;
    Dump.Write(source, output);
    return 0;
}
catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException or InvalidDataException)
{
    // (MSBuild shows a line starting "error" as a build error.)
    Console.Error.WriteLine("error SFDD001: " + e.Message);
    return 1;
}

static string GameDataFile(string? game)
{
    game ??= Environment.GetEnvironmentVariable("STONESHARD_DIR") is { Length: > 0 } fromEnv ? fromEnv : SteamLibrary.FindStoneshard();
    if (game == null)
        throw new DirectoryNotFoundException("StoneForge's typed API is generated from Stoneshard's own game data, and Stoneshard wasn't found "
            + "in Steam's libraries. Set STONESHARD_DIR (or the StoneshardDir MSBuild property) to the game's folder.");
    string preserved = Path.Combine(game, "dotnet", "data_base.win");
    if (File.Exists(preserved))
        return preserved;
    string dataWin = Path.Combine(game, "data.win");
    return File.Exists(dataWin) ? dataWin : throw new FileNotFoundException($"No data.win in {game} - is that Stoneshard's folder?");
}

static int Usage()
{
    Console.Error.WriteLine("usage: StoneForge.DataDump <output folder> [--game <Stoneshard folder>] [--data <data.win>]");
    return 2;
}
