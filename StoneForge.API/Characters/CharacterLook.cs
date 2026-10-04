using System.Text.Json.Nodes;

namespace StoneForge;

/// <summary>A character's appearance as the game composites the player's sprite from it (scr_playerSpriteUpdate): the
/// layers - body, head, hair, each piece of equipment worn... - each a sprite drawn at an offset, clipped and masked, over
/// a frame grid; and the body and ground sprites. <see cref="OfPlayer"/> reads the player's;
/// <see cref="ToJson"/> / <see cref="FromJson"/> carry it elsewhere (another game: sprite ids are the same in every game
/// from the same game data); <see cref="Build"/> makes another character's sprites from it - a companion, a mannequin,
/// another player - by the game's own compositor.</summary>
public sealed record CharacterLook(int FramesX, int FramesY, GmValue Ground, GmValue Body, IReadOnlyList<LookLayer> Layers)
{
    // The compositor's globals, saved around a build and put back after.
    private static readonly string[] Globals =
    {
        "playerSpritePartsArray", "playerSpritePartsArrayHeight", "playerSpriteImageNumberX", "playerSpriteImageNumberY",
        "playerSpriteGround", "playerSpriteBody", "playerSpriteArray", "playerSpriteUpdate", "playerSpriteSpeed",
        "playerSpriteIndex",
    };

    /// <summary>The player's look as the game holds it (global.playerSpritePartsArray and the rest); null before the game
    /// has one (no character yet).</summary>
    public static CharacterLook? OfPlayer()
    {
        Game.CheckRunning("CharacterLook.OfPlayer");
        using GmArray? parts = Game.Global["playerSpritePartsArray"].AsArray;
        GmValue height = Game.Global["playerSpritePartsArrayHeight"];
        GmValue framesX = Game.Global["playerSpriteImageNumberX"], framesY = Game.Global["playerSpriteImageNumberY"];
        if (parts == null || height.Kind != GmKind.Real || framesX.Kind != GmKind.Real || framesX.AsInt < 1)
            return null;
        var layers = new List<LookLayer>();
        for (int i = 0; i < Math.Min(height.AsInt, parts.Length); i++)
        {
            using GmArray? part = parts[i].AsArray;
            if (part == null || part.Length < LookLayer.ValueCount)
                continue;
            var values = Enumerable.Range(0, LookLayer.ValueCount).Select(j => part[j]).ToArray();
            layers.Add(new LookLayer(values, Origin(values[0]), Origin(values[11])));
        }
        return new CharacterLook(framesX.AsInt, framesY.AsInt, Game.Global["playerSpriteGround"], Game.Global["playerSpriteBody"], layers);
    }

    /// <summary>As JSON, to keep or send.</summary>
    public string ToJson()
    {
        var layers = new JsonArray();
        foreach (var layer in Layers)
        {
            var values = new JsonArray();
            foreach (GmValue value in layer.Values)
                values.Add(value.ToJsonNode());
            layers.Add(new JsonObject
            {
                ["values"] = values,
                ["sprite"] = new JsonArray(layer.SpriteOrigin.X, layer.SpriteOrigin.Y),
                ["mask"] = new JsonArray(layer.MaskOrigin.X, layer.MaskOrigin.Y),
            });
        }
        return new JsonObject
        {
            ["framesX"] = FramesX, ["framesY"] = FramesY,
            ["ground"] = Ground.ToJsonNode(), ["body"] = Body.ToJsonNode(),
            ["layers"] = layers,
        }.ToJsonString();
    }

