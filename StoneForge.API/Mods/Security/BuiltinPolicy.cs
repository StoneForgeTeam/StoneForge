namespace StoneForge;

/// <summary>The GameMaker built-in functions mods may not call through <see cref="Game.CallBuiltin"/>: those
/// that reach outside the game - files and folders (ModFiles is the way), the network and web, other
/// programs and native libraries, the clipboard, environment variables, loading other game data - and the
/// ones that call functions by index (a way around this list).</summary>
internal static class BuiltinPolicy
{
    private static readonly string[] DeniedPrefixes =
    {
        "file_", "directory_", "ini_", "buffer_save", "buffer_load", "network_", "http_", "url_", "external_",
        "clipboard_", "steam_", "zip_", "video_", "audio_create_stream", "sprite_save", "screen_save",
        "surface_save", "font_add", "sprite_add", "sprite_replace", "background_add", "load_csv", "game_save",
        "game_load", "game_change", "environment_", "execute_", "shell_", "os_request_permission", "gc_",
        "script_execute", "method", "callv", "debug_", "show_error", "exception_unhandled_handler",
        "dll_", "texture_global_scale", "window_handle", "window_device", "parameter_",
    };

    private static readonly HashSet<string> DeniedNames = new(StringComparer.Ordinal)
    {
        "get_open_filename", "get_save_filename", "get_open_filename_ext", "get_save_filename_ext",
        "event_perform_object",
    };

    internal static bool Allowed(string name, out string reason)
    {
        reason = "";
        string n = name.Trim().ToLowerInvariant();
        foreach (string prefix in DeniedPrefixes)
            if (n.StartsWith(prefix, StringComparison.Ordinal))
            {
                reason = $"{name} isn't available to mods (it reaches outside the game)";
                return false;
            }
        if (DeniedNames.Contains(n))
        {
            reason = $"{name} isn't available to mods";
            return false;
        }
        // (Crashes the game when called this way on its runtime; Game.Global[name] is undefined when missing.)
        if (n == "variable_global_exists")
        {
            reason = "variable_global_exists crashes this game when called from C#: use Game.Global[name] (undefined if there's no such global)";
            return false;
        }
        return true;
    }
}
