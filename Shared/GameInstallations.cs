using System.IO;

namespace StoneForge;

internal sealed record GameInstallation(string Path, string Store);

internal static class GameInstallations
{
    internal static IReadOnlyList<GameInstallation> Discover()
        => Combine(SteamLibrary.FindAllStoneshard().Select(path => new GameInstallation(path, "Steam"))
            .Concat(EpicLibrary.FindAllStoneshard().Select(path => new GameInstallation(path, "Epic")))
            .Concat(GogLibrary.FindAllStoneshard().Select(path => new GameInstallation(path, "GOG"))));

    // A store may register one folder in several manifests or registry views. Offer it only once.
    internal static IReadOnlyList<GameInstallation> Combine(IEnumerable<GameInstallation> installations)
        => installations.Select(installation => installation with { Path = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(installation.Path)) })
            .GroupBy(installation => installation.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => new GameInstallation(group.First().Path,
                string.Join(" / ", group.Select(installation => installation.Store).Distinct(StringComparer.OrdinalIgnoreCase))))
            .ToArray();
}