    /// <summary>A look from <see cref="ToJson"/>'s text; null if it isn't one.</summary>
    public static CharacterLook? FromJson(string json)
    {
        if (GmJson.Parse(json) is not JsonObject o || Number(o["framesX"]) is not { } framesX || Number(o["framesY"]) is not { } framesY
            || o["layers"] is not JsonArray layers)
            return null;
        var read = new List<LookLayer>();
        foreach (var node in layers)
        {
            if (node is not JsonObject layer || layer["values"] is not JsonArray values || values.Count != LookLayer.ValueCount)
                return null;
            read.Add(new LookLayer(values.Select(GmJson.FromNode).ToArray(), Pair(layer["sprite"]), Pair(layer["mask"])));
        }
        return new CharacterLook((int)framesX, (int)framesY, GmJson.FromNode(o["ground"]), GmJson.FromNode(o["body"]), read);
    }

    /// <summary>A character's sprites made from this look by the game's own compositor (scr_playerSpriteUpdate), as the
    /// player's are made: each layer's sprite sits at the origins it had where the look was read while it composites
    /// (equipment's origins are set per wearer, scr_itemCharSpritesInit), then the player's globals and origins are put
    /// back. Call it in a Draw event - it draws to surfaces - and <see cref="CharacterSprites.Dispose"/> the sprites once
    /// they're no longer drawn. Null if the look can't be built (its body sprite isn't in this game).</summary>
    public CharacterSprites? Build()
    {
        Game.CheckRunning("CharacterLook.Build");
        if (FramesX < 1 || FramesY < 1 || Layers.Count == 0 || !SpriteExists(Body))
            return null;
        // (The layers as scr_playerSpriteInit makes them: the values, then two flags.)
        var parts = Layers.Select(layer =>
        {
            var part = GmArray.Create(LookLayer.ValueCount + 2, 0);
            for (int j = 0; j < LookLayer.ValueCount; j++)
                part[j] = layer.Values[j];
            part[LookLayer.ValueCount] = false;
            part[LookLayer.ValueCount + 1] = false;
            return part;
        }).ToList();
        var saved = Globals.ToDictionary(name => name, name => Game.Global[name]);
        // (Origins: ours noted first, put back in reverse - a sprite used by several layers ends as it was.)
        var origins = new List<(GmValue Sprite, double X, double Y)>();
        using var partsArray = GmArray.From(parts.Select(p => (GmValue)p));
        // (Nothing of the player's in it to be freed: the compositor deletes what it replaces.)
        using var built = GmArray.Create(CharacterSprites.Count, -4);
        try
        {
            Game.Global["playerSpriteArray"] = built;
            Game.Global["playerSpritePartsArray"] = partsArray;
            Game.Global["playerSpritePartsArrayHeight"] = parts.Count;
            Game.Global["playerSpriteImageNumberX"] = FramesX;
            Game.Global["playerSpriteImageNumberY"] = FramesY;
            Game.Global["playerSpriteGround"] = Ground;
            Game.Global["playerSpriteBody"] = Body;
            Game.Global["playerSpriteUpdate"] = true;
            foreach (var layer in Layers)
            {
                SetOrigin(origins, layer.Sprite, layer.SpriteOrigin);
                SetOrigin(origins, layer.Mask, layer.MaskOrigin);
            }
            Game.CallScript("scr_playerSpriteUpdate", default);
            // (Read back from the global: writing into the array we gave it, the game copies it first - copy on write.)
            using GmArray? result = Game.Global["playerSpriteArray"].AsArray;
            if (result == null || result.Length < CharacterSprites.Count)
                return null;
            var sprites = Enumerable.Range(0, CharacterSprites.Count).Select(i => result[i].Kind == GmKind.Real ? result[i].AsInt : -4).ToArray();
            return sprites[0] < 0 ? null : new CharacterSprites(sprites);
        }
        finally
        {
            for (int i = origins.Count - 1; i >= 0; i--)
                Game.CallBuiltin("sprite_set_offset", origins[i].Sprite, origins[i].X, origins[i].Y);
            foreach (var (name, value) in saved)
                Game.Global[name] = value;
            foreach (var part in parts)
                part.Dispose();
            foreach (var value in saved.Values)
                (value.AsArray as GmRef ?? value.AsStruct)?.Dispose();
        }
    }

