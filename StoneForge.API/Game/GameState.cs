namespace StoneForge;

public static partial class Game
{
    /// <summary>Whether the game is in the middle of something a mod shouldn't step into - a room change
    /// (o_smoothRoomChanger), a fade (o_black_overlay), a dialogue (o_dialogue) or a cutscene (<see cref="IsCutscene"/>).
    /// A good check before moving the player, changing the room, saving or loading.</summary>
    public static bool IsBusy
        => Exists(GameObjectId.o_smoothRoomChanger) || Exists(GameObjectId.o_black_overlay) || Exists(GameObjectId.o_dialogue)
            || IsCutscene;

    /// <summary>Whether a cutscene is playing, as the game decides it (scr_is_cutscene: the cutscene controller's on, the
    /// UI's hidden, or the screen's faded). Run as the player, as the game's own script needs - it reads the instance's
    /// object_index, and with none it fails. False with no player (the main menu).</summary>
    public static bool IsCutscene
    {
        get
        {
            Instance player = CallBuiltin("instance_find", (int)GameObjectId.o_player, 0);
            return !player.IsNone && CallScript("scr_is_cutscene", player).AsBool;
        }
    }

    private static bool Exists(GameObjectId obj) => CallBuiltin("instance_exists", (int)obj).AsBool;
}
