using StoneForge;

// The Esc menu's layout: what mods' calls make of the game's buttons as it made them (laid out without the game).
[Collection("MainMenu")]
public class EscMenuTests : IDisposable
{
    private static readonly ModContext A = new("esca"), B = new("escb");

    public EscMenuTests() => EscMenu.ResetForTests();
    public void Dispose() => EscMenu.ResetForTests();

    private static void Nothing() { }

    [Fact]
    public void A_localized_button_refresh_preserves_order_and_other_mods()
    {
        string text = "Start quest";
        EscMenu.AddButton(A, () => text, Nothing);
        EscMenu.AddButton(B, "Start quest", Nothing);
        text = "Start bounty";
        EscMenu.RefreshLocalizedButtons(A.Id);
        Assert.Equal(new[] { "Resume", "LoadGame", "MessageLog", "Settings", "Start bounty", "Start quest", "SaveAndExit" }, EscMenu.Describe());
    }

    [Fact]
    public void Untouched_it_is_the_games()
        => Assert.Equal(new[] { "Resume", "LoadGame", "MessageLog", "Settings", "SaveAndExit" }, EscMenu.Describe());

    [Fact]
    public void A_client_exits_without_saving()
    {
        EscMenu.RemoveButton(A, EscButton.SaveAndExit);
        EscMenu.AddButton(A, EscButton.Exit);
        Assert.Equal(new[] { "Resume", "LoadGame", "MessageLog", "Settings", "Exit" }, EscMenu.Describe());
    }

    [Fact]
    public void AddButton_goes_above_the_exit_whichever_it_is()
    {
        EscMenu.AddButton(A, "Players", Nothing);
        Assert.Equal(new[] { "Resume", "LoadGame", "MessageLog", "Settings", "Players", "SaveAndExit" }, EscMenu.Describe());
        // (Where the game can't save it shows Exit, and no Load Game in permadeath.)
        var noSave = new[] { EscButton.Resume, EscButton.MessageLog, EscButton.Settings, EscButton.Exit };
        Assert.Equal(new[] { "Resume", "MessageLog", "Settings", "Players", "Exit" }, EscMenu.Describe(noSave));
    }

    [Fact]
    public void Before_and_after_by_name_text_or_another_mods_button()
    {
        EscMenu.AddAfter(A, EscButton.Resume, "Admin", Nothing);
        EscMenu.AddBefore(B, "admin", "Players", Nothing);
        EscMenu.AddAfter(B, "Save & Exit", "Quit", Nothing);
        var french = new Dictionary<EscButton, string> { [EscButton.Settings] = "Options" };
        EscMenu.AddBefore(B, "OPTIONS", "Help", Nothing);
        Assert.Equal(new[] { "Resume", "Players", "Admin", "LoadGame", "MessageLog", "Help", "Settings", "SaveAndExit", "Quit" },
            EscMenu.Describe(texts: french));
    }

    [Fact]
    public void A_removal_waits_for_a_button_added_later_and_a_missing_one_does_nothing()
    {
        EscMenu.RemoveButton(A, "Lobby");
        EscMenu.AddButton(B, "Lobby", Nothing);
        EscMenu.RemoveButton(A, "Nowhere");
        Assert.Equal(EscMenu.Default.Select(b => b.ToString()), EscMenu.Describe());
    }

    [Fact]
    public void Clear_then_the_games_buttons_like_any_and_a_game_button_not_shown_can_be_added()
    {
        EscMenu.ClearButtons(A);
        EscMenu.AddButton(A, EscButton.Resume);
        EscMenu.AddButton(A, "Lobby", Nothing);
        EscMenu.AddButton(A, EscButton.ExitGame);
        Assert.Equal(new[] { "Resume", "Lobby", "ExitGame" }, EscMenu.Describe());
    }

    [Fact]
    public void Restore_undoes_whats_been_done_since_the_mods_loaded_and_a_mod_switched_off_takes_its_own()
    {
        EscMenu.BeginLoad();
        EscMenu.AddButton(A, "Players", Nothing);
        EscMenu.EndLoad();
        EscMenu.RemoveButton(B, EscButton.Settings);
        EscMenu.RestoreButtons(B);
        Assert.Equal(new[] { "Resume", "LoadGame", "MessageLog", "Settings", "Players", "SaveAndExit" }, EscMenu.Describe());
        EscMenu.RemoveMod("esca");
        Assert.Equal(EscMenu.Default.Select(b => b.ToString()), EscMenu.Describe());
    }

    [Fact]
    public void UndoChanges_takes_back_this_mods_changes_only()
    {
        EscMenu.RemoveButton(A, EscButton.SaveAndExit);
        EscMenu.AddButton(A, EscButton.Exit);
        EscMenu.AddButton(B, "Players", Nothing);
        EscMenu.UndoChanges(A);
        Assert.Equal(new[] { "Resume", "LoadGame", "MessageLog", "Settings", "Players", "SaveAndExit" }, EscMenu.Describe());
    }

    [Fact]
    public void Not_a_game_button_throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EscMenu.RemoveButton(A, (EscButton)42));
}
