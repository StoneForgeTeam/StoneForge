namespace StoneForge.Tests;

// Arrays and structs as values (GmRef): what crosses to and from the native bridge, and how they compare. Their
// contents go through the game's own built-ins, so those are tested in game.
public class GmRefTests
{
    [Fact]
    public void Arrays_and_structs_are_their_own_kinds()
    {
        GmValue array = new GmArray(1, 100);
        GmValue strukt = new GmStruct(2, 200);
        Assert.Equal(GmKind.Array, array.Kind);
        Assert.Equal(GmKind.Struct, strukt.Kind);
        Assert.NotNull(array.AsArray);
        Assert.Null(array.AsStruct);
        Assert.NotNull(strukt.AsStruct);
        Assert.Null(((GmValue)5.0).AsArray);
        Assert.True(((GmValue)(GmArray?)null).IsUndefined);
    }

    [Fact]
    public void The_same_game_array_is_equal_however_it_was_got()
    {
        // (Two handles on one array: the game's pointer decides.)
        GmValue first = new GmArray(1, 100);
        GmValue again = new GmArray(7, 100);
        GmValue other = new GmArray(3, 300);
        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.NotEqual((GmValue)new GmArray(4, 400), (GmValue)new GmStruct(5, 400));
    }

    [Fact]
    public void They_cross_to_native_by_their_id()
    {
        var strings = new List<IntPtr>();
        NValue array = Game.ToNative(new GmArray(42, 4200), strings);
        NValue strukt = Game.ToNative(new GmStruct(43, 4300), strings);
        Assert.Equal(7, array.Kind);
        Assert.Equal(42, array.Real);
        Assert.Equal((IntPtr)4200, array.Ptr);
        Assert.Equal(8, strukt.Kind);
        Assert.Equal(43, strukt.Real);
    }

    [Fact]
    public void They_come_back_from_native_as_references()
    {
        GmValue array = Game.FromNative(new NValue { Kind = 7, Real = 9, Ptr = 900 });
        GmValue strukt = Game.FromNative(new NValue { Kind = 8, Real = 10, Ptr = 1000 });
        Assert.Equal(GmKind.Array, array.Kind);
        Assert.Equal(GmKind.Struct, strukt.Kind);
        // (Id 0: the bridge couldn't keep it - undefined, not a dead reference.)
        Assert.True(Game.FromNative(new NValue { Kind = 7, Real = 0, Ptr = 900 }).IsUndefined);
    }

    [Fact]
    public void A_disposed_one_cant_be_passed_in()
    {
        var array = new GmArray(11, 1100);
        array.Dispose();
        array.Dispose();
        Assert.Throws<ObjectDisposedException>(() => Game.ToNative(array, new List<IntPtr>()));
    }
}
