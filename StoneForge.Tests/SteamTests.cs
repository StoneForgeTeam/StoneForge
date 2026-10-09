using StoneForge;

public sealed class SteamTests : FakeGame
{
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
