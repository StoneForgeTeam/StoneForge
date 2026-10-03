using StoneForge;

// mod.json and how mods' content is named: "examplemod:key" to mods, "examplemod__key" in the game's data.
public class ModIdentityTests
{
    private const string Valid = """{ "id": "examplemod", "name": "Example Mod", "version": "1.0.0", "author": "me", "description": "Hi", "stoneforge": "0.1" }""";

    [Fact]
    public void A_valid_manifest_reads()
    {
        var manifest = ModIdentity.ParseManifest(Valid);
        Assert.Equal(new ManifestData("examplemod", "Example Mod", "1.0.0", "me", "Hi", "0.1"), manifest);
    }

    [Fact]
    public void Only_id_name_and_version_are_required()
        => Assert.Equal(new ManifestData("m", "M", "1", "", "", null), ModIdentity.ParseManifest("""{ "id": "m", "name": "M", "version": "1" }"""));

    [Fact]
    public void Trusted_is_a_boolean_off_by_default()
    {
        Assert.True(ModIdentity.ParseManifest("""{ "id": "m", "name": "M", "version": "1", "trusted": true }""").Trusted);
        Assert.False(ModIdentity.ParseManifest("""{ "id": "m", "name": "M", "version": "1", "trusted": false }""").Trusted);
        Assert.False(ModIdentity.ParseManifest("""{ "id": "m", "name": "M", "version": "1" }""").Trusted);
    }

    [Theory]
    [InlineData("""{ "name": "M", "version": "1" }""")] // no id
    [InlineData("""{ "id": "m", "version": "1" }""")] // no name
    [InlineData("""{ "id": "m", "name": "M" }""")] // no version
    [InlineData("""{ "id": "m", "name": "M", "version": "1", "nmae": "x" }""")] // unknown key
    [InlineData("""{ "id": "m", "name": "M", "version": 1 }""")] // not a string
    [InlineData("""{ "id": "m", "name": "M", "version": "1", "stoneforge": "soon" }""")]
    [InlineData("""{ "id": "m", "name": "M", "version": "1", "trusted": "yes" }""")] // not a boolean
    [InlineData("""[ "id" ]""")]
    [InlineData("""{ "id": "m", """)] // not JSON
    public void Invalid_manifests_say_why(string json)
        => Assert.Throws<InvalidDataException>(() => ModIdentity.ParseManifest(json));

    [Theory]
    [InlineData("examplemod", true)]
    [InlineData("failmelon_examplemod", true)]
    [InlineData("mod2", true)]
    [InlineData("ExampleMod", false)] // upper case
    [InlineData("example_", false)] // trailing _
    [InlineData("a__b", false)] // __ separates the ID from content keys
    [InlineData("2mod", false)]
    [InlineData("failmelon.examplemod", false)]
    [InlineData("example mod", false)]
    public void Mod_IDs_are_lowercase_with_single_underscores(string id, bool valid)
        => Assert.Equal(valid, ModIdentity.IsValidId(id));

    [Theory]
    [InlineData("examplemod:Example Blade", "examplemod__Example Blade")]
    [InlineData("examplemod:example_tonic", "examplemod__example_tonic")]
    [InlineData("Drifter Sword", "Drifter Sword")] // the game's own
    [InlineData("Not A:Mod", "Not A:Mod")] // not a mod ID before the colon
    public void Full_IDs_become_game_keys(string name, string gameKey)
        => Assert.Equal(gameKey, ModIdentity.ToGameKey(name));

    [Theory]
    [InlineData("_hidden")]
    [InlineData("a:b")]
    [InlineData(" ")]
    public void Content_keys_cant_break_the_naming(string key)
        => Assert.Throws<ArgumentException>(() => ModIdentity.CheckKey(key, "item"));

    [Fact]
    public void Different_mods_never_share_a_game_key()
    {
        // (Valid IDs have no "__" and keys don't start with "_", so the first "__" splits a game key unambiguously.)
        Assert.NotEqual(ModIdentity.GameKey("a_b", "c"), ModIdentity.GameKey("a", "b_c"));
    }

    [Theory]
    [InlineData("0.1.0", "0.1", true)]
    [InlineData("0.1.1", "0.1", true)]
    [InlineData("0.0.9", "0.1", false)]
    [InlineData("1.0.0-beta", "0.1.0", true)]
    public void StoneForge_version_requirements(string current, string needed, bool ok)
        => Assert.Equal(ok, ModIdentity.Satisfies(current, needed));
}
