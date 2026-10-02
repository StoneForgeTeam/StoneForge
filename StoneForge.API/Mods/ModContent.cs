namespace StoneForge;

// Every import is owned, including imports made before Load fails. Retired sprite IDs stay valid:
// game objects can retain them, so replace their pixels instead of deleting them underneath the game.
internal static class ModContent
{
    private sealed record SpriteKey(string Owner, string Path, int Frames, int X, int Y);
    private static readonly Dictionary<SpriteKey, int> Active = new();
    private static readonly Dictionary<SpriteKey, int> Retired = new();
    private static readonly Dictionary<string, List<int>> Sounds = new();
    private static string? _transparent;
    internal static int ActiveSprites => Active.Count;
    internal static int RetiredSprites => Retired.Count;

    internal static int LoadSprite(string owner, string path, int frames, int x, int y)
    {
        Game.EnsureGameThread();
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 1);
        var key = new SpriteKey(owner, path, frames, x, y);
        if (Active.TryGetValue(key, out int existing)) return existing;
        int id;
        if (Retired.TryGetValue(key, out id))
        {
            // Reuse only the same owner's asset, never let an old reference show another mod's picture.
            if (Game.CallBuiltinTrusted("sprite_exists", default, default, id).AsBool)
                Replace(id, path, frames, x, y);
            else
                id = Add(path, frames, x, y);
        }
        else id = Add(path, frames, x, y);
        if (id < 0) return -1;
        if (!Game.CallBuiltinTrusted("sprite_exists", default, default, id).AsBool)
            throw new GameCallException("sprite_add", $"could not load {Path.GetFileName(path)}");
        Active.Add(key, id);
        Retired.Remove(key);
        return id;
    }

    private static int Add(string path, int frames, int x, int y)
        => Game.CallBuiltinTrusted("sprite_add", default, default, path, frames, false, false, x, y).AsInt;

    internal static int LoadSound(string owner, string path)
    {
        Game.EnsureGameThread();
        int id = Game.CallBuiltinTrusted("audio_create_stream", default, default, path).AsInt;
        if (id >= 0)
        {
            if (!Sounds.TryGetValue(owner, out var sounds)) Sounds[owner] = sounds = new();
            sounds.Add(id);
        }
        return id;
    }

    internal static void RemoveMod(string owner)
    {
        foreach (var pair in Active.Where(p => p.Key.Owner == owner).ToArray())
        {
            try
            {
                if (Game.CallBuiltinTrusted("sprite_exists", default, default, pair.Value).AsBool)
                    Replace(pair.Value, Transparent(), 1, 0, 0);
                Retired[pair.Key] = pair.Value;
                Active.Remove(pair.Key);
            }
            catch (Exception e) { Game.Log($"[{owner}] Retiring sprite {pair.Value} failed (kept for retry): {e.Message}"); }
        }
        if (!Sounds.TryGetValue(owner, out var sounds)) return;
        foreach (int id in sounds.ToArray())
        {
            try
            {
                Game.CallBuiltinTrusted("audio_stop_sound", default, default, id);
                Game.CallBuiltinTrusted("audio_destroy_stream", default, default, id);
                sounds.Remove(id);
            }
            catch (Exception e) { Game.Log($"[{owner}] Releasing sound {id} failed (kept for retry): {e.Message}"); }
        }
        if (sounds.Count == 0) Sounds.Remove(owner);
    }

    private static string Transparent()
    {
        if (_transparent != null) return _transparent;
        string path = Path.Combine(Path.GetDirectoryName(typeof(ModContent).Assembly.Location)!, "transparent.png");
        File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAC0lEQVR4nGNgAAIAAAUAAXpeqz8AAAAASUVORK5CYII="));
        return _transparent = path;
    }

    private static void Replace(int id, string path, int frames, int x, int y)
    {
        var result = Game.CallBuiltinTrusted("sprite_replace", default, default, id, path, frames, false, false, x, y);
        if (result.Kind == GmKind.Real && result.AsReal < 0)
            throw new GameCallException("sprite_replace", $"could not load {Path.GetFileName(path)} into sprite {id}");
    }
}
