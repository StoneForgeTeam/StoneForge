using StoneForge;

// A character's look (CharacterLook, LookLayer, CharacterSprites): the player's read from the compositor's globals, sent
// as JSON and back, and built into another character's sprites by the game's compositor - its layers at the origins
// they had where the look was read, and the player's globals and origins put back after (laid out with FakeGame's
// arrays, scripts and sprites).
public class CharacterLookTests : FakeGame
{
    private const int Body = 100, Shirt = 101, Helmet = 102, HelmetMask = 103, Built = 200, BuiltMask = 210;
    private readonly FakeScripts _scripts = new();
    private readonly FakeSprites _sprites = new();
    // What the compositor saw: its layer count, and the shirt's origin while it ran.
    private int _builds, _layersSeen;
    private (double, double) _shirtOriginSeen;

    public CharacterLookTests()
    {
        Refs = new FakeRefs();
        GameScripts = _scripts;
        Sprites = _sprites;
        KeepGlobalWrites = true;
        _sprites.Origins[Body] = (16, 40);
        _sprites.Origins[Shirt] = (10, 20);
        _sprites.Origins[Helmet] = (5, 5);
        _sprites.Origins[HelmetMask] = (1, 1);
        // (As the game's compositor: it reads the globals and fills playerSpriteArray - four rows and a mask.)
        _scripts.Add("scr_playerSpriteUpdate", _ =>
        {
            _builds++;
            _layersSeen = Globals["playerSpritePartsArrayHeight"].AsInt;
            _shirtOriginSeen = _sprites.Origins[Shirt];
            // (Into a copy, as the game's copy on write does: the array it was given stays as it was.)
            Assert.Equal(-4, Globals["playerSpriteArray"].AsArray![0].AsInt);
            var built = GmArray.From(Enumerable.Range(0, 4).Select(i => (GmValue)(Built + i)).Append(BuiltMask));
            Globals["playerSpriteArray"] = built;
            for (int i = 0; i < 4; i++)
                _sprites.Origins[Built + i] = (0, 0);
            _sprites.Origins[BuiltMask] = (0, 0);
            return GmValue.Undefined;
        });
    }

    // A layer as scr_playerSpriteInit makes it: 13 values and two flags.
    private static GmArray Layer(int sprite, int frame, int mask)
    {
        var values = new GmValue[] { sprite, frame, 2, -3, 0, 0, 0, 0, 0, -4, 0, mask, 8192, false, false };
        return GmArray.From(values);
    }

    // The player's look: a shirt and a helmet with its own mask, over the body; 9 frames by 4 rows.
    private void Player()
    {
        Globals["playerSpritePartsArray"] = GmArray.From(new GmValue[] { Layer(Shirt, 0, -4), Layer(Helmet, 2, HelmetMask) });
        Globals["playerSpritePartsArrayHeight"] = 2;
        Globals["playerSpriteImageNumberX"] = 9;
        Globals["playerSpriteImageNumberY"] = 4;
        Globals["playerSpriteGround"] = -4;
        Globals["playerSpriteBody"] = Body;
        Globals["playerSpriteArray"] = GmArray.From(new GmValue[] { 50, 51, 52, 53, 54 });
    }

    [Fact]
    public void The_players_look_is_read_and_goes_out_as_JSON_and_back()
    {
        Player();
        var look = CharacterLook.OfPlayer()!;
        Assert.Equal((9, 4, Body), (look.FramesX, look.FramesY, look.Body.AsInt));
        Assert.Equal(2, look.Layers.Count);
        Assert.Equal((Shirt, 0, -4), (look.Layers[0].Sprite.AsInt, look.Layers[0].Frame.AsInt, look.Layers[0].Mask.AsInt));
        Assert.Equal((10.0, 20.0), look.Layers[0].SpriteOrigin);
        Assert.Equal((0.0, 0.0), look.Layers[0].MaskOrigin);
        Assert.Equal((1.0, 1.0), look.Layers[1].MaskOrigin);

        var back = CharacterLook.FromJson(look.ToJson())!;
        Assert.Equal((look.FramesX, look.FramesY, look.Body, look.Ground), (back.FramesX, back.FramesY, back.Body, back.Ground));
        for (int i = 0; i < look.Layers.Count; i++)
        {
            Assert.Equal(look.Layers[i].Values, back.Layers[i].Values);
            Assert.Equal((look.Layers[i].SpriteOrigin, look.Layers[i].MaskOrigin), (back.Layers[i].SpriteOrigin, back.Layers[i].MaskOrigin));
        }
    }

    [Fact]
    public void A_look_is_built_at_its_own_origins_and_the_players_are_put_back()
    {
        Player();
        var look = CharacterLook.OfPlayer()!;
        // (Here the shirt sits elsewhere - set for another wearer.)
        _sprites.Origins[Shirt] = (0, 0);

        using var sprites = look.Build()!;
        Assert.Equal(1, _builds);
        Assert.Equal(2, _layersSeen);
        Assert.Equal((10.0, 20.0), _shirtOriginSeen);
        Assert.Equal((Built, Built + 3, BuiltMask), (sprites.Normal, sprites.FlashPositive, sprites.Mask));
        // (Ours again: the shirt's origin, and the player's globals - their sprites untouched.)
        Assert.Equal((0.0, 0.0), _sprites.Origins[Shirt]);
        Assert.Equal(Body, Globals["playerSpriteBody"].AsInt);
        Assert.Equal(51, Globals["playerSpriteArray"].AsArray![1].AsInt);
        Assert.Empty(_sprites.Deleted);

        sprites.Dispose();
        Assert.Equal(new[] { Built, Built + 1, Built + 2, Built + 3, BuiltMask }, _sprites.Deleted);
        sprites.Dispose();
        Assert.Equal(5, _sprites.Deleted.Count);
    }

    [Fact]
    public void A_look_whose_body_isnt_in_this_game_isnt_built()
    {
        Player();
        var look = CharacterLook.OfPlayer()! with { Body = 999 };
        Assert.Null(look.Build());
        Assert.Equal(0, _builds);
    }

    [Fact]
    public void Before_a_character_there_is_no_look()
    {
        Assert.Null(CharacterLook.OfPlayer());
        Assert.Null(CharacterLook.FromJson("not json"));
        Assert.Null(CharacterLook.FromJson("""{"framesX": 9, "framesY": 4, "layers": [{"values": [1, 2]}]}"""));
    }

    [Theory]
    [InlineData(0, false, 0)]
    [InlineData(0, true, 1)]
    [InlineData(-6, true, 2)]
    [InlineData(6, false, 3)]
    [InlineData(5, false, 0)]
    public void The_sprite_drawn_is_picked_as_the_player_picks_its_own(double flash, bool blinking, int row)
    {
        Player();
        using var sprites = CharacterLook.OfPlayer()!.Build()!;
        Assert.Equal(Built + row, sprites.For(flash, blinking));
    }
}