    private static bool SpriteExists(GmValue sprite) => sprite.Kind == GmKind.Real && sprite.AsInt >= 0 && Game.CallBuiltin("sprite_exists", sprite).AsBool;

    // A sprite's origin here (0, 0 for none).
    private static (double X, double Y) Origin(GmValue sprite)
        => SpriteExists(sprite) ? (Game.CallBuiltin("sprite_get_xoffset", sprite).AsReal, Game.CallBuiltin("sprite_get_yoffset", sprite).AsReal) : (0, 0);

    private static void SetOrigin(List<(GmValue, double, double)> origins, GmValue sprite, (double X, double Y) origin)
    {
        if (!SpriteExists(sprite))
            return;
        var (x, y) = Origin(sprite);
        origins.Add((sprite, x, y));
        Game.CallBuiltin("sprite_set_offset", sprite, origin.X, origin.Y);
    }

    private static double? Number(JsonNode? node) => node is JsonValue v && v.TryGetValue(out double d) ? d : null;
    private static (double, double) Pair(JsonNode? node)
        => node is JsonArray a && a.Count == 2 && Number(a[0]) is { } x && Number(a[1]) is { } y ? (x, y) : (0, 0);
}

/// <summary>One layer of a <see cref="CharacterLook"/>: the compositor's values for it (scr_playerSpriteInit's - the
/// sprite, its frame, offset, clipping, clip sprite, mask sprite and mask colour) and the origins its sprite and mask had
/// where the look was read.</summary>
public sealed record LookLayer(IReadOnlyList<GmValue> Values, (double X, double Y) SpriteOrigin, (double X, double Y) MaskOrigin)
{
    internal const int ValueCount = 13;

    /// <summary>Its sprite (-4: none - nothing drawn for it).</summary>
    public GmValue Sprite => Values[0];
    /// <summary>Its frame.</summary>
    public GmValue Frame => Values[1];
    /// <summary>Its mask sprite (-4: its own sprite, in its mask colour).</summary>
    public GmValue Mask => Values[11];
}

/// <summary>A character's sprites built from a <see cref="CharacterLook"/>: the five the game draws the player with
/// (global.playerSpriteArray), each its animation frames. They're the caller's: <see cref="Dispose"/> deletes them.</summary>
public sealed class CharacterSprites : IDisposable
{
    internal const int Count = 5;
    private readonly int[] _sprites;

    internal CharacterSprites(int[] sprites) => _sprites = sprites;

    /// <summary>All five, as the game's playerSpriteArray holds them.</summary>
    public IReadOnlyList<int> All => _sprites;

    /// <summary>Drawn most of the time.</summary>
    public int Normal => _sprites[0];
    /// <summary>Drawn as it blinks (scr_unitBlinkUpdate).</summary>
    public int Blinking => _sprites[1];
    /// <summary>Drawn while its hit flash (diss) is below -5, and above 5.</summary>
    public int FlashNegative => _sprites[2];
    public int FlashPositive => _sprites[3];
    /// <summary>Its mask: the compositor's last row.</summary>
    public int Mask => _sprites[4];

    /// <summary>The sprite to draw now, as o_player picks its own (its Draw Begin): by its hit flash, then
    /// blinking.</summary>
    public int For(double flash, bool blinking)
        => Math.Abs(flash) > 5 ? flash < 0 ? FlashNegative : FlashPositive : blinking ? Blinking : Normal;

    /// <summary>Whether they've been deleted.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>Deletes the sprites (some rows may be the same sprite: each is deleted once).</summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;
        IsDisposed = true;
        if (!Game.Running)
            return;
        foreach (int sprite in _sprites.Distinct())
            if (sprite >= 0 && Game.CallBuiltin("sprite_exists", sprite).AsBool)
                Game.CallBuiltin("sprite_delete", sprite);
    }
}
