using StoneForge;

// The main menu's layout: what mods' calls make of the game's four buttons (laid out without the game).
[Collection("MainMenu")]
public class MainMenuTests : IDisposable
{
    private static readonly ModContext A = new("moda"), B = new("modb");

    public MainMenuTests() => MainMenu.ResetForTests();
    public void Dispose() => MainMenu.ResetForTests();

    private static void Nothing() { }

    [Fact]
    public void Untouched_it_is_the_games()
        => Assert.Equal(new[] { "Play", "Settings", "Credits", "Exit" }, MainMenu.Describe());

    [Fact]
    public void AddButton_goes_above_Exit()
    {
        MainMenu.AddButton(A, "Mods+", Nothing);
        Assert.Equal(new[] { "Play", "Settings", "Credits", "Mods+", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void Localized_button_refresh_preserves_other_mods_and_menu_order()
    {
        MainMenu.AddButton(A, "Mods", Nothing);
        MainMenu.AddButton(B, "Mods", Nothing);
        MainMenu.RefreshButtonText(A.Id, "Mods", "Modifications");
        Assert.Equal(new[] { "Play", "Settings", "Credits", "Modifications", "Mods", "Exit" }, MainMenu.Describe());
        MainMenu.RefreshButtonText(A.Id, "Modifications", "Modifications");
        Assert.Equal(6, MainMenu.Describe().Count);
    }

    [Fact]
    public void Before_and_after_a_game_button_by_name_or_Start()
    {
        MainMenu.AddBefore(A, "Start", "Multiplayer", Nothing);
        MainMenu.AddAfter(A, VanillaButton.Settings, "Mods", Nothing);
        Assert.Equal(new[] { "Multiplayer", "Play", "Settings", "Mods", "Credits", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void Anchors_by_shown_text_and_by_another_mods_button()
    {
        MainMenu.AddAfter(A, "jouer", "Multiplayer", Nothing);
        MainMenu.AddAfter(B, "MULTIPLAYER", "Lobby", Nothing);
        var french = new Dictionary<VanillaButton, string> { [VanillaButton.Play] = "Jouer" };
        Assert.Equal(new[] { "Play", "Multiplayer", "Lobby", "Settings", "Credits", "Exit" }, MainMenu.Describe(french));
    }

    [Fact]
    public void An_anchor_added_later_is_waited_for()
    {
        MainMenu.AddAfter(A, "Lobby", "Chat", Nothing);
        MainMenu.AddBefore(B, VanillaButton.Exit, "Lobby", Nothing);
        Assert.Equal(new[] { "Play", "Settings", "Credits", "Lobby", "Chat", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void A_missing_anchor_falls_back_above_Exit()
    {
        MainMenu.AddBefore(A, "Nowhere", "X", Nothing);
        Assert.Equal(new[] { "Play", "Settings", "Credits", "X", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void A_button_is_removed_by_name_and_put_back_by_adding_it()
    {
        MainMenu.RemoveButton(A, VanillaButton.Credits);
        Assert.Equal(new[] { "Play", "Settings", "Exit" }, MainMenu.Describe());
        MainMenu.AddButton(B, VanillaButton.Credits);
        Assert.Equal(new[] { "Play", "Settings", "Credits", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void A_removal_waits_for_another_mods_button_added_later()
    {
        MainMenu.RemoveButton(A, "lobby");
        MainMenu.AddButton(B, "Lobby", Nothing);
        MainMenu.RemoveButton(A, "Nowhere");
        Assert.Equal(new[] { "Play", "Settings", "Credits", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void Clear_empties_it_and_the_games_buttons_are_added_like_any()
    {
        MainMenu.AddButton(A, "Old", Nothing);
        MainMenu.AddButton(B, "Theirs", Nothing);
        MainMenu.ClearButtons(A);
        MainMenu.AddButton(A, VanillaButton.Play);
        MainMenu.AddButton(A, "Multiplayer", Nothing);
        MainMenu.AddButton(A, VanillaButton.Exit);
        MainMenu.AddBefore(A, "Exit", VanillaButton.Settings);
        MainMenu.AddAfter(A, VanillaButton.Play, VanillaButton.Credits);
        Assert.Equal(new[] { "Play", "Credits", "Multiplayer", "Settings", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void Restore_puts_back_the_menu_as_the_mods_loaded_it()
    {
        MainMenu.BeginLoad();
        MainMenu.AddAfter(A, VanillaButton.Play, "Test1", Nothing);
        MainMenu.EndLoad();
        // (Later - a click: cleared, the game's Play put back.)
        MainMenu.ClearButtons(A);
        MainMenu.AddButton(A, VanillaButton.Play);
        Assert.Equal(new[] { "Play" }, MainMenu.Describe());
        MainMenu.RestoreButtons(B);
        Assert.Equal(new[] { "Play", "Test1", "Settings", "Credits", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void A_mod_switched_off_takes_what_it_did_with_it()
    {
        MainMenu.ClearButtons(A);
        MainMenu.AddButton(A, "Only", Nothing);
        MainMenu.AddButton(B, "Theirs", Nothing);
        MainMenu.RemoveMod("moda");
        Assert.Equal(new[] { "Play", "Settings", "Credits", "Theirs", "Exit" }, MainMenu.Describe());
    }

    [Fact]
    public void Back_goes_back_one_menu()
    {
        MainMenu.BeginLoad();
        MainMenu.AddButton(A, "Example Button", Nothing);
        MainMenu.EndLoad();
        MainMenu.ClearButtons(A);
        MainMenu.AddButton(A, VanillaButton.Play);
        MainMenu.AddButton(A, "Play Options", Nothing);
        MainMenu.AddButton(A, VanillaButton.Back);
        MainMenu.ClearButtons(A);
        MainMenu.AddButton(A, VanillaButton.NewGame);
        MainMenu.AddButton(A, VanillaButton.Back);
        Assert.True(MainMenu.UndoLastClear());
        Assert.Equal(new[] { "Play", "Play Options", "Back" }, MainMenu.Describe());
        Assert.True(MainMenu.UndoLastClear());
        Assert.Equal(new[] { "Play", "Settings", "Credits", "Example Button", "Exit" }, MainMenu.Describe());
        // (At the menu as it started: nothing to undo.)
        Assert.False(MainMenu.UndoLastClear());
    }

    [Theory]
    [InlineData("New Game", VanillaButton.NewGame)]
    [InlineData("loadgame", VanillaButton.LoadGame)]
    [InlineData("Continue", VanillaButton.Continue)]
    [InlineData("Start", VanillaButton.Play)]
    public void Game_buttons_are_named_with_or_without_spaces(string name, VanillaButton which)
    {
        MainMenu.ClearButtons(A);
        MainMenu.AddButton(A, which);
        MainMenu.AddBefore(A, name, "Mine", Nothing);
        Assert.Equal(new[] { "Mine", which.ToString() }, MainMenu.Describe());
    }

    [Fact]
    public void Only_the_games_buttons_are_game_buttons()
        => Assert.Throws<ArgumentOutOfRangeException>(() => MainMenu.AddButton(A, (VanillaButton)9));
}
