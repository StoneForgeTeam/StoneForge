using System.Text.Json;

namespace StoneForge.Loader;

internal static class LoaderOptions
{
    internal static void Load()
    {
        string file = Path.Combine(Path.GetDirectoryName(typeof(Bridge).Assembly.Location)!, "stoneforge.json");
        if (!File.Exists(file)) return;
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            if (json.RootElement.TryGetProperty("callbackFailureThreshold", out var threshold))
                Hooks.FailureThreshold = Math.Clamp(threshold.GetInt32(), 1, 100);
        }
        catch (Exception e) { Game.Log("stoneforge.json unreadable; using defaults: " + e.Message); }
    }
}
