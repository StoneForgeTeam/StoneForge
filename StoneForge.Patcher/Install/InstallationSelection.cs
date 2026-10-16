using System.IO;

namespace StoneForge.Patcher;

internal static class InstallationSelection
{
    internal static string? Choose(IReadOnlyList<GameInstallation> installations, TextReader input, Action<string> output, bool interactive)
    {
        if (installations.Count == 0)
            return null;
        if (installations.Count == 1)
            return installations[0].Path;
        if (!interactive)
            throw new InvalidOperationException("Multiple Stoneshard installations were found. Specify the game folder explicitly: StoneForge.Patcher <command> \"<game folder>\". Found: "
                + string.Join("; ", installations.Select(installation => $"{installation.Store}: {installation.Path}")));
        output("Multiple Stoneshard installations were found. Choose the copy to use:");
        for (int i = 0; i < installations.Count; i++)
            output($"  {i + 1}. [{installations[i].Store}] {installations[i].Path}");
        output("  M. Enter another folder");
        output("  0. Cancel");
        while (true)
        {
            output($"Choose 1-{installations.Count}, M, or 0:");
            string? answer = input.ReadLine()?.Trim();
            if (answer == null || answer == "0")
                throw new OperationCanceledException("Installation selection cancelled. No files were changed.");
            if (string.Equals(answer, "M", StringComparison.OrdinalIgnoreCase))
                return null;
            if (int.TryParse(answer, out int selected) && selected >= 1 && selected <= installations.Count)
                return installations[selected - 1].Path;
            output("Please enter one of the listed choices.");
        }
    }
}
