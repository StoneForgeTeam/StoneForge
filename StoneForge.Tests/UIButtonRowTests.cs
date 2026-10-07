using StoneForge;

// A button row's layout: spread, or in a frame's set places with pinned ones (laid out without the game).
public class UIButtonRowTests
{
    private static List<double> Xs(UIButtonRow row)
    {
        typeof(UIButtonRow).GetMethod("Layout", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(row, null);
        return row.Children.Select(c => c.X).ToList();
    }

    private static UIButtonRow Row(int buttons, IReadOnlyList<double>? places = null)
    {
        var row = new UIButtonRow(0, 0, 437) { Positions = places };
        for (int i = 0; i < buttons; i++)
            row.Add(new UIButton { Text = "b" + i });
        return row;
    }

    private static readonly double[] Places = { 0, 133, 235, 337 };

    [Fact]
    public void Places_fill_from_the_left_and_a_pinned_one_keeps_its_own()
    {
        var row = Row(2, Places);
        row.Pin(row.Children[1], 3);
        Assert.Equal(new double[] { 0, 337 }, Xs(row));
    }

    [Fact]
    public void Four_buttons_take_the_four_places()
        => Assert.Equal(Places, Xs(Row(4, Places)));

    [Fact]
    public void More_buttons_than_places_are_spread()
    {
        var xs = Xs(Row(5, Places));
        Assert.Equal(5, xs.Distinct().Count());
        Assert.True(xs.SequenceEqual(xs.OrderBy(x => x)));
    }

    [Fact]
    public void Without_places_they_spread_evenly()
        => Assert.Equal(new double[] { 59, 277 }, Xs(Row(2)));
}
