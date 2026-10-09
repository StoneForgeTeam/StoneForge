using StoneForge;

public sealed class SteamTests : FakeGame
{
    [Theory]
    [InlineData("12345")]
    [InlineData("76561197960278073")]
    public void Mod_context_exposes_current_contributor_eligibility(string contributor)
    {
        var context = new ModContext(new ModManifest(new ManifestData("contributor_test", "Test", "1", "", "", null,
            Contributors: new[] { contributor })));
        SteamInitialized = true; SteamAccount = 12345;
        Assert.True(context.IsContributor);
        Assert.False(new ModContext("unlisted_test").IsContributor);
        SteamAccount = 999;
        Assert.False(context.IsContributor);
        SteamAccount = 12345; SteamInitialized = false;
        Assert.False(context.IsContributor);
    }
    [Theory]
    [InlineData(12345, 12345)]
    [InlineData(4294967295, 4294967295)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(4294967296, 0)]
    [InlineData(1.5, 0)]
    public void Account_ID_remains_exact_and_invalid_values_grant_no_identity(double value, long expected)
    {
        SteamInitialized = true; SteamAccount = value;
        Assert.Equal((uint)expected, Steam.AccountId);
    }
    [Fact]
    public void Unavailable_Steam_returns_no_identity()
    {
        SteamInitialized = false; SteamAccount = 12345;
        Assert.Equal(0u, Steam.AccountId);
        Assert.Equal(0, SteamAccountReads);
    }
}
