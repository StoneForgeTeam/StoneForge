namespace StoneForge;

/// <summary>Typed wrappers for common GameMaker built-in functions (anything else: <see cref="Game.CallBuiltin"/>).</summary>
public static class Gm
{
    public static bool InstanceExists(GameObjectId obj) => Game.CallBuiltin("instance_exists", GmValue.From(obj)).AsBool;
    public static int InstanceNumber(GameObjectId obj) => Game.CallBuiltin("instance_number", GmValue.From(obj)).AsInt;

    /// <summary>Creates an instance of <paramref name="obj"/> at (x, y) on depth <paramref name="depth"/>.</summary>
    public static T Create<T>(double x, double y, double depth, GameObjectId obj) where T : GameInstance, new()
        => GameInstance.Wrap<T>(Game.CallBuiltin("instance_create_depth", x, y, depth, GmValue.From(obj)).AsInstance);

    public static bool ObjectIsAncestor(int obj, int parent) => Game.CallBuiltin("object_is_ancestor", obj, parent).AsBool;
    public static bool ObjectIsAncestor(GameObjectId obj, GameObjectId parent) => ObjectIsAncestor((int)obj, (int)parent);
    public static string ObjectGetName(int obj) => Game.CallBuiltin("object_get_name", obj).AsString;

    /// <summary>An asset's index by name (-1 if there's none).</summary>
    public static int AssetGetIndex(string name) => Game.CallBuiltin("asset_get_index", name).AsInt;

    // A struct made with a GML constructor (by its script's name), as `new constructor(args)` makes one: the runner's own
    // @@NewGMLObject@@ - what the game's `new` expressions call - so its statics, its parent constructor, everything is
    // set up as for the game's own.
    internal static GmValue New(string constructor, params GmValue[] args)
    {
        var all = new GmValue[args.Length + 1];
        all[0] = AssetGetIndex(constructor);
        args.CopyTo(all, 1);
        return Game.CallBuiltinTrusted("@@NewGMLObject@@", default, default, all);
    }

    public static void ShowDebugMessage(string text) => Game.CallBuiltin("show_debug_message", text);
    public static int AudioPlaySound(Sound sound, int priority = 1, bool loop = false) => Game.CallBuiltin("audio_play_sound", GmValue.From(sound), priority, loop).AsInt;

    /// <summary>The game's irandom: a whole number from 0 to <paramref name="max"/>, both included. It draws on
    /// the game's own random generator, which the game seeds for its world (levels, chests...): for a mod's own
    /// chances use C#'s <see cref="System.Random"/> (<c>Random.Shared.Next(3)</c>), which leaves the game's rolls
    /// alone.</summary>
    public static int Irandom(int max) => Game.CallBuiltin("irandom", max).AsInt;
    /// <summary>The game's irandom_range: a whole number from <paramref name="min"/> to <paramref name="max"/>, both
    /// included, from the game's own random generator (see <see cref="Irandom"/>; under <see cref="Game.WithSeed"/>, a
    /// number the same in every game).</summary>
    public static int IrandomRange(int min, int max) => Game.CallBuiltin("irandom_range", min, max).AsInt;
    /// <summary>The game's random: 0 up to <paramref name="max"/> (see <see cref="Irandom"/> - prefer
    /// <see cref="System.Random"/> for a mod's own chances).</summary>
    public static double Random(double max) => Game.CallBuiltin("random", max).AsReal;

    /// <summary>Milliseconds since the game started.</summary>
    public static double CurrentTime => Game.Global["current_time"].AsReal;
    /// <summary>The room the game is in.</summary>
    public static int Room => Game.Global["room"].AsInt;
    /// <summary>Whether the game is on its main menu (its room, r_main_menu, or global.mainMenuRoom).</summary>
    public static bool InMainMenu
    {
        get
        {
            int room = Room;
            GmValue menu = Game.Global["mainMenuRoom"];
            return room == (int)global::StoneForge.Room.r_main_menu || room == (int)global::StoneForge.Room.r_main_menu_prologue
                || (menu.Kind == GmKind.Real && room == menu.AsInt);
        }
    }

    /// <summary>Whether a game is being played: there's a player (o_player, or one of the characters' own
    /// objects), off the main menu.</summary>
    public static bool InGame => Game.CallBuiltin("instance_exists", (int)GameObjectId.o_player).AsBool && !InMainMenu;
}
